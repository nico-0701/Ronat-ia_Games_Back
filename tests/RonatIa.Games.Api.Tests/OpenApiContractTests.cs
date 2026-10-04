using System.Text.Encodings.Web;
using System.Text.Json;
using RonatIa.Games.Api.Tests.Infrastructure;

namespace RonatIa.Games.Api.Tests;

/// <summary>
/// O contrato OpenAPI (<c>docs/openapi/v1.json</c>) é versionado no Git e consumido pelo Front.
/// Este teste falha se a API mudou e o arquivo não foi atualizado, deixando a mudança visível no diff do PR.
/// Para atualizar: <c>UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests</c>.
/// </summary>
public sealed class OpenApiContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task Document_is_served_and_describes_the_api()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        var document = JsonDocument.Parse(json).RootElement;

        Assert.Equal("Ronat-ia Games API", document.GetProperty("info").GetProperty("title").GetString());
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/meta", out _));
        Assert.DoesNotContain("/test/errors", json);
    }

    [Fact]
    public async Task Document_matches_the_committed_contract()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        var current = Normalize(json);
        var path = Path.Combine(RepoPaths.Root, "docs", "openapi", "v1.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, current);
            return;
        }

        Assert.True(File.Exists(path), "docs/openapi/v1.json não existe. Gere com: UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests");
        var committed = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");

        Assert.True(
            committed == current,
            "O contrato OpenAPI mudou. Atualize o arquivo e inclua-o no commit: UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests");
    }

    private static string Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, PrettyJson).ReplaceLineEndings("\n") + "\n";
    }
}
