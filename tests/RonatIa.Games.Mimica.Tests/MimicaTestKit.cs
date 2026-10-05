using System.Text.Json;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Mimica;

namespace RonatIa.Games.Mimica.Tests;

/// <summary>Contexto de teste: o horário é do teste e o sorteio tem semente (resultado reproduzível).</summary>
public sealed class TestContext(DateTimeOffset now, int seed = 7) : IGameContext
{
    public DateTimeOffset Now { get; set; } = now;

    public IGameRandom Random { get; } = new SeededRandom(seed);
}

public sealed class SeededRandom(int seed) : IGameRandom
{
    private readonly Random _random = new(seed);

    public int NextInt(int maxExclusive) => _random.Next(maxExclusive);

    public long NextInt64() => _random.NextInt64();

    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>
/// Uma mesa de Mímica para os testes: quatro jogadores (A1 e A2 no time 0; B1 e B2 no time 1, em ordem de entrada A1, B1, A2, B2)
/// e um anfitrião que não joga. Acompanha o estado e o relógio, e devolve as transições para conferência.
/// </summary>
public sealed class Table
{
    public static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid A1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid A2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    public static readonly Guid B1 = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public static readonly Guid B2 = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    public static readonly GameActor Host = new(null, null, IsHost: true);
    public static readonly GameActor Spectator = new(null, null, IsHost: false);

    private Table(MimicaGame game, TestContext context, GameState state)
    {
        Game = game;
        Context = context;
        State = state;
    }

    public MimicaGame Game { get; }

    public TestContext Context { get; }

    public GameState State { get; private set; }

    public DateTimeOffset Now
    {
        get => Context.Now;
        set => Context.Now = value;
    }

    public static GameActor Player(Guid id) => new(id, id == A1 || id == A2 ? 0 : 1, IsHost: false);

    public static GameSetup Setup(JsonElement config, params Guid[] noAccount) => new(
        Guid.NewGuid(),
        [
            new SetupPlayer(A1, 0, 0, !noAccount.Contains(A1)),
            new SetupPlayer(B1, 1, 1, !noAccount.Contains(B1)),
            new SetupPlayer(A2, 0, 2, !noAccount.Contains(A2)),
            new SetupPlayer(B2, 1, 3, !noAccount.Contains(B2)),
        ],
        config);

    public static Table New(object? config = null, IMimicaContent? content = null, int seed = 7, params Guid[] noAccount)
    {
        var game = content is null ? new MimicaGame() : new MimicaGame(content);
        var context = new TestContext(Start, seed);
        var normalized = game.ValidateConfig(config is null ? null : JsonSerializer.SerializeToElement(config)).Normalized
            ?? throw new InvalidOperationException("Configuração de teste inválida.");

        var transition = game.Start(Setup(normalized, noAccount), context);
        return new Table(game, context, transition.State) { LastTransition = transition };
    }

    public GameTransition LastTransition { get; private set; } = null!;

    /// <summary>Todos os eventos produzidos desde o início.</summary>
    public List<GameEvent> Events { get; } = [];

    public GameTransition Act(GameActor actor, string type, object? payload = null)
    {
        var transition = Try(actor, type, payload);
        State = transition.State;
        LastTransition = transition;
        Events.AddRange(transition.Events);
        return transition;
    }

    /// <summary>Aplica sem guardar o novo estado (para conferir recusas e antevisões).</summary>
    public GameTransition Try(GameActor actor, string type, object? payload = null) => Game.Apply(
        State,
        new GameAction(type, payload is null ? GameJson.EmptyObject : JsonSerializer.SerializeToElement(payload, GameJson.Options)),
        actor,
        Context);

    public PlayerView View(GameActor viewer) => Game.Project(State, viewer, Context);

    public JsonElement ViewJson(GameActor viewer) => View(viewer).View;

    /// <summary>O mímico da vez, lido da visão pública.</summary>
    public Guid Performer => ViewJson(Spectator).GetProperty("performerPlayerId").GetGuid();

    public string Phase => ViewJson(Spectator).GetProperty("phase").GetString()!;

    public int Score(int team) => ViewJson(Spectator).GetProperty("scores").GetProperty(team.ToString()).GetInt32();

    public JsonElement Raw => State.Data;

    /// <summary>Do início da vez até o "jogando": o mímico começa e passam os 3 s de preparo.</summary>
    public void BeginPlaying()
    {
        Act(Player(Performer), "startTurn");
        Now += TimeSpan.FromSeconds(MimicaGame.PrepSeconds);
    }

    /// <summary>Joga um turno inteiro com o resultado pedido, com o mímico dando o veredito.</summary>
    public void PlayTurn(string outcome)
    {
        var performer = Player(Performer);
        BeginPlaying();
        switch (outcome)
        {
            case "hit":
                Act(performer, "reportHit");
                break;
            case "stealHit":
                Act(performer, "reportMiss");
                Act(performer, "reportStealHit");
                break;
            case "stealMiss":
                Act(performer, "reportMiss");
                Act(performer, "reportStealMiss");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }
}
