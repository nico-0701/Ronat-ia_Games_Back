using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Infrastructure.Security;

namespace RonatIa.Games.Api.Startup;

public static class ApiAuthentication
{
    public const string AdminPolicy = "admin";

    /// <summary>
    /// JWT Bearer com algoritmo fixo (HS256), emissor e audiência validados e relógio injetado. Todo controller exige
    /// autenticação por padrão (<c>MapControllers().RequireAuthorization()</c>): endpoints públicos precisam de
    /// <c>[AllowAnonymous]</c> explícito.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configuração preguiçosa (IOptions): as chaves só são lidas na primeira requisição, depois da configuração final do host.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwtOptions, time) =>
            {
                var jwt = jwtOptions.Value;

                bearer.MapInboundClaims = false;
                bearer.SaveToken = false;
                bearer.RequireHttpsMetadata = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = JwtKeys.ForValidation(jwt),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = jwt.ClockSkew,
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                        IsWithinLifetime(notBefore, expires, time.GetUtcNow(), parameters.ClockSkew),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };

                bearer.Events = new JwtBearerEvents
                {
                    // O WebSocket do SignalR não consegue enviar o cabeçalho Authorization: o token vai na query, só para /hubs.
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },

                    // Logout, "sair de todos os aparelhos" e suspensão valem na hora, sem esperar o JWT expirar.
                    OnTokenValidated = async context =>
                    {
                        var userId = context.Principal?.GetUserId();
                        var sessionId = context.Principal?.GetSessionId();
                        if (userId is null || sessionId is null)
                        {
                            context.Fail("Token sem identificação de conta e sessão.");
                            return;
                        }

                        var validator = context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>();
                        if (!await validator.IsActiveAsync(userId.Value, sessionId.Value, context.HttpContext.RequestAborted))
                        {
                            context.Fail("Sessão encerrada.");
                        }
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole("admin"));

        return services;
    }

    private static bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, DateTimeOffset now, TimeSpan skew)
    {
        if (expires is null)
        {
            return false;
        }

        var utcNow = now.UtcDateTime;
        if (notBefore is { } start && start > utcNow + skew)
        {
            return false;
        }

        return expires.Value >= utcNow - skew;
    }
}
