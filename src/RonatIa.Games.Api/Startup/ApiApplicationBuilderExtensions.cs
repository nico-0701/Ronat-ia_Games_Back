using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using RonatIa.Games.Api.Realtime;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

namespace RonatIa.Games.Api.Startup;

public static class ApiApplicationBuilderExtensions
{
    /// <summary>Monta o pipeline HTTP. A ordem importa: cabeçalhos encaminhados → logs → erros → CORS → autenticação → limites → autorização → endpoints.</summary>
    public static WebApplication UseApi(this WebApplication app)
    {
        var configuration = app.Configuration;
        var environment = app.Environment;

        if (configuration.GetValue("ForwardedHeaders:Enabled", !environment.IsDevelopment()))
        {
            app.UseForwardedHeaders();
        }

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, exception) =>
                exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
                : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
        });

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseApiSecurityHeaders();

        if (!environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseRouting();
        app.UseCors(ApiCors.PolicyName);
        app.UseAuthentication();

        // Depois da autenticação, para que limites por usuário enxerguem quem é.
        if (configuration.GetValue("RateLimiting:Enabled", true))
        {
            app.UseRateLimiter();
        }

        app.UseAuthorization();

        // Negado por padrão: todo controller exige login, salvo [AllowAnonymous] explícito.
        app.MapControllers().RequireAuthorization();

        // Tempo real das partidas. O token vai na query (access_token) só aqui, porque o WebSocket não envia cabeçalho; a conexão
        // é encerrada quando o token expira (o cliente reconecta com o token renovado).
        app.MapHub<SessionsHub>("/hubs/sessions", options =>
            {
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                options.CloseOnAuthenticationExpiration = true;
            })
            .RequireAuthorization();

        // Saúde: "live" só diz que o processo responde; "ready" executa as verificações marcadas com a tag "ready".
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous().DisableRateLimiting();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous().DisableRateLimiting();

        if (environment.IsDevelopment() || configuration.GetValue("Docs:Enabled", false))
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference(options => options.WithTitle("Ronat-ia Games API")).AllowAnonymous();
        }

        return app;
    }
}
