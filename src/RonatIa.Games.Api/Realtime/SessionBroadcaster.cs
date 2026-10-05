using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Sessions;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Api.Realtime;

/// <summary>Um aviso para a fila: a partida mudou (e, se for o caso, o grupo precisa recarregar a lista, ou houve revanche).</summary>
/// <param name="Lifecycle">Criada, começou, terminou, cancelada, entrou/saiu alguém: a lista de partidas do grupo mudou.</param>
public sealed record SessionNotice(Guid SessionId, Guid? GroupId, bool Lifecycle, Guid? RematchSessionId = null);

/// <summary>
/// Fila entre o fim de uma requisição que mudou a partida e o envio aos assinantes. A requisição só enfileira (não espera nem
/// falha por causa do tempo real); o <see cref="SessionBroadcaster"/> envia depois, juntando avisos repetidos da mesma partida.
/// </summary>
public sealed class SessionBroadcastQueue
{
    private readonly Channel<SessionNotice> _channel = Channel.CreateUnbounded<SessionNotice>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<SessionNotice> Reader => _channel.Reader;

    public void Publish(SessionNotice notice) => _channel.Writer.TryWrite(notice);
}

/// <summary>
/// Envia a cada assinante a <b>sua</b> visão da partida depois de cada mudança. Para cada pessoa: confere de novo que ainda é
/// membro do grupo (senão encerra a assinatura e avisa), monta a visão dela com o mesmo código do REST e envia a todas as
/// conexões dela. Segredos de um jogador nunca vão na visão de outro, porque quem decide é a projeção do jogo.
/// </summary>
public sealed class SessionBroadcaster(
    SessionBroadcastQueue queue,
    SessionSubscriptions subscriptions,
    IHubContext<SessionsHub> hub,
    IServiceScopeFactory scopes,
    ILogger<SessionBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var first in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Avisos repetidos da mesma partida viram um só: o estado enviado é sempre o mais recente.
                var batch = new Dictionary<Guid, SessionNotice> { [first.SessionId] = first };
                while (queue.Reader.TryRead(out var next))
                {
                    batch[next.SessionId] = batch.TryGetValue(next.SessionId, out var current) ? Merge(current, next) : next;
                }

                foreach (var notice in batch.Values)
                {
                    try
                    {
                        await ProcessAsync(notice, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception, "Falha ao enviar a atualização da partida {SessionId}", notice.SessionId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // desligando
        }
    }

    private static SessionNotice Merge(SessionNotice older, SessionNotice newer) => new(
        newer.SessionId,
        newer.GroupId ?? older.GroupId,
        older.Lifecycle || newer.Lifecycle,
        newer.RematchSessionId ?? older.RematchSessionId);

    private async Task ProcessAsync(SessionNotice notice, CancellationToken cancellationToken)
    {
        var subscribers = subscriptions.Subscribers(notice.SessionId);

        if (subscribers.Count > 0)
        {
            await using var scope = scopes.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<SessionReader>();
            var logins = scope.ServiceProvider.GetRequiredService<ISessionValidator>();

            foreach (var person in subscribers.GroupBy(s => s.UserId))
            {
                // Logout, "sair de todos os aparelhos" e suspensão valem para conexões já abertas: o aparelho cujo login foi
                // encerrado deixa de receber dados (o JWT dele ainda "vale" até expirar, mas a sessão não).
                var live = new List<string>();
                var revoked = new List<string>();
                foreach (var device in person.GroupBy(s => s.AuthSessionId))
                {
                    var ids = device.Select(s => s.ConnectionId);
                    if (await logins.IsActiveAsync(person.Key, device.Key, cancellationToken))
                    {
                        live.AddRange(ids);
                    }
                    else
                    {
                        revoked.AddRange(ids);
                    }
                }

                try
                {
                    if (live.Count > 0)
                    {
                        var access = await reader.RequireAsync(person.Key, notice.SessionId, cancellationToken);
                        var view = await reader.BuildAsync(access, cancellationToken);
                        await hub.Clients.Clients(live).SendAsync(RealtimeEvents.SessionUpdated, view, cancellationToken);
                    }
                }
                catch (AppException exception) when (exception.Kind == ErrorKind.NotFound)
                {
                    // Saiu do grupo (ou a partida deixou de ser dele): encerra a assinatura sem enviar nada.
                    revoked.AddRange(live);
                }

                foreach (var connectionId in revoked)
                {
                    subscriptions.Remove(notice.SessionId, connectionId);
                }

                if (revoked.Count > 0)
                {
                    await hub.Clients.Clients(revoked).SendAsync(RealtimeEvents.AccessRevoked, new AccessRevokedDto(notice.SessionId), cancellationToken);
                }
            }

            if (notice.RematchSessionId is { } rematch)
            {
                var recipients = subscriptions.Subscribers(notice.SessionId).Select(s => s.ConnectionId).ToList();
                if (recipients.Count > 0)
                {
                    await hub.Clients.Clients(recipients).SendAsync(RealtimeEvents.RematchCreated, new RematchDto(notice.SessionId, rematch), cancellationToken);
                }
            }
        }

        if (notice.Lifecycle && notice.GroupId is { } groupId)
        {
            var listeners = subscriptions.GroupConnections(groupId);
            if (listeners.Count > 0)
            {
                await hub.Clients.Clients(listeners).SendAsync(RealtimeEvents.GroupSessionsChanged, new GroupSessionsChangedDto(groupId), cancellationToken);
            }
        }
    }
}
