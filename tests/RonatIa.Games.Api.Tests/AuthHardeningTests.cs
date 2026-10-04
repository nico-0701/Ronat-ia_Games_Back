using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Infrastructure.Security;

namespace RonatIa.Games.Api.Tests;

/// <summary>Limites de taxa e recusa de configuração insegura na subida.</summary>
public sealed class AuthHardeningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_attempts_are_rate_limited_per_client()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:AuthLoginPerMinute", "3"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.LoginRawAsync(TestPhones.Next());
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
        Assert.True(last!.Headers.Contains("Retry-After"));
        Assert.Equal("rate_limit.exceeded", await last.ReadCodeAsync());

        // Saúde e meta não entram na conta do login.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/meta")).StatusCode);
    }

    [Fact]
    public async Task Registrations_are_rate_limited_per_client()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:AuthRegisterPerHour", "2"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/register",
                new { phone = TestPhones.Next(), displayName = $"Pessoa {i + 10}", acceptTerms = true });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public void The_api_refuses_to_start_without_a_phone_pepper()
    {
        using var broken = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", "Host=localhost");
            builder.UseEnvironment("Testing");
            builder.UseSetting("Auth:PhonePepper", "");
            builder.UseSetting("Jwt:SigningKey", factory.JwtSigningKey);
        });

        var exception = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        Assert.Contains("Auth:PhonePepper", Flatten(exception));
    }

    [Fact]
    public void The_api_refuses_to_start_with_a_short_jwt_key()
    {
        using var broken = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", "Host=localhost");
            builder.UseSetting("Auth:PhonePepper", factory.PhonePepper);
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[8]));
        });

        var exception = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        Assert.Contains("Jwt:SigningKey", Flatten(exception));
    }

    [Fact]
    public void The_public_development_secrets_are_refused_outside_development()
    {
        using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default", "Host=localhost");
            builder.UseSetting("Auth:PhonePepper", DevelopmentSecrets.PhonePepper);
            builder.UseSetting("Jwt:SigningKey", DevelopmentSecrets.JwtSigningKey);
        });

        var exception = Assert.ThrowsAny<Exception>(() => production.CreateClient());
        var message = Flatten(exception);

        Assert.Contains("desenvolvimento", message);
        Assert.Contains("Auth:PhonePepper", message);
        Assert.Contains("Jwt:SigningKey", message);
    }

    [Fact]
    public void The_development_settings_file_uses_exactly_the_public_development_secrets()
    {
        var path = Path.Combine(RepoPaths.Root, "src", "RonatIa.Games.Api", "appsettings.Development.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal(DevelopmentSecrets.PhonePepper, document.RootElement.GetProperty("Auth").GetProperty("PhonePepper").GetString());
        Assert.Equal(DevelopmentSecrets.JwtSigningKey, document.RootElement.GetProperty("Jwt").GetProperty("SigningKey").GetString());
    }

    [Fact]
    public void Production_settings_files_do_not_ship_real_secrets()
    {
        var path = Path.Combine(RepoPaths.Root, "src", "RonatIa.Games.Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.False(document.RootElement.TryGetProperty("Jwt", out _));
        Assert.False(document.RootElement.GetProperty("Auth").TryGetProperty("PhonePepper", out _));
        Assert.False(document.RootElement.TryGetProperty("ConnectionStrings", out _));
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is OptionsValidationException validation)
            {
                messages.AddRange(validation.Failures);
            }
        }

        return string.Join(" | ", messages);
    }
}
