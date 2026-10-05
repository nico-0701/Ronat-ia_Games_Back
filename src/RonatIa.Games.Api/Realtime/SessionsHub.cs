using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Application.Sessions;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Api.Realtime;

/// <summary>
/// Tempo real das partidas (<c>/hubs/sessions</c>). O hub só <b>avisa e entrega a visão</b>: quem age continua usando o REST
/// (<c>POST /sessions/{id}/actions</c>), então a autorização, a idempotência e a concorrência têm um caminho só.
/// O cliente assina uma partida com <see cref="Subscribe"/> (recebe a visão atual) e passa a receber
/// <see cref="RealtimeEvents.SessionUpdated"/> a cada mudança, <b>sempre com a visão própria dele</b>. Ao reconectar,
/// assina de novo: a resposta já traz o estado completo (não há "mensagens perdidas" para repor).
/// </summary>
[Authorize]
public sealed class SessionsHub(
    SessionSubscriptions subscriptions,
    SessionLobbyService lobby,
    GroupAccess groups) : Hub
{
    /// <summary>
    /// Assina a partida. Quem não é membro do grupo dela recebe o mesmo <c>session.not_found</c> do REST. Devolve a visão
    /// atual e quem já está online; os demais assinantes são avisados da presença.
    /// </summary>
    public async Task<SubscribeResult> Subscribe(Guid sessionId)
    {
        var snapshot = await lobby.GetAsync(Context.User!.RequireUserId(), sessionId, Context.ConnectionAborted);

        var subscriber = new SessionSubscriptions.Subscriber(Context.ConnectionId, Context.User!.RequireUserId(), snapshot.MyMemberId, Context.User!.RequireSessionId());
        if (!subscriptions.TryAdd(sessionId, subscriber, out var cameOnline))
        {
            throw new HubException("realtime.too_many_subscriptions: Esta conexão já assina partidas demais.");
        }

        if (cameOnline)
        {
            var others = subscriptions.Subscribers(sessionId).Where(s => s.ConnectionId != Context.ConnectionId).Select(s => s.ConnectionId).ToList();
            if (others.Count > 0)
            {
                await Clients.Clients(others).SendAsync(RealtimeEvents.PresenceChanged, new PresenceDto(sessionId, snapshot.MyMemberId, Online: true));
            }
        }

        return new SubscribeResult(snapshot, subscriptions.OnlineMembers(sessionId));
    }

    public async Task Unsubscribe(Guid sessionId)
    {
        if (subscriptions.Remove(sessionId, Context.ConnectionId) is { } change)
        {
            await NotifyPresenceAsync(change);
        }
    }

    /// <summary>Assina os avisos de "a lista de partidas do grupo mudou" (para a tela do grupo recarregar a lista).</summary>
    public async Task SubscribeGroup(Guid groupId)
    {
        var userId = Context.User!.RequireUserId();
        await groups.RequireMemberAsync(userId, groupId, Context.ConnectionAborted);

        if (!subscriptions.TryAddGroup(groupId, Context.ConnectionId, userId))
        {
            throw new HubException("realtime.too_many_subscriptions: Esta conexão já assina partidas demais.");
        }
    }

    public Task UnsubscribeGroup(Guid groupId)
    {
        subscriptions.RemoveGroup(groupId, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var change in subscriptions.RemoveConnection(Context.ConnectionId))
        {
            await NotifyPresenceAsync(change);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task NotifyPresenceAsync(SessionSubscriptions.PresenceChange change)
    {
        var recipients = subscriptions.Subscribers(change.SessionId).Select(s => s.ConnectionId).ToList();
        if (recipients.Count > 0)
        {
            await Clients.Clients(recipients).SendAsync(RealtimeEvents.PresenceChanged, new PresenceDto(change.SessionId, change.MemberId, change.Online));
        }
    }
}

/// <summary>Erros esperados (<see cref="AppException"/>) chegam ao cliente como <c>HubException</c> "código: mensagem", o mesmo <c>code</c> do REST.</summary>
public sealed class AppExceptionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (AppException exception)
        {
            throw new HubException($"{exception.Code}: {exception.Message}");
        }
    }
}
