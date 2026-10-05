using System.Text.Json;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Mimica;

namespace RonatIa.Games.Mimica.Tests;

public sealed class MimicaConfigTests
{
    private static readonly MimicaGame Game = new();

    private static ConfigResult Validate(string? json) =>
        Game.ValidateConfig(json is null ? null : GameJson.Parse(json));

    [Fact]
    public void The_definition_describes_a_two_team_game()
    {
        var d = Game.Definition;

        Assert.Equal("mimica", d.Id);
        Assert.Equal((2, 24, 2, 1), (d.MinPlayers, d.MaxPlayers, d.TeamCount, d.MinPlayersPerTeam));
        Assert.Equal(10, d.ConfigDefaults.GetProperty("rounds").GetInt32());
        Assert.Equal(60, d.ConfigDefaults.GetProperty("turnSeconds").GetInt32());
        Assert.Equal(3, d.ConfigDefaults.GetProperty("lateGraceSeconds").GetInt32());
        Assert.Equal(["expressoes", "famosos", "cotidiano"], d.ConfigDefaults.GetProperty("categories").EnumerateArray().Select(c => c.GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("null")]
    public void No_config_means_the_defaults(string? json)
    {
        var result = Validate(json);

        Assert.True(result.IsValid);
        var config = result.Normalized!.Value;
        Assert.Equal((10, 60, 3), (config.GetProperty("rounds").GetInt32(), config.GetProperty("turnSeconds").GetInt32(), config.GetProperty("lateGraceSeconds").GetInt32()));
        Assert.Equal(3, config.GetProperty("categories").GetArrayLength());
    }

    [Fact]
    public void A_partial_config_keeps_what_was_sent_and_fills_the_rest()
    {
        var config = Validate("""{"rounds":20,"categories":["famosos"],"extra":"ignorado"}""").Normalized!.Value;

        Assert.Equal(20, config.GetProperty("rounds").GetInt32());
        Assert.Equal(60, config.GetProperty("turnSeconds").GetInt32());
        Assert.Equal(["famosos"], config.GetProperty("categories").EnumerateArray().Select(c => c.GetString()));
        Assert.False(config.TryGetProperty("extra", out _)); // o que não é do jogo não fica salvo
    }

    [Theory]
    [InlineData("""{"rounds":0}""", "rounds")]
    [InlineData("""{"rounds":31}""", "rounds")]
    [InlineData("""{"rounds":"dez"}""", "rounds")]
    [InlineData("""{"rounds":2.5}""", "rounds")]
    [InlineData("""{"turnSeconds":5}""", "turnSeconds")]
    [InlineData("""{"turnSeconds":301}""", "turnSeconds")]
    [InlineData("""{"lateGraceSeconds":-1}""", "lateGraceSeconds")]
    [InlineData("""{"lateGraceSeconds":11}""", "lateGraceSeconds")]
    [InlineData("""{"categories":[]}""", "categories")]
    [InlineData("""{"categories":["xadrez"]}""", "categories")]
    [InlineData("""{"categories":["famosos","famosos"]}""", "categories")]
    [InlineData("""{"categories":"famosos"}""", "categories")]
    [InlineData("""{"categories":[1]}""", "categories")]
    [InlineData("""[1,2]""", "config")]
    [InlineData("""42""", "config")]
    public void Invalid_values_are_reported_by_field(string json, string field)
    {
        var result = Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Errors!.Keys);
    }

    [Fact]
    public void Every_error_is_reported_at_once()
    {
        var result = Validate("""{"rounds":99,"turnSeconds":1,"categories":[]}""");

        Assert.Equal(["categories", "rounds", "turnSeconds"], result.Errors!.Keys.Order());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    public void The_allowed_range_of_rounds_includes_the_original_presets(int rounds)
    {
        Assert.True(Validate($$"""{"rounds":{{rounds}}}""").IsValid);
    }

    [Fact]
    public void A_custom_deck_limits_the_themes_offered()
    {
        var content = new MimicaContent([new MimicaCategory("so", "Só um", "", "Tema:", [new MimicaPrompt("so-1", "Única")])]);
        var game = new MimicaGame(content);

        Assert.Equal(["so"], game.Definition.ConfigDefaults.GetProperty("categories").EnumerateArray().Select(c => c.GetString()));
        Assert.False(game.ValidateConfig(GameJson.Parse("""{"categories":["famosos"]}""")).IsValid);
    }
}
