using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Infrastructure.Security;

namespace RonatIa.Games.Infrastructure.Tests;

public sealed class TurnstileVerifierTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return await respond(request);
        }
    }

    private static (TurnstileVerifier Verifier, StubHandler Handler) Create(string? secret, Func<HttpRequestMessage, Task<HttpResponseMessage>>? respond = null)
    {
        var handler = new StubHandler(respond ?? (_ => Task.FromResult(Json("""{"success":true}"""))));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/") };
        var options = Options.Create(new TurnstileOptions { SecretKey = secret });
        return (new TurnstileVerifier(http, options, NullLogger<TurnstileVerifier>.Instance), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_secret_the_verification_is_off_and_nothing_is_called(string? secret)
    {
        var (verifier, handler) = Create(secret);

        Assert.False(verifier.IsEnabled);
        Assert.True(await verifier.VerifyAsync(null, null, CancellationToken.None));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task With_a_secret_a_missing_token_fails_without_calling_cloudflare()
    {
        var (verifier, handler) = Create("segredo");

        Assert.True(verifier.IsEnabled);
        Assert.False(await verifier.VerifyAsync(null, "1.2.3.4", CancellationToken.None));
        Assert.False(await verifier.VerifyAsync("  ", "1.2.3.4", CancellationToken.None));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task A_successful_response_passes_and_sends_secret_token_and_ip()
    {
        var (verifier, handler) = Create("segredo-do-servidor");

        var ok = await verifier.VerifyAsync("token-do-cliente", "203.0.113.7", CancellationToken.None);

        Assert.True(ok);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Request.Method);
        Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", call.Request.RequestUri!.ToString());
        Assert.Contains("secret=segredo-do-servidor", call.Body);
        Assert.Contains("response=token-do-cliente", call.Body);
        Assert.Contains("remoteip=203.0.113.7", call.Body);
    }

    [Fact]
    public async Task The_remote_ip_is_optional()
    {
        var (verifier, handler) = Create("segredo");

        await verifier.VerifyAsync("token", null, CancellationToken.None);

        Assert.DoesNotContain("remoteip", handler.Calls.Single().Body);
    }

    [Fact]
    public async Task A_negative_answer_fails()
    {
        var (verifier, _) = Create("segredo", _ => Task.FromResult(Json("""{"success":false,"error-codes":["invalid-input-response"]}""")));

        Assert.False(await verifier.VerifyAsync("token", null, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task An_http_error_fails_closed(HttpStatusCode status)
    {
        var (verifier, _) = Create("segredo", _ => Task.FromResult(Json("""{"success":true}""", status)));

        Assert.False(await verifier.VerifyAsync("token", null, CancellationToken.None));
    }

    [Fact]
    public async Task A_network_failure_or_garbage_response_fails_closed()
    {
        var (network, _) = Create("segredo", _ => throw new HttpRequestException("sem rede"));
        var (garbage, _) = Create("segredo", _ => Task.FromResult(Json("isto não é json")));

        Assert.False(await network.VerifyAsync("token", null, CancellationToken.None));
        Assert.False(await garbage.VerifyAsync("token", null, CancellationToken.None));
    }
}
