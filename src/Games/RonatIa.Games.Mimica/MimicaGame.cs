using System.Text.Json;
using RonatIa.Games.Abstractions;

namespace RonatIa.Games.Mimica;

/// <summary>
/// Mímica: dois times se alternam. Na vez do time, um jogador (o "mímico", em rodízio) vê uma carta e faz a mímica; o time
/// dele adivinha. O mímico (ou o anfitrião) dá o veredito: <c>reportHit</c> vale 1 ponto ao time da vez; <c>reportMiss</c>
/// abre a chance de roubo, em que o adversário tem 30 s e, se acertar (<c>reportStealHit</c>), leva o ponto. Depois de
/// <c>rodadas × 2</c> turnos, vence o maior placar; empate: todos vencem.
/// </summary>
/// <remarks>
/// Regras vindas do app original, agora no servidor: a carta é sorteada aqui e <b>só o mímico a vê</b> (o anfitrião também,
/// quando o mímico é um perfil sem conta, que não tem celular); ao fim do turno ela é revelada a todos no resumo. O tempo é
/// dado: o preparo de 3 s vira "jogando" sozinho quando o prazo passa, e zerar o cronômetro <b>não encerra o turno</b>
/// (como no original): <c>reportMiss</c> continua valendo e <c>reportHit</c> é aceito até o prazo + a tolerância.
/// </remarks>
public sealed class MimicaGame : IGameModule
{
    public const string Id = "mimica";
    public const int SchemaVersion = 1;
    public const int PrepSeconds = 3;
    public const int StealSeconds = 30;
    public const int Teams = 2;

    private const int DefaultRounds = 10;
    private const int DefaultTurnSeconds = 60;
    private const int DefaultLateGraceSeconds = 3;

    private readonly IMimicaContent _content;

    public MimicaGame()
        : this(MimicaContent.Default)
    {
    }

    public MimicaGame(IMimicaContent content)
    {
        _content = content;
        Definition = new GameDefinition(
            Id,
            "Mímica",
            "Dois times se alternam: um faz a mímica e o time dele adivinha. Se errar, o adversário tem uma chance de roubar o ponto.",
            RulesVersion: 1,
            MinPlayers: 2,
            MaxPlayers: 24,
            TeamCount: Teams,
            MinPlayersPerTeam: 1,
            ConfigDefaults: JsonSerializer.SerializeToElement(
                new MimicaConfig(DefaultRounds, DefaultTurnSeconds, content.Categories.Select(c => c.Id).ToList(), DefaultLateGraceSeconds),
                GameJson.Options));
    }

    public GameDefinition Definition { get; }

    // ---- configuração ----

    public ConfigResult ValidateConfig(JsonElement? config)
    {
        var errors = new Dictionary<string, string[]>();
        var rounds = DefaultRounds;
        var turnSeconds = DefaultTurnSeconds;
        var grace = DefaultLateGraceSeconds;
        var categories = _content.Categories.Select(c => c.Id).ToList();

        if (config is { ValueKind: JsonValueKind.Object } obj)
        {
            ReadInt(obj, "rounds", ref rounds, 1, 30, "Use de 1 a 30 rodadas por time.", errors);
            ReadInt(obj, "turnSeconds", ref turnSeconds, 10, 300, "Use de 10 a 300 segundos por turno.", errors);
            ReadInt(obj, "lateGraceSeconds", ref grace, 0, 10, "A tolerância vai de 0 a 10 segundos.", errors);

            if (obj.TryGetProperty("categories", out var list))
            {
                var parsed = ReadCategories(list);
                if (parsed is null)
                {
                    errors["categories"] = ["Escolha pelo menos um tema, sem repetir: " + string.Join(", ", _content.Categories.Select(c => c.Id)) + "."];
                }
                else
                {
                    categories = parsed;
                }
            }
        }
        else if (config is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            errors["config"] = ["A configuração deve ser um objeto."];
        }

        return errors.Count > 0
            ? ConfigResult.Invalid(errors)
            : ConfigResult.Valid(JsonSerializer.SerializeToElement(new MimicaConfig(rounds, turnSeconds, categories, grace), GameJson.Options));
    }

    // ---- começar ----

    public GameTransition Start(GameSetup setup, IGameContext context)
    {
        var config = setup.Config.Deserialize<MimicaConfig>(GameJson.Options)
            ?? throw new InvalidOperationException("Configuração da Mímica ausente.");

        var rosters = new Dictionary<int, List<Guid>>();
        for (var team = 0; team < Teams; team++)
        {
            var members = setup.Players.Where(p => p.Team == team).OrderBy(p => p.Seat).Select(p => p.PlayerId).ToList();
            if (members.Count == 0)
            {
                throw RuleViolation.WrongState("mimica.team_empty", $"O time {team + 1} não tem jogadores.");
            }

            rosters[team] = members;
        }

        var state = new MimicaState
        {
            Config = config,
            Turn = 0,
            TotalTurns = config.Rounds * Teams,
            Phase = MimicaPhase.TurnIntro,
            Rosters = rosters,
            Scores = Enumerable.Range(0, Teams).ToDictionary(t => t, _ => 0),
            NoAccount = setup.Players.Where(p => !p.HasAccount).Select(p => p.PlayerId).ToList(),
            Used = [],
        };

        state.Card = Draw(state, context.Random);

        return new GameTransition(GameState.From(SchemaVersion, state), [TurnReady(state)], []);
    }

    // ---- ações ----

    public GameTransition Apply(GameState gameState, GameAction action, GameActor actor, IGameContext context)
    {
        var state = gameState.Read<MimicaState>();
        if (state.Phase == MimicaPhase.Finished)
        {
            throw RuleViolation.WrongState("mimica.finished", "A partida terminou.");
        }

        var now = context.Now;
        var phase = EffectivePhase(state, now);
        var team = TeamOfTurn(state);

        switch (action.Type)
        {
            case "startTurn":
                RequireOperator(state, actor);
                if (state.Phase != MimicaPhase.TurnIntro)
                {
                    throw RuleViolation.WrongState("mimica.turn_already_started", "A vez já começou.");
                }

                state.Phase = MimicaPhase.Prep;
                state.PrepEndsAt = now.AddSeconds(PrepSeconds);
                state.PlayEndsAt = state.PrepEndsAt.Value.AddSeconds(state.Config.TurnSeconds);
                return new GameTransition(
                    GameState.From(SchemaVersion, state),
                    [GameEvent.Of("mimica.turn_started", new { turn = state.Turn, team, performerPlayerId = PerformerOf(state) })],
                    []);

            case "reportHit":
                RequireOperator(state, actor);
                if (phase != MimicaPhase.Playing)
                {
                    throw RuleViolation.WrongState("mimica.not_playing", "O veredito só vale depois do preparo e antes do roubo.");
                }

                if (now > state.PlayEndsAt!.Value.AddSeconds(state.Config.LateGraceSeconds))
                {
                    throw RuleViolation.WrongState("mimica.turn_expired", "O tempo acabou: registre que não acertou.");
                }

                state.Scores[team]++;
                return EndTurn(state, "hit", [new ScoreChange(null, team, 1, "hit")], context);

            case "reportMiss":
                RequireOperator(state, actor);
                if (phase != MimicaPhase.Playing)
                {
                    throw RuleViolation.WrongState("mimica.not_playing", "O veredito só vale depois do preparo e antes do roubo.");
                }

                state.Phase = MimicaPhase.Steal;
                state.StealEndsAt = now.AddSeconds(StealSeconds);
                return new GameTransition(
                    GameState.From(SchemaVersion, state),
                    [GameEvent.Of("mimica.miss", new { turn = state.Turn, team, stealingTeam = Opponent(team) })],
                    []);

            case "reportStealHit":
                RequireOperator(state, actor);
                RequireSteal(state);
                if (now > state.StealEndsAt!.Value.AddSeconds(state.Config.LateGraceSeconds))
                {
                    throw RuleViolation.WrongState("mimica.steal_expired", "O tempo da chance acabou: registre que não acertou.");
                }

                state.Scores[Opponent(team)]++;
                return EndTurn(state, "stealHit", [new ScoreChange(null, Opponent(team), 1, "steal_hit")], context);

            case "reportStealMiss":
                RequireOperator(state, actor);
                RequireSteal(state);
                return EndTurn(state, "stealMiss", [], context);

            case "skipTurn":
                if (!actor.IsHost)
                {
                    throw RuleViolation.NotAllowed("mimica.host_only", "Só o anfitrião pode pular a vez.");
                }

                return EndTurn(state, "skipped", [], context);

            default:
                throw RuleViolation.Invalid("mimica.unknown_action", $"Ação desconhecida: {action.Type}.");
        }
    }

    // ---- o que cada pessoa vê ----

    public PlayerView Project(GameState gameState, GameActor viewer, IGameContext context)
    {
        var state = gameState.Read<MimicaState>();
        var finished = state.Phase == MimicaPhase.Finished;
        var phase = EffectivePhase(state, context.Now);
        var performer = finished ? (Guid?)null : PerformerOf(state);
        var isOperator = !finished && (viewer.IsHost || (viewer.PlayerId is not null && viewer.PlayerId == performer));

        var allowed = new List<string>();
        if (isOperator)
        {
            switch (phase)
            {
                case MimicaPhase.TurnIntro:
                    allowed.Add("startTurn");
                    break;
                case MimicaPhase.Playing:
                    allowed.Add("reportMiss");
                    if (context.Now <= state.PlayEndsAt!.Value.AddSeconds(state.Config.LateGraceSeconds))
                    {
                        allowed.Insert(0, "reportHit");
                    }

                    break;
                case MimicaPhase.Steal:
                    allowed.Add("reportStealMiss");
                    if (context.Now <= state.StealEndsAt!.Value.AddSeconds(state.Config.LateGraceSeconds))
                    {
                        allowed.Insert(0, "reportStealHit");
                    }

                    break;
            }
        }

        if (viewer.IsHost && !finished)
        {
            allowed.Add("skipTurn");
        }

        // A carta é segredo do mímico. Quando o mímico é um perfil sem conta (sem celular), o anfitrião vê por ele.
        var seesCard = !finished
            && phase is MimicaPhase.Prep or MimicaPhase.Playing or MimicaPhase.Steal
            && performer is { } p
            && (viewer.PlayerId == p || (viewer.IsHost && state.NoAccount.Contains(p)));

        var view = new MimicaView(
            Phase: PhaseName(phase),
            Round: finished ? state.Config.Rounds : (state.Turn / Teams) + 1,
            TotalRounds: state.Config.Rounds,
            Turn: state.Turn,
            TotalTurns: state.TotalTurns,
            Team: finished ? null : TeamOfTurn(state),
            PerformerPlayerId: performer,
            TurnSeconds: state.Config.TurnSeconds,
            PrepSeconds: PrepSeconds,
            StealSeconds: StealSeconds,
            PrepEndsAt: phase is MimicaPhase.Prep ? state.PrepEndsAt : null,
            PlayEndsAt: phase is MimicaPhase.Prep or MimicaPhase.Playing ? state.PlayEndsAt : null,
            StealEndsAt: phase is MimicaPhase.Steal ? state.StealEndsAt : null,
            Scores: state.Scores,
            Card: seesCard ? CardView(state.Card) : null,
            LastTurn: state.LastTurn is { } last ? new LastTurnView(last.Turn, last.Team, last.PerformerPlayerId, last.Outcome, CardView(last.Card)) : null);

        return PlayerView.Of(view, allowed, DeadlineOf(phase, state));
    }

    // ---- fim ----

    public GameResult Finish(GameState gameState)
    {
        var state = gameState.Read<MimicaState>();
        var best = state.Scores.Values.Max();

        // Empate = todos vencem. A posição é por pontuação do time (1, 1 no empate; 1 e 2 caso contrário).
        var standings = new List<PlayerStanding>();
        for (var team = 0; team < Teams; team++)
        {
            var score = state.Scores[team];
            var rank = 1 + state.Scores.Values.Count(other => other > score);
            standings.AddRange(state.Rosters[team].Select(player => new PlayerStanding(player, team, score, rank, score == best)));
        }

        return new GameResult(standings);
    }

    // ---- mecânica ----

    private GameTransition EndTurn(MimicaState state, string outcome, IReadOnlyList<ScoreChange> points, IGameContext context)
    {
        var team = TeamOfTurn(state);
        var performer = PerformerOf(state);
        var endedCard = state.Card;
        var events = new List<GameEvent>
        {
            // Só ids: o texto da carta é revelado na visão (resumo do turno), não na trilha.
            GameEvent.Of("mimica.turn_ended", new { turn = state.Turn, team, performerPlayerId = performer, outcome, categoryId = endedCard.CategoryId, promptId = endedCard.PromptId }),
        };

        state.LastTurn = new TurnSummary(state.Turn, team, performer, outcome, endedCard);
        state.PrepEndsAt = null;
        state.PlayEndsAt = null;
        state.StealEndsAt = null;
        state.Turn++;

        if (state.Turn >= state.TotalTurns)
        {
            state.Phase = MimicaPhase.Finished;
            events.Add(GameEvent.Of("mimica.finished", new { scores = state.Scores }));
            return new GameTransition(GameState.From(SchemaVersion, state), events, points, IsFinished: true);
        }

        state.Phase = MimicaPhase.TurnIntro;
        state.Card = Draw(state, context.Random, endedCard.PromptId);
        events.Add(TurnReady(state));
        return new GameTransition(GameState.From(SchemaVersion, state), events, points);
    }

    /// <summary>
    /// Sorteia a carta da vez: o tema é escolhido por igual entre os ativos e a carta, entre as ainda não usadas do tema
    /// (sem repetir até esgotar o baralho; esgotado, recomeça sem repetir a última).
    /// </summary>
    private MimicaCard Draw(MimicaState state, IGameRandom random, string? lastPromptId = null)
    {
        var categoryId = state.Config.Categories[random.NextInt(state.Config.Categories.Count)];
        var category = _content.FindCategory(categoryId)
            ?? throw new InvalidOperationException($"Tema desconhecido no estado da partida: {categoryId}.");

        if (!state.Used.TryGetValue(categoryId, out var used))
        {
            used = [];
            state.Used[categoryId] = used;
        }

        var remaining = category.Prompts.Where(prompt => !used.Contains(prompt.Id)).ToList();
        if (remaining.Count == 0)
        {
            used.Clear();
            remaining = category.Prompts.Where(prompt => prompt.Id != lastPromptId).ToList();
            if (remaining.Count == 0)
            {
                remaining = [.. category.Prompts];
            }
        }

        var pick = remaining[random.NextInt(remaining.Count)];
        used.Add(pick.Id);
        return new MimicaCard(categoryId, pick.Id);
    }

    private static GameEvent TurnReady(MimicaState state) =>
        GameEvent.Of("mimica.turn_ready", new { turn = state.Turn, round = (state.Turn / Teams) + 1, team = TeamOfTurn(state), performerPlayerId = PerformerOf(state) });

    private static int TeamOfTurn(MimicaState state) => state.Turn % Teams;

    private static int Opponent(int team) => 1 - team;

    /// <summary>Rodízio do mímico: <c>roster[time][⌊turno / 2⌋ mod tamanho]</c> (idêntico ao app original).</summary>
    private static Guid PerformerOf(MimicaState state)
    {
        var roster = state.Rosters[TeamOfTurn(state)];
        return roster[(state.Turn / Teams) % roster.Count];
    }

    /// <summary>O preparo de 3 s vira "jogando" sozinho quando o prazo passa: a fase é derivada do relógio, não de um timer.</summary>
    private static MimicaPhase EffectivePhase(MimicaState state, DateTimeOffset now) =>
        state.Phase == MimicaPhase.Prep && state.PrepEndsAt is { } prepEnds && now >= prepEnds ? MimicaPhase.Playing : state.Phase;

    private static DateTimeOffset? DeadlineOf(MimicaPhase phase, MimicaState state) => phase switch
    {
        MimicaPhase.Prep => state.PrepEndsAt,
        MimicaPhase.Playing => state.PlayEndsAt,
        MimicaPhase.Steal => state.StealEndsAt,
        _ => null,
    };

    /// <summary>Quem dá os comandos da vez: o mímico ou o anfitrião (que cobre o perfil sem celular, a desconexão e o "celular na mesa").</summary>
    private static void RequireOperator(MimicaState state, GameActor actor)
    {
        if (actor.IsHost || (actor.PlayerId is not null && actor.PlayerId == PerformerOf(state)))
        {
            return;
        }

        throw RuleViolation.NotAllowed("mimica.not_performer", "Só quem está fazendo a mímica (ou o anfitrião) pode fazer isso.");
    }

    private static void RequireSteal(MimicaState state)
    {
        if (state.Phase != MimicaPhase.Steal)
        {
            throw RuleViolation.WrongState("mimica.not_stealing", "Não há chance de roubo aberta.");
        }
    }

    private static string PhaseName(MimicaPhase phase) => phase switch
    {
        MimicaPhase.TurnIntro => "turnIntro",
        MimicaPhase.Prep => "prep",
        MimicaPhase.Playing => "playing",
        MimicaPhase.Steal => "steal",
        _ => "finished",
    };

    private CardView? CardView(MimicaCard card)
    {
        var prompt = _content.FindPrompt(card.PromptId);
        var category = _content.FindCategory(card.CategoryId);
        return new CardView(card.CategoryId, category?.Kicker ?? string.Empty, prompt?.Text ?? card.PromptId, card.PromptId);
    }

    private static void ReadInt(JsonElement obj, string name, ref int value, int min, int max, string message, Dictionary<string, string[]> errors)
    {
        if (!obj.TryGetProperty(name, out var element))
        {
            return;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var parsed) || parsed < min || parsed > max)
        {
            errors[name] = [message];
            return;
        }

        value = parsed;
    }

    private List<string>? ReadCategories(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = new List<string>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } id || _content.FindCategory(id) is null || ids.Contains(id))
            {
                return null;
            }

            ids.Add(id);
        }

        return ids.Count == 0 ? null : ids;
    }
}

// ---- configuração, estado e visão ----

/// <summary>Configuração normalizada da partida (é o que fica salva).</summary>
/// <param name="Rounds">Rodadas por time; a partida tem <c>Rounds × 2</c> turnos.</param>
/// <param name="TurnSeconds">Duração do turno, depois dos 3 s de preparo.</param>
/// <param name="Categories">Temas ativos (ids).</param>
/// <param name="LateGraceSeconds">Tolerância para registrar um acerto logo depois de o tempo acabar (latência do celular).</param>
internal sealed record MimicaConfig(int Rounds, int TurnSeconds, List<string> Categories, int LateGraceSeconds);

internal enum MimicaPhase
{
    TurnIntro,
    Prep,
    Playing,
    Steal,
    Finished,
}

internal sealed record MimicaCard(string CategoryId, string PromptId);

internal sealed record TurnSummary(int Turn, int Team, Guid PerformerPlayerId, string Outcome, MimicaCard Card);

/// <summary>O estado completo da partida, <b>com a carta da vez</b>: só sai do servidor pela projeção.</summary>
internal sealed class MimicaState
{
    public MimicaConfig Config { get; set; } = new(10, 60, [], 3);

    public int Turn { get; set; }

    public int TotalTurns { get; set; }

    public MimicaPhase Phase { get; set; }

    public Dictionary<int, List<Guid>> Rosters { get; set; } = [];

    public Dictionary<int, int> Scores { get; set; } = [];

    /// <summary>Jogadores sem conta: o anfitrião vê a carta e dá o veredito por eles.</summary>
    public List<Guid> NoAccount { get; set; } = [];

    public MimicaCard Card { get; set; } = new(string.Empty, string.Empty);

    /// <summary>Cartas já usadas por tema, para não repetir até esgotar o baralho.</summary>
    public Dictionary<string, List<string>> Used { get; set; } = [];

    public DateTimeOffset? PrepEndsAt { get; set; }

    public DateTimeOffset? PlayEndsAt { get; set; }

    public DateTimeOffset? StealEndsAt { get; set; }

    public TurnSummary? LastTurn { get; set; }
}

/// <summary>A carta como o mímico a vê (o texto e o tema).</summary>
public sealed record CardView(string CategoryId, string Kicker, string Text, string PromptId);

/// <summary>O resumo do turno anterior, aberto a todos: "A mímica era…".</summary>
public sealed record LastTurnView(int Turn, int Team, Guid PerformerPlayerId, string Outcome, CardView? Card);

/// <summary>A visão pública da partida, mais a carta quando quem olha pode vê-la.</summary>
public sealed record MimicaView(
    string Phase,
    int Round,
    int TotalRounds,
    int Turn,
    int TotalTurns,
    int? Team,
    Guid? PerformerPlayerId,
    int TurnSeconds,
    int PrepSeconds,
    int StealSeconds,
    DateTimeOffset? PrepEndsAt,
    DateTimeOffset? PlayEndsAt,
    DateTimeOffset? StealEndsAt,
    Dictionary<int, int> Scores,
    CardView? Card,
    LastTurnView? LastTurn);
