using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Infrastructure.Persistence;
using RonatIa.Games.Infrastructure.Security;

namespace RonatIa.Games.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra persistência, segurança e demais integrações externas.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // A conexão é lida quando o DbContext é criado (e não aqui), para que configurações de teste e variáveis de ambiente valham.
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("A string de conexão ConnectionStrings:Default não está configurada.");

            PersistenceConfiguration.Configure(options, connectionString);
        });

        services.AddScoped<IAppDbContext>(serviceProvider => serviceProvider.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDbExceptionClassifier, NpgsqlExceptionClassifier>();

        // Opções validadas na subida: segredo ausente/fraco ou valor de desenvolvimento em produção impede a API de iniciar.
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateOnStart();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();
        services.AddOptions<RegistrationOptions>().Bind(configuration.GetSection(RegistrationOptions.SectionName));
        services.AddOptions<TurnstileOptions>().Bind(configuration.GetSection(TurnstileOptions.SectionName));
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddSingleton<IPhoneNormalizer, LibPhoneNumberNormalizer>();
        services.AddSingleton<IPhoneHasher, HmacPhoneHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddMemoryCache();
        services.AddScoped<ISessionValidator, SessionValidator>();

        services.AddHttpClient<TurnstileVerifier>(client =>
        {
            client.BaseAddress = new Uri("https://challenges.cloudflare.com/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddTransient<ICaptchaVerifier>(serviceProvider => serviceProvider.GetRequiredService<TurnstileVerifier>());

        return services;
    }
}
