using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RonatIa.Games.Application.Sessions;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>Uma mensagem recebida do hub, com o JSON original (para conferir que um segredo não está nele).</summary>
public sealed record LiveMessage(string Event, JsonElement Payload)
{
    public string Raw => Payload.GetRawText();

    public T As<T>() => JsonSerializer.Deserialize<T>(Raw, TestJson.Options)!;
}

/// <summary>Uma conexão SignalR de teste, de uma pessoa, com as mensagens guardadas em ordem para serem esperadas com limite de tempo.</summary>
public sealed class LiveConnection : IAsyncDisposable
{
    private static readonly string[] Events = ["SessionUpdated", "PresenceChanged", "RematchCreated", "AccessRevoked", "GroupSessionsChanged"];

    private readonly HubConnection _connection;
    private readonly Channel<LiveMessage> _channel = Channel.CreateUnbounded<LiveMessage>();
    private readonly List<LiveMessage> _buffer = [];

    private LiveConnection(HubConnection connection)
    {
        _connection = connection;
        foreach (var name in Events)
        {
            connection.On<JsonElement>(name, payload => _channel.Writer.TryWrite(new LiveMessage(name, payload.Clone())));
        }
    }

    /// <summary>Todas as mensagens já recebidas e ainda não consumidas.</summary>
    public IReadOnlyList<LiveMessage> Pending => _buffer;

    public static HubConnection Build(WebApplicationFactory<Program> factory, string? accessToken, HttpTransportType transport = HttpTransportType.LongPolling, Uri? baseAddress = null) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress ?? factory.Server.BaseAddress, "/hubs/sessions"), options =>
            {
                options.Transports = transport;
                if (baseAddress is null)
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                }

                options.AccessTokenProvider = () => Task.FromResult(accessToken);
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
            .Build();

    public static async Task<LiveConnection> ConnectAsync(WebApplicationFactory<Program> factory, Person person, HttpTransportType transport = HttpTransportType.LongPolling, Uri? baseAddress = null)
    {
        var live = new LiveConnection(Build(factory, person.Auth.AccessToken, transport, baseAddress));
        await live._connection.StartAsync();
        return live;
    }

    public async Task<SubscribeResultView> SubscribeAsync(Guid sessionId)
    {
        var raw = await _connection.InvokeAsync<JsonElement>("Subscribe", sessionId);
        return new SubscribeResultView(
            JsonSerializer.Deserialize<GameSessionDto>(raw.GetProperty("session").GetRawText(), TestJson.Options)!,
            raw.GetProperty("onlineMemberIds").EnumerateArray().Select(e => e.GetGuid()).ToList(),
            raw.GetProperty("session").GetRawText());
    }

    public Task UnsubscribeAsync(Guid sessionId) => _connection.InvokeAsync("Unsubscribe", sessionId);

    public Task SubscribeGroupAsync(Guid groupId) => _connection.InvokeAsync("SubscribeGroup", groupId);

    public Task UnsubscribeGroupAsync(Guid groupId) => _connection.InvokeAsync("UnsubscribeGroup", groupId);

    public Task StopAsync() => _connection.StopAsync();

    /// <summary>Espera a próxima mensagem do tipo pedido (e que satisfaça o filtro); as demais ficam guardadas para as próximas esperas.</summary>
    public async Task<LiveMessage> NextAsync(string eventName, Func<LiveMessage, bool>? where = null, int timeoutMs = 6000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            var index = _buffer.FindIndex(m => m.Event == eventName && (where?.Invoke(m) ?? true));
            if (index >= 0)
            {
                var message = _buffer[index];
                _buffer.RemoveAt(index);
                return message;
            }

            try
            {
                _buffer.Add(await _channel.Reader.ReadAsync(timeout.Token));
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Nenhuma mensagem \"{eventName}\" em {timeoutMs} ms. Recebidas e não consumidas: [{string.Join(", ", _buffer.Select(m => m.Event))}].");
            }
        }
    }

    /// <summary>Verdadeiro se, durante <paramref name="quietMs"/>, não chegou nenhuma mensagem do tipo pedido.</summary>
    public async Task<bool> GetsNoneAsync(string eventName, int quietMs = 700)
    {
        try
        {
            await NextAsync(eventName, timeoutMs: quietMs);
            return false;
        }
        catch (TimeoutException)
        {
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _connection.DisposeAsync();
        }
        catch (Exception)
        {
            // a conexão já pode ter caído
        }
    }
}

public sealed record SubscribeResultView(GameSessionDto Session, IReadOnlyList<Guid> OnlineMemberIds, string RawSession);
