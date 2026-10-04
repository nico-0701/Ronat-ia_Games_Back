using System.Diagnostics;

namespace RonatIa.Games.Api.Startup;

public static class SecurityHeaders
{
    /// <summary>
    /// Cabeçalhos de segurança básicos e <c>X-Trace-Id</c> em toda resposta. Respostas que definem seu próprio
    /// <c>Cache-Control</c> (ex.: avatares) mantêm o valor; as demais não são armazenadas em cache.
    /// </summary>
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["X-Frame-Options"] = "DENY";
                headers["X-Trace-Id"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers["Cache-Control"] = "no-store";
                }

                return Task.CompletedTask;
            });

            await next();
        });
}
