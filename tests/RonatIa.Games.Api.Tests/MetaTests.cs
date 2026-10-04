using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RonatIa.Games.Api.Tests.Infrastructure;

namespace RonatIa.Games.Api.Tests;

public sealed class MetaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Meta_returns_api_version_and_server_time()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/meta");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v1", body.GetProperty("apiVersion").GetString());
        Assert.Equal("0.0.0", body.GetProperty("minClientVersion").GetString());
        var serverTime = body.GetProperty("serverTimeUtc").GetDateTimeOffset();
        Assert.InRange(serverTime, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_trace_id()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/meta");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("X-Trace-Id").Single()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Cors_allows_configured_origin_and_blocks_others()
    {
        var client = factory.CreateClient();

        var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        allowed.Headers.Add("Origin", "https://app.exemplo.test");
        var allowedResponse = await client.SendAsync(allowed);

        var preview = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        preview.Headers.Add("Origin", "https://pr-12.exemplo.pages.dev");
        var previewResponse = await client.SendAsync(preview);

        var blocked = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        blocked.Headers.Add("Origin", "https://malicioso.example");
        var blockedResponse = await client.SendAsync(blocked);

        Assert.Equal("https://app.exemplo.test", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("https://pr-12.exemplo.pages.dev", previewResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(blockedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
