using System.Text.Json;

namespace RonatIa.Games.Abstractions;

/// <summary>
/// O contrato de um jogo. Um módulo é uma máquina de estados <b>pura</b>: dado o estado, uma ação e quem agir, devolve o
/// novo estado, os eventos e os pontos. Não conhece banco, rede, SignalR nem o relógio do sistema (o horário vem do
/// <see cref="IGameContext"/>), o que o torna testável sem infraestrutura. O servidor é a fonte da verdade: o cliente envia
/// ações, nunca pontuação, e só enxerga o que <see cref="Project"/> devolve para ele.
/// </summary>
/// <remarks>
/// Prazos são <b>dados</b> no estado (ex.: <c>deadlineAt</c>) e o módulo os avalia quando chega uma ação ou uma leitura;
/// não há timers em memória (o servidor gratuito dorme e reinicia). Informação secreta (a carta do mímico, o papel de
/// cada um) fica no estado, mas só sai pela projeção de quem pode vê-la.
/// </remarks>
public interface IGameModule
{
    GameDefinition Definition { get; }

    /// <summary>Valida e normaliza a configuração da partida (preenche padrões, limita faixas).</summary>
    ConfigResult ValidateConfig(JsonElement? config);

    /// <summary>Estado inicial a partir dos jogadores (e times) já definidos no lobby, mais os eventos e pontos de abertura, se houver.</summary>
    GameTransition Start(GameSetup setup, IGameContext context);

    /// <summary>
    /// Aplica uma ação. Valida tudo (quem pode, em que fase, se o prazo permite) e lança <see cref="RuleViolation"/> se a ação
    /// não for válida; nesse caso nada muda. Nunca altera o estado recebido: devolve um novo.
    /// </summary>
    GameTransition Apply(GameState state, GameAction action, GameActor actor, IGameContext context);

    /// <summary>
    /// O que <paramref name="viewer"/> pode ver agora, incluindo as ações permitidas e o prazo. Segredos de outros ficam de fora.
    /// Quem só assiste (não é jogador) também passa por aqui, com <see cref="GameActor.PlayerId"/> nulo.
    /// </summary>
    PlayerView Project(GameState state, GameActor viewer, IGameContext context);

    /// <summary>A classificação com o estado atual (usada no fim natural da partida e quando o anfitrião encerra antes).</summary>
    GameResult Finish(GameState state);
}

/// <summary>Quem e quando: o horário (para os prazos) e o sorteio, ambos injetados para que o módulo seja determinístico nos testes.</summary>
public interface IGameContext
{
    DateTimeOffset Now { get; }

    IGameRandom Random { get; }
}

/// <summary>Fonte de sorteio do módulo. Em testes, uma implementação com semente torna o jogo reproduzível.</summary>
public interface IGameRandom
{
    /// <summary>Inteiro em <c>[0, maxExclusive)</c>.</summary>
    int NextInt(int maxExclusive);

    long NextInt64();

    /// <summary>Embaralha a lista no lugar (Fisher-Yates).</summary>
    void Shuffle<T>(IList<T> list);
}
