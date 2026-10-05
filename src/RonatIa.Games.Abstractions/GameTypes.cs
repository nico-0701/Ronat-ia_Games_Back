using System.Text.Json;
using System.Text.Json.Serialization;

namespace RonatIa.Games.Abstractions;

/// <summary>Identidade e limites de um jogo, mostrados no catálogo e usados para validar o lobby.</summary>
/// <param name="Id">Slug estável, ex.: <c>mimica</c>.</param>
/// <param name="RulesVersion">Sobe quando as regras mudam; a partida guarda a versão de quando foi criada.</param>
/// <param name="TeamCount">0 = cada um por si (sem times); N &gt; 0 = N times, todos os jogadores precisam de um time para começar.</param>
/// <param name="MinPlayersPerTeam">Só vale quando há times.</param>
/// <param name="ConfigDefaults">A configuração padrão (um objeto JSON), que o cliente usa para montar a tela de opções.</param>
public sealed record GameDefinition(
    string Id,
    string Name,
    string Description,
    int RulesVersion,
    int MinPlayers,
    int MaxPlayers,
    int TeamCount,
    int MinPlayersPerTeam,
    JsonElement ConfigDefaults);

/// <summary>Resultado da validação de uma configuração: o objeto normalizado ou os erros por campo.</summary>
public sealed class ConfigResult
{
    private ConfigResult(JsonElement? normalized, IReadOnlyDictionary<string, string[]>? errors)
    {
        Normalized = normalized;
        Errors = errors;
    }

    public bool IsValid => Errors is null;

    public JsonElement? Normalized { get; }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static ConfigResult Valid(JsonElement normalized) => new(normalized.Clone(), null);

    public static ConfigResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);

    public static ConfigResult Invalid(string field, string message) =>
        new(null, new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Um jogador no momento de começar (o lobby já definiu quem joga e em que time).</summary>
/// <param name="PlayerId">Identifica o jogador na partida (não é o id da conta).</param>
/// <param name="Team">Número do time (0, 1...), ou nulo quando o jogo não tem times.</param>
/// <param name="Seat">Ordem de entrada no lobby, de 0 em diante.</param>
/// <param name="HasAccount">Falso para um perfil sem conta (quem não tem celular): o anfitrião age por essa pessoa, e o jogo decide o que isso muda (ex.: quem vê a carta).</param>
public sealed record SetupPlayer(Guid PlayerId, int? Team, int Seat, bool HasAccount = true);

public sealed record GameSetup(Guid SessionId, IReadOnlyList<SetupPlayer> Players, JsonElement Config);

/// <summary>
/// O estado do jogo, opaco para a plataforma: um JSON que só o módulo entende. <see cref="SchemaVersion"/> permite ao módulo
/// ler estados gravados por versões antigas.
/// </summary>
public sealed record GameState(int SchemaVersion, JsonElement Data)
{
    public static GameState From<T>(int schemaVersion, T value) =>
        new(schemaVersion, JsonSerializer.SerializeToElement(value, GameJson.Options));

    public T Read<T>() =>
        Data.Deserialize<T>(GameJson.Options) ?? throw new InvalidOperationException("O estado do jogo está vazio.");
}

/// <summary>Uma ação enviada por um jogador, ex.: <c>{ "type": "reportHit" }</c>. O <see cref="Payload"/> é um objeto JSON (vazio quando não há dados).</summary>
public sealed record GameAction(string Type, JsonElement Payload)
{
    /// <summary>
    /// Lê os dados da ação como <typeparamref name="T"/>. Dados que não batem com o tipo (campo com tipo errado, número fora
    /// do formato) viram <see cref="RuleViolation"/> <c>action.invalid_payload</c>, que a plataforma responde com 400.
    /// </summary>
    public T ReadPayload<T>()
    {
        try
        {
            if (Payload.ValueKind is JsonValueKind.Object)
            {
                return Payload.Deserialize<T>(GameJson.Options) ?? throw new JsonException();
            }
        }
        catch (JsonException)
        {
            // cai na recusa abaixo
        }

        throw RuleViolation.Invalid("action.invalid_payload", "Os dados da ação são inválidos.");
    }
}

/// <summary>
/// Quem age ou quem está olhando. <see cref="PlayerId"/> é nulo para quem não joga mas assiste ou gerencia a partida
/// (um administrador do grupo, ou o anfitrião que não entrou no jogo).
/// </summary>
/// <param name="IsHost">Anfitrião da partida ou administrador do grupo: pode executar as ações de gerência do jogo (ex.: pular um turno).</param>
public sealed record GameActor(Guid? PlayerId, int? Team, bool IsHost)
{
    public bool IsPlayer => PlayerId is not null;
}

public sealed record GameEvent(string Type, JsonElement? Payload = null, Guid? ActorPlayerId = null)
{
    public static GameEvent Of<T>(string type, T payload, Guid? actorPlayerId = null) =>
        new(type, JsonSerializer.SerializeToElement(payload, GameJson.Options), actorPlayerId);
}

/// <summary>Pontos para um jogador, um time ou os dois. Entram no livro-razão da partida; só o servidor os cria.</summary>
public sealed record ScoreChange(Guid? PlayerId, int? Team, int Points, string Reason);

/// <summary>O que uma ação (ou o início) produz: o novo estado, o que aconteceu, quem pontuou e se a partida acabou.</summary>
public sealed record GameTransition(
    GameState State,
    IReadOnlyList<GameEvent> Events,
    IReadOnlyList<ScoreChange> Points,
    bool IsFinished = false)
{
    public static GameTransition Of(GameState state, bool isFinished = false) =>
        new(state, [], [], isFinished);
}

/// <summary>
/// A visão de um jogador: um JSON próprio do jogo (<see cref="View"/>) mais o que a plataforma entende. As
/// <see cref="AllowedActions"/> são calculadas pelo servidor: o cliente não deduz o que é válido.
/// </summary>
public sealed record PlayerView(JsonElement View, IReadOnlyList<string> AllowedActions, DateTimeOffset? DeadlineAt = null)
{
    public static PlayerView Of<T>(T view, IReadOnlyList<string> allowedActions, DateTimeOffset? deadlineAt = null) =>
        new(JsonSerializer.SerializeToElement(view, GameJson.Options), allowedActions, deadlineAt);
}

public sealed record PlayerStanding(Guid PlayerId, int? Team, int Score, int Rank, bool IsWinner);

public sealed record GameResult(IReadOnlyList<PlayerStanding> Standings);

/// <summary>Opções de JSON compartilhadas por plataforma e módulos: camelCase, enums como texto, números estritos.</summary>
public static class GameJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <summary>Um objeto JSON vazio (<c>{}</c>), para ações sem dados.</summary>
    public static JsonElement EmptyObject { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
