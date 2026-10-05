using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RonatIa.Games.Api.Startup;

namespace RonatIa.Games.Api.Realtime;

/// <summary>
/// Limite de chamadas por conexão no hub (o limitador de requisições do ASP.NET não enxerga mensagens de uma conexão já
/// aberta). Janela fixa de 1 minuto; passou do limite, a chamada falha com <c>rate_limit.exceeded</c> até a janela virar.
/// </summary>
public sealed class HubRateLimitFilter(IOptions<RateLimitingSettings> options, TimeProvider time) : IHubFilter
{
    private const string StateKey = "hub-rate-limit";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private sealed class State
    {
        public DateTimeOffset WindowStart { get; set; }

        public int Count { get; set; }
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var settings = options.Value;
        if (settings.Enabled)
        {
            var now = time.GetUtcNow();
            var items = invocationContext.Context.Items;
            if (!items.TryGetValue(StateKey, out var existing) || existing is not State state)
            {
                state = new State { WindowStart = now };
                items[StateKey] = state;
            }

            // Uma conexão processa uma chamada por vez, então o contador não precisa de lock.
            if (now - state.WindowStart >= Window)
            {
                state.WindowStart = now;
                state.Count = 0;
            }

            if (++state.Count > Math.Max(1, settings.HubInvocationsPerMinute))
            {
                throw new HubException("rate_limit.exceeded: Muitas chamadas em pouco tempo. Aguarde um instante.");
            }
        }

        return await next(invocationContext);
    }
}
