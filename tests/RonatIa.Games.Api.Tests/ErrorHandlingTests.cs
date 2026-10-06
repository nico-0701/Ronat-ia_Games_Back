using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RonatIa.Games.Api.Tests.Infrastructure;

namespace RonatIa.Games.Api.Tests;

public sealed class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task Unknown_route_returns_problem_details_with_code()
    {
        var response = await factory.CreateClient().GetAsync("/rota/que/nao/existe");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("http.not_found", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Errors_generated_by_the_framework_follow_the_same_shape_as_the_ones_from_the_app()
    {
        var notFound = await factory.CreateClient().GetAsync("/rota/que/nao/existe");
        var unauthorized = await factory.CreateClient().GetAsync("/api/v1/users/me");

        foreach (var (response, code) in new[] { (notFound, "http.not_found"), (unauthorized, "auth.unauthorized") })
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal($"urn:ronat-ia:error:{code}", problem.GetProperty("type").GetString());
            Assert.Equal(code, problem.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()), "todo erro traz um detail em português");
        }

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Theory]
    [InlineData("/test/errors/not-found", HttpStatusCode.NotFound, "test.not_found")]
    [InlineData("/test/errors/conflict", HttpStatusCode.Conflict, "test.conflict")]
    [InlineData("/test/errors/forbidden", HttpStatusCode.Forbidden, "test.forbidden")]
    [InlineData("/test/errors/validation", HttpStatusCode.BadRequest, "test.invalid")]
    public async Task App_exceptions_map_to_status_and_stable_code(string path, HttpStatusCode status, string code)
    {
        var response = await factory.CreateClient().GetAsync(path);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Validation_exception_exposes_errors_per_field()
    {
        var response = await factory.CreateClient().GetAsync("/test/errors/validation");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Nome muito curto.", problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Unexpected_exception_returns_generic_500_without_leaking_details()
    {
        var response = await factory.CreateClient().GetAsync("/test/errors/boom");
        var raw = await response.Content.ReadAsStringAsync();
        var problem = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("server.error", problem.GetProperty("code").GetString());
        Assert.DoesNotContain("segredo-que-nao-pode-vazar", raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("   at ", raw);
    }

    [Fact]
    public async Task Invalid_body_returns_validation_problem_with_field_errors()
    {
        using var content = new StringContent("""{"name":"ab"}""", Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/test/errors/validate", content);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("Name", out _) || problem.GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Malformed_json_returns_400_problem_details()
    {
        using var content = new StringContent("{ isto nao e json", Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/test/errors/validate", content);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());
    }
}
