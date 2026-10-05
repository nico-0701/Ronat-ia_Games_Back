using System.Text.Json;
using RonatIa.Games.Abstractions;

namespace RonatIa.Games.Abstractions.Tests;

public sealed class ContractTests
{
    private sealed record Sample(int Count, string Name, Guid Id, SampleKind Kind);

    private enum SampleKind
    {
        FirstOne,
        Second,
    }

    [Fact]
    public void State_round_trips_through_json_with_camel_case_names_and_string_enums()
    {
        var original = new Sample(3, "teste", Guid.NewGuid(), SampleKind.FirstOne);

        var state = GameState.From(2, original);
        var restored = state.Read<Sample>();

        Assert.Equal(2, state.SchemaVersion);
        Assert.Equal(original, restored);
        Assert.Equal("firstOne", state.Data.GetProperty("kind").GetString());
        Assert.Equal(3, state.Data.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Reading_an_empty_state_fails_loudly()
    {
        var state = new GameState(1, GameJson.Parse("null"));

        Assert.Throws<InvalidOperationException>(() => state.Read<Sample>());
    }

    [Fact]
    public void The_payload_is_read_as_the_requested_type()
    {
        var action = new GameAction("add", GameJson.Parse("""{"count":5,"name":"x","id":"00000000-0000-0000-0000-000000000001","kind":"second"}"""));

        var sample = action.ReadPayload<Sample>();

        Assert.Equal(5, sample.Count);
        Assert.Equal(SampleKind.Second, sample.Kind);
    }

    [Theory]
    [InlineData("""{"count":"cinco"}""")]       // tipo errado
    [InlineData("""{"count":1.5}""")]            // número fora do formato
    [InlineData("""{"kind":"inexistente"}""")]   // enum desconhecido
    [InlineData("[1,2]")]                        // não é objeto
    [InlineData("42")]
    [InlineData("null")]
    public void A_payload_that_does_not_fit_becomes_a_400_style_rule_violation(string json)
    {
        var action = new GameAction("add", GameJson.Parse(json));

        var violation = Assert.Throws<RuleViolation>(() => action.ReadPayload<Sample>());

        Assert.Equal(RuleViolationKind.InvalidAction, violation.Kind);
        Assert.Equal("action.invalid_payload", violation.Code);
    }

    [Fact]
    public void An_empty_object_payload_is_valid_and_leaves_defaults()
    {
        var action = new GameAction("pass", GameJson.EmptyObject);

        var sample = action.ReadPayload<Sample>();

        Assert.Equal(0, sample.Count);
        Assert.Null(sample.Name);
    }

    [Fact]
    public void Rule_violations_carry_a_kind_a_stable_code_and_a_message()
    {
        var invalid = RuleViolation.Invalid("jogo.a", "msg a");
        var denied = RuleViolation.NotAllowed("jogo.b", "msg b");
        var wrongState = RuleViolation.WrongState("jogo.c", "msg c");

        Assert.Equal([RuleViolationKind.InvalidAction, RuleViolationKind.NotAllowed, RuleViolationKind.InvalidState], [invalid.Kind, denied.Kind, wrongState.Kind]);
        Assert.Equal(["jogo.a", "jogo.b", "jogo.c"], [invalid.Code, denied.Code, wrongState.Code]);
        Assert.Equal("msg b", denied.Message);
    }

    [Fact]
    public void A_valid_config_result_carries_a_detached_normalized_copy()
    {
        JsonElement element;
        using (var document = JsonDocument.Parse("""{"turns":4}"""))
        {
            element = document.RootElement;
            var result = ConfigResult.Valid(element);

            Assert.True(result.IsValid);
            Assert.Null(result.Errors);
            // O documento original é descartado ao sair do bloco; o resultado precisa continuar legível.
            element = result.Normalized!.Value;
        }

        Assert.Equal(4, element.GetProperty("turns").GetInt32());
    }

    [Fact]
    public void An_invalid_config_result_carries_the_field_errors()
    {
        var single = ConfigResult.Invalid("turns", "Use de 1 a 20.");
        var many = ConfigResult.Invalid(new Dictionary<string, string[]> { ["a"] = ["x"], ["b"] = ["y", "z"] });

        Assert.False(single.IsValid);
        Assert.Null(single.Normalized);
        Assert.Equal(["Use de 1 a 20."], single.Errors!["turns"]);
        Assert.Equal(2, many.Errors!.Count);
    }

    [Fact]
    public void Events_and_views_serialize_their_payloads_as_json_objects()
    {
        var actor = Guid.NewGuid();

        var gameEvent = GameEvent.Of("jogo.acertou", new { playerId = actor, team = 1 }, actor);
        var view = PlayerView.Of(new { phase = "playing" }, ["guess"], DateTimeOffset.UnixEpoch);

        Assert.Equal(actor, gameEvent.Payload!.Value.GetProperty("playerId").GetGuid());
        Assert.Equal(actor, gameEvent.ActorPlayerId);
        Assert.Equal("playing", view.View.GetProperty("phase").GetString());
        Assert.Equal(["guess"], view.AllowedActions);
    }

    [Fact]
    public void A_transition_without_events_or_points_is_a_one_liner()
    {
        var state = GameState.From(1, new { x = 1 });

        var transition = GameTransition.Of(state, isFinished: true);

        Assert.Empty(transition.Events);
        Assert.Empty(transition.Points);
        Assert.True(transition.IsFinished);
    }

    [Fact]
    public void An_actor_without_a_player_id_is_a_spectator_or_a_manager()
    {
        Assert.False(new GameActor(null, null, IsHost: true).IsPlayer);
        Assert.True(new GameActor(Guid.NewGuid(), 0, IsHost: false).IsPlayer);
    }

    [Fact]
    public void Parsed_json_is_independent_of_the_source_document()
    {
        var element = GameJson.Parse("""{"a":{"b":[1,2,3]}}""");

        Assert.Equal(3, element.GetProperty("a").GetProperty("b").GetArrayLength());
        Assert.Equal(JsonValueKind.Object, GameJson.EmptyObject.ValueKind);
    }
}
