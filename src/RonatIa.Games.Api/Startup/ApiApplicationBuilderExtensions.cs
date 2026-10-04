using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

namespace RonatIa.Games.Api.Startup;

public static class ApiApplicationBuilderExtensions
{
    /// <summary>Monta o pipeline HTTP. A ordem importa: cabeçalhos encaminhados → logs → erros → CORS → limites → endpoints.</summary>
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

        if (configuration.GetValue("RateLimiting:Enabled", true))
        {
            app.UseRateLimiter();
        }

        app.MapControllers();

        // Saúde: "live" só diz que o processo responde; "ready" executa as verificações marcadas com a tag "ready".
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableRateLimiting();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting();

        if (environment.IsDevelopment() || configuration.GetValue("Docs:Enabled", false))
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options => options.WithTitle("Ronat-ia Games API"));
        }

        return app;
    }
}
