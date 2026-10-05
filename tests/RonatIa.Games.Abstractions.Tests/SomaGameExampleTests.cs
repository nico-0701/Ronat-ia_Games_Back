using System.Text.Json;
using RonatIa.Games.Abstractions;

namespace RonatIa.Games.Abstractions.Tests;

/// <summary>
/// O jogo de exemplo de <c>docs/GAME_DEVELOPMENT.md</c>, copiado como está: se o contrato mudar e o exemplo da documentação
/// deixar de compilar, este arquivo quebra junto. Também mostra como se testa um módulo sem nenhuma infraestrutura.
/// </summary>
public sealed class SomaGameExampleTests
{
    // ---- o exemplo da documentação ----

    public sealed class SomaGame : IGameModule
    {
        public GameDefinition Definition { get; } = new(
            "soma", "Soma", "Chegue primeiro à meta.", RulesVersion: 1,
            MinPlayers: 2, MaxPlayers: 6, TeamCount: 0, MinPlayersPerTeam: 0,
            ConfigDefaults: GameJson.Parse("""{"target":10}"""));

        private sealed record State(int Target, Dictionary<Guid, int> Scores, bool Finished);

        private sealed record AddPayload(int N);

        private sealed record View(int Target, Dictionary<Guid, int> Scores, bool Finished);

        public ConfigResult ValidateConfig(JsonElement? config)
        {
            var target = 10;
            if (config is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty("target", out var t)
                && (t.ValueKind != JsonValueKind.Number || !t.TryGetInt32(out target) || target is < 1 or > 100))
            {
                return ConfigResult.Invalid("target", "Use uma meta de 1 a 100.");
            }

            return ConfigResult.Valid(JsonSerializer.SerializeToElement(new { target }, GameJson.Options));
        }

        public GameTransition Start(GameSetup setup, IGameContext ctx) =>
            GameTransition.Of(GameState.From(1, new State(
                setup.Config.GetProperty("target").GetInt32(),
                setup.Players.ToDictionary(p => p.PlayerId, _ => 0),
                Finished: false)));

        public GameTransition Apply(GameState gameState, GameAction action, GameActor actor, IGameContext ctx)
        {
            var state = gameState.Read<State>();
            if (state.Finished)
            {
                throw RuleViolation.WrongState("soma.finished", "A partida terminou.");
            }

            if (action.Type != "add")
            {
                throw RuleViolation.Invalid("soma.unknown_action", $"Ação desconhecida: {action.Type}.");
            }

            if (actor.PlayerId is not { } me)
            {
                throw RuleViolation.NotAllowed("soma.not_a_player", "Só quem joga pode pontuar.");
            }

            var n = action.ReadPayload<AddPayload>().N;
            if (n is < 1 or > 3)
            {
                throw RuleViolation.Invalid("soma.invalid_amount", "Some de 1 a 3.");
            }

            state.Scores[me] += n;
            var finished = state.Scores[me] >= state.Target;
            return new GameTransition(
                GameState.From(1, state with { Finished = finished }),
                [GameEvent.Of("soma.somou", new { playerId = me, n }, me)],
                [new ScoreChange(me, Team: null, n, "add")],
                IsFinished: finished);
        }

        public PlayerView Project(GameState gameState, GameActor viewer, IGameContext ctx)
        {
            var state = gameState.Read<State>();
            return PlayerView.Of(new View(state.Target, state.Scores, state.Finished), state.Finished || !viewer.IsPlayer ? [] : ["add"]);
        }

        public GameResult Finish(GameState gameState)
        {
            var scores = gameState.Read<State>().Scores;
            var best = scores.Values.Max();
            return new GameResult(scores
                .Select(kv => new PlayerStanding(kv.Key, Team: null, kv.Value, Rank: 1 + scores.Count(o => o.Value > kv.Value), IsWinner: kv.Value == best))
                .ToList());
        }
    }

    // ---- como testar um módulo: contexto falso e nada mais ----

    private sealed class FixedContext : IGameContext
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

        public IGameRandom Random { get; } = new FirstRandom();
    }

    private sealed class FirstRandom : IGameRandom
    {
        public int NextInt(int maxExclusive) => 0;

        public long NextInt64() => 0;

        public void Shuffle<T>(IList<T> list)
        {
        }
    }

    private static readonly SomaGame Game = new();
    private static readonly FixedContext Context = new();
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Beto = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static GameState NewGame(int target = 4)
    {
        var config = Game.ValidateConfig(GameJson.Parse($$"""{"target":{{target}}}""")).Normalized!.Value;
        var setup = new GameSetup(Guid.NewGuid(), [new SetupPlayer(Ana, null, 0), new SetupPlayer(Beto, null, 1)], config);
        return Game.Start(setup, Context).State;
    }

    private static GameTransition Add(GameState state, Guid player, int n) =>
        Game.Apply(state, new GameAction("add", GameJson.Parse($$"""{"n":{{n}}}""")), new GameActor(player, null, IsHost: false), Context);

    [Fact]
    public void The_example_config_is_normalized_and_validated()
    {
        Assert.Equal(10, Game.ValidateConfig(null).Normalized!.Value.GetProperty("target").GetInt32());
        Assert.Equal(7, Game.ValidateConfig(GameJson.Parse("""{"target":7}""")).Normalized!.Value.GetProperty("target").GetInt32());
        Assert.False(Game.ValidateConfig(GameJson.Parse("""{"target":0}""")).IsValid);
        Assert.False(Game.ValidateConfig(GameJson.Parse("""{"target":"muito"}""")).IsValid);
    }

    [Fact]
    public void Playing_to_the_goal_scores_emits_events_and_finishes_with_standings()
    {
        var state = NewGame(target: 4);

        var first = Add(state, Ana, 3);
        Assert.False(first.IsFinished);
        Assert.Equal(3, Assert.Single(first.Points).Points);
        Assert.Equal("soma.somou", Assert.Single(first.Events).Type);

        var second = Add(first.State, Beto, 2);
        var last = Add(second.State, Ana, 1);

        Assert.True(last.IsFinished);
        var result = Game.Finish(last.State);
        Assert.Equal([Ana, Beto], result.Standings.Select(s => s.PlayerId).Order());
        var winner = Assert.Single(result.Standings, s => s.IsWinner);
        Assert.Equal((Ana, 4, 1), (winner.PlayerId, winner.Score, winner.Rank));
        Assert.Equal(2, result.Standings.Single(s => s.PlayerId == Beto).Rank); // 1 + um jogador com mais pontos
    }

    [Fact]
    public void The_module_refuses_invalid_actions_with_stable_codes_and_does_not_touch_the_state()
    {
        var state = NewGame();
        var before = state.Data.GetRawText();

        var tooMuch = Assert.Throws<RuleViolation>(() => Add(state, Ana, 9));
        var spectator = Assert.Throws<RuleViolation>(() => Game.Apply(state, new GameAction("add", GameJson.Parse("""{"n":1}""")), new GameActor(null, null, IsHost: true), Context));
        var unknown = Assert.Throws<RuleViolation>(() => Game.Apply(state, new GameAction("dancar", GameJson.EmptyObject), new GameActor(Ana, null, false), Context));

        Assert.Equal((RuleViolationKind.InvalidAction, "soma.invalid_amount"), (tooMuch.Kind, tooMuch.Code));
        Assert.Equal((RuleViolationKind.NotAllowed, "soma.not_a_player"), (spectator.Kind, spectator.Code));
        Assert.Equal("soma.unknown_action", unknown.Code);
        Assert.Equal(before, state.Data.GetRawText());
    }

    [Fact]
    public void Spectators_get_the_public_view_without_actions_and_players_get_theirs()
    {
        var state = NewGame();

        var player = Game.Project(state, new GameActor(Ana, null, false), Context);
        var spectator = Game.Project(state, new GameActor(null, null, false), Context);

        Assert.Equal(["add"], player.AllowedActions);
        Assert.Empty(spectator.AllowedActions);
        Assert.Equal(4, spectator.View.GetProperty("target").GetInt32());
    }
}
