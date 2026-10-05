using System.Text.Json;
using RonatIa.Games.Abstractions;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>
/// Jogo de teste "Relay": dois times, um jogador da vez conhece uma palavra secreta e os demais tentam adivinhá-la, com prazo.
/// Exercita o que o motor precisa garantir: segredo só na visão de quem pode ver, permissões por ação, prazo avaliado pelo
/// servidor, pontos no livro-razão e fim da partida. Vive nos testes (não é um jogo do produto).
/// </summary>
public sealed class RelayGame : IGameModule
{
    public const string Id = "relay";
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);

    private static readonly string[] Words = ["abacaxi", "bicicleta", "cachorro", "dinossauro", "elefante", "foguete", "guitarra", "helicoptero"];

    public GameDefinition Definition { get; } = new(
        Id,
        "Relay (teste)",
        "Jogo de teste do motor de partidas.",
        RulesVersion: 1,
        MinPlayers: 2,
        MaxPlayers: 6,
        TeamCount: 2,
        MinPlayersPerTeam: 1,
        ConfigDefaults: GameJson.Parse("""{"turns":4,"turnSeconds":60}"""));

    private sealed record Config(int Turns, int TurnSeconds);

    private sealed record State(
        int Turn,
        int TotalTurns,
        int TurnSeconds,
        List<Guid> Order,
        Dictionary<Guid, int> Teams,
        string Word,
        DateTimeOffset DeadlineAt,
        List<string> Used,
        Dictionary<int, int> TeamScores,
        bool Finished);

    private sealed record GuessPayload(string? Text);

    public sealed record View(string Phase, int Turn, int TotalTurns, Guid? PerformerPlayerId, DateTimeOffset DeadlineAt, Dictionary<int, int> Scores, string? Word);

    public ConfigResult ValidateConfig(JsonElement? config)
    {
        var turns = 4;
        var seconds = 60;
        var errors = new Dictionary<string, string[]>();

        if (config is { ValueKind: JsonValueKind.Object } obj)
        {
            if (obj.TryGetProperty("turns", out var t))
            {
                if (t.ValueKind != JsonValueKind.Number || !t.TryGetInt32(out turns) || turns is < 1 or > 20)
                {
                    errors["turns"] = ["Use de 1 a 20 turnos."];
                }
            }

            if (obj.TryGetProperty("turnSeconds", out var s))
            {
                if (s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out seconds) || seconds is < 5 or > 300)
                {
                    errors["turnSeconds"] = ["Use de 5 a 300 segundos."];
                }
            }
        }
        else if (config is not null && config.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            errors["config"] = ["A configuração deve ser um objeto."];
        }

        return errors.Count > 0
            ? ConfigResult.Invalid(errors)
            : ConfigResult.Valid(JsonSerializer.SerializeToElement(new Config(turns, seconds), GameJson.Options));
    }

    public GameTransition Start(GameSetup setup, IGameContext context)
    {
        var config = setup.Config.Deserialize<Config>(GameJson.Options)!;
        var order = setup.Players.OrderBy(p => p.Seat).Select(p => p.PlayerId).ToList();
        var teams = setup.Players.ToDictionary(p => p.PlayerId, p => p.Team ?? 0);

        var state = new State(
            Turn: 0,
            config.Turns,
            config.TurnSeconds,
            order,
            teams,
            Word: Words[context.Random.NextInt(Words.Length)],
            DeadlineAt: context.Now.AddSeconds(config.TurnSeconds),
            Used: [],
            TeamScores: Enumerable.Range(0, 2).ToDictionary(t => t, _ => 0),
            Finished: false);

        state.Used.Add(state.Word);
        return new GameTransition(
            GameState.From(1, state),
            [GameEvent.Of("relay.turn_started", new { turn = 0, performerPlayerId = order[0] })],
            []);
    }

    public GameTransition Apply(GameState gameState, GameAction action, GameActor actor, IGameContext context)
    {
        var state = gameState.Read<State>();
        if (state.Finished)
        {
            throw RuleViolation.WrongState("relay.finished", "A partida terminou.");
        }

        var performer = state.Order[state.Turn % state.Order.Count];

        switch (action.Type)
        {
            case "guess":
                {
                    if (actor.PlayerId is not { } guesser)
                    {
                        throw RuleViolation.NotAllowed("relay.not_a_player", "Só quem joga pode adivinhar.");
                    }

                    if (guesser == performer)
                    {
                        throw RuleViolation.NotAllowed("relay.performer_cannot_guess", "Quem sabe a palavra não adivinha.");
                    }

                    if (context.Now > state.DeadlineAt + Grace)
                    {
                        throw RuleViolation.WrongState("relay.turn_expired", "O tempo do turno acabou.");
                    }

                    var text = action.ReadPayload<GuessPayload>().Text;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw RuleViolation.Invalid("relay.text_required", "Informe o palpite.");
                    }

                    if (!string.Equals(text.Trim(), state.Word, StringComparison.OrdinalIgnoreCase))
                    {
                        // Palpite errado: nada muda no estado, mas o fato (sem o palpite) fica na trilha.
                        return new GameTransition(gameState, [GameEvent.Of("relay.wrong_guess", new { playerId = guesser }, guesser)], []);
                    }

                    var team = state.Teams[guesser];
                    state.TeamScores[team] += 1;
                    var next = NextTurn(state, context);
                    return new GameTransition(
                        GameState.From(1, next),
                        [GameEvent.Of("relay.guessed", new { playerId = guesser, team }, guesser)],
                        [new ScoreChange(guesser, team, 1, "guess")],
                        next.Finished);
                }

            case "pass":
                {
                    if (actor.PlayerId != performer)
                    {
                        throw RuleViolation.NotAllowed("relay.not_performer", "Só quem está na vez pode passar.");
                    }

                    var next = NextTurn(state, context);
                    return new GameTransition(GameState.From(1, next), [GameEvent.Of("relay.passed", new { playerId = performer })], [], next.Finished);
                }

            case "skip":
                {
                    if (!actor.IsHost)
                    {
                        throw RuleViolation.NotAllowed("relay.host_only", "Só o anfitrião pode pular o turno.");
                    }

                    var next = NextTurn(state, context);
                    return new GameTransition(GameState.From(1, next), [GameEvent.Of("relay.skipped", new { turn = state.Turn })], [], next.Finished);
                }

            default:
                throw RuleViolation.Invalid("relay.unknown_action", $"Ação desconhecida: {action.Type}.");
        }
    }

    public PlayerView Project(GameState gameState, GameActor viewer, IGameContext context)
    {
        var state = gameState.Read<State>();
        var performer = state.Finished ? (Guid?)null : state.Order[state.Turn % state.Order.Count];
        var isPerformer = viewer.PlayerId is not null && viewer.PlayerId == performer;

        var allowed = new List<string>();
        if (!state.Finished)
        {
            if (isPerformer)
            {
                allowed.Add("pass");
            }
            else if (viewer.IsPlayer)
            {
                allowed.Add("guess");
            }

            if (viewer.IsHost)
            {
                allowed.Add("skip");
            }
        }

        var view = new View(
            state.Finished ? "finished" : "playing",
            state.Turn,
            state.TotalTurns,
            performer,
            state.DeadlineAt,
            state.TeamScores,
            Word: isPerformer ? state.Word : null);

        return PlayerView.Of(view, allowed, state.Finished ? null : state.DeadlineAt);
    }

    public GameResult Finish(GameState gameState)
    {
        var state = gameState.Read<State>();
        var best = state.TeamScores.Values.DefaultIfEmpty(0).Max();

        var ordered = state.Order
            .Select(id => new { Id = id, Team = state.Teams[id], Score = state.TeamScores[state.Teams[id]] })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Empate: todos do time vencedor (ou de ambos) vencem; o ranking é por pontuação do time (competição 1, 1, 3...).
        var standings = ordered
            .Select(x => new PlayerStanding(x.Id, x.Team, x.Score, Rank: 1 + ordered.Count(o => o.Score > x.Score), IsWinner: x.Score == best))
            .ToList();

        return new GameResult(standings);
    }

    private static State NextTurn(State state, IGameContext context)
    {
        var turn = state.Turn + 1;
        if (turn >= state.TotalTurns)
        {
            return state with { Turn = turn, Finished = true };
        }

        var pool = Words.Where(w => !state.Used.Contains(w)).ToList();
        if (pool.Count == 0)
        {
            pool = [.. Words];
        }

        var word = pool[context.Random.NextInt(pool.Count)];
        return state with
        {
            Turn = turn,
            Word = word,
            DeadlineAt = context.Now.AddSeconds(state.TurnSeconds),
            Used = [.. state.Used, word],
        };
    }
}

/// <summary>Jogo de teste "Solo": cada um por si, sem times. Cada <c>add</c> soma 1 a 3 pontos a quem agiu; quem chega à meta vence.</summary>
public sealed class SoloGame : IGameModule
{
    public const string Id = "solo";

    public GameDefinition Definition { get; } = new(
        Id,
        "Solo (teste)",
        "Jogo de teste sem times.",
        RulesVersion: 1,
        MinPlayers: 1,
        MaxPlayers: 4,
        TeamCount: 0,
        MinPlayersPerTeam: 0,
        ConfigDefaults: GameJson.Parse("""{"target":5}"""));

    private sealed record State(int Target, Dictionary<Guid, int> Scores, bool Finished);

    private sealed record AddPayload(int N);

    public sealed record View(int Target, Dictionary<Guid, int> Scores, bool Finished);

    public ConfigResult ValidateConfig(JsonElement? config)
    {
        var target = 5;
        if (config is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty("target", out var t))
        {
            if (t.ValueKind != JsonValueKind.Number || !t.TryGetInt32(out target) || target is < 1 or > 50)
            {
                return ConfigResult.Invalid("target", "Use uma meta de 1 a 50.");
            }
        }

        return ConfigResult.Valid(JsonSerializer.SerializeToElement(new { target }, GameJson.Options));
    }

    public GameTransition Start(GameSetup setup, IGameContext context)
    {
        var target = setup.Config.GetProperty("target").GetInt32();
        var state = new State(target, setup.Players.ToDictionary(p => p.PlayerId, _ => 0), Finished: false);
        return GameTransition.Of(GameState.From(1, state));
    }

    public GameTransition Apply(GameState gameState, GameAction action, GameActor actor, IGameContext context)
    {
        var state = gameState.Read<State>();
        if (state.Finished)
        {
            throw RuleViolation.WrongState("solo.finished", "A partida terminou.");
        }

        if (action.Type != "add")
        {
            throw RuleViolation.Invalid("solo.unknown_action", $"Ação desconhecida: {action.Type}.");
        }

        if (actor.PlayerId is not { } playerId)
        {
            throw RuleViolation.NotAllowed("solo.not_a_player", "Só quem joga pode pontuar.");
        }

        var n = action.ReadPayload<AddPayload>().N;
        if (n is < 1 or > 3)
        {
            throw RuleViolation.Invalid("solo.invalid_amount", "Some de 1 a 3.");
        }

        state.Scores[playerId] += n;
        var finished = state.Scores[playerId] >= state.Target;
        return new GameTransition(
            GameState.From(1, state with { Finished = finished }),
            [GameEvent.Of("solo.added", new { playerId, n }, playerId)],
            [new ScoreChange(playerId, null, n, "add")],
            finished);
    }

    public PlayerView Project(GameState gameState, GameActor viewer, IGameContext context)
    {
        var state = gameState.Read<State>();
        return PlayerView.Of(new View(state.Target, state.Scores, state.Finished), state.Finished || !viewer.IsPlayer ? [] : ["add"]);
    }

    public GameResult Finish(GameState gameState)
    {
        var state = gameState.Read<State>();
        var best = state.Scores.Values.DefaultIfEmpty(0).Max();

        var standings = state.Scores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new PlayerStanding(kv.Key, null, kv.Value, Rank: 1 + state.Scores.Count(o => o.Value > kv.Value), IsWinner: kv.Value == best))
            .ToList();

        return new GameResult(standings);
    }
}
