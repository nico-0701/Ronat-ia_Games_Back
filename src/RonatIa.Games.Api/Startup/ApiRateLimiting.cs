using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace RonatIa.Games.Api.Startup;

public static class RateLimitPolicies
{
    public const string AuthLogin = "auth-login";
    public const string AuthRegister = "auth-register";
    public const string AuthRefresh = "auth-refresh";
    public const string Upload = "upload";
    public const string Export = "export";
    public const string GroupCreate = "group-create";
    public const string GroupJoin = "group-join";
    public const string SessionCreate = "session-create";
    public const string SessionAction = "session-action";
}

public static class ApiRateLimiting
{
    /// <summary>
    /// Teto global por IP e políticas próprias para os endpoints sensíveis. Os limites são lidos de forma preguiçosa
    /// (seção <c>RateLimiting</c>); para desligar tudo (testes), use <c>RateLimiting:Enabled=false</c>.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 600,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.AddPolicy(RateLimitPolicies.AuthLogin, context =>
                PerIp(context, settings => settings.AuthLoginPerMinute, TimeSpan.FromMinutes(1)));

            options.AddPolicy(RateLimitPolicies.AuthRegister, context =>
                PerIp(context, settings => settings.AuthRegisterPerHour, TimeSpan.FromHours(1)));

            options.AddPolicy(RateLimitPolicies.AuthRefresh, context =>
                PerIp(context, settings => settings.AuthRefreshPerMinute, TimeSpan.FromMinutes(1)));

            options.AddPolicy(RateLimitPolicies.Upload, context =>
                PerUser(context, settings => settings.UploadPerHour, TimeSpan.FromHours(1)));

            options.AddPolicy(RateLimitPolicies.Export, context =>
                PerUser(context, settings => settings.ExportPerHour, TimeSpan.FromHours(1)));

            options.AddPolicy(RateLimitPolicies.GroupCreate, context =>
                PerUser(context, settings => settings.GroupCreatePerHour, TimeSpan.FromHours(1)));

            // Entrar e conferir a senha compartilham a cota: é a barreira contra adivinhar senhas de grupo.
            options.AddPolicy(RateLimitPolicies.GroupJoin, context =>
                PerUser(context, settings => settings.GroupJoinPerMinute, TimeSpan.FromMinutes(1)));

            options.AddPolicy(RateLimitPolicies.SessionCreate, context =>
                PerUser(context, settings => settings.SessionCreatePerHour, TimeSpan.FromHours(1)));

            options.AddPolicy(RateLimitPolicies.SessionAction, context =>
                PerUser(context, settings => settings.SessionActionPerMinute, TimeSpan.FromMinutes(1)));
        });

        return services;
    }

    /// <summary>IP do cliente (já ajustado pelo middleware de cabeçalhos encaminhados).</summary>
    public static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> PerIp(HttpContext context, Func<RateLimitingSettings, int> limit, TimeSpan window)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limit(settings)),
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    /// <summary>Limite por pessoa autenticada (cai para o IP se, por algum motivo, não houver usuário).</summary>
    private static RateLimitPartition<string> PerUser(HttpContext context, Func<RateLimitingSettings, int> limit, TimeSpan window)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
        var key = context.User.GetUserId()?.ToString() ?? ClientKey(context);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limit(settings)),
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Muitas requisições",
            Detail = "Foram feitas requisições demais em pouco tempo. Aguarde um instante e tente de novo.",
        };
        problem.Extensions["code"] = "rate_limit.exceeded";

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }
}
