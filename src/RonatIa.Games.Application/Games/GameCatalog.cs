using RonatIa.Games.Abstractions;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Application.Games;

/// <summary>Os jogos instalados (módulos registrados na DI). O catálogo vive no código: não há tabela de jogos.</summary>
public interface IGameCatalog
{
    IReadOnlyList<IGameModule> All { get; }

    IGameModule? Find(string gameId);

    /// <summary>O módulo do jogo ou <c>game.not_found</c>.</summary>
    IGameModule Require(string gameId);
}

public sealed class GameCatalog : IGameCatalog
{
    private readonly Dictionary<string, IGameModule> _byId;

    public GameCatalog(IEnumerable<IGameModule> modules)
    {
        var list = modules.OrderBy(module => module.Definition.Id, StringComparer.Ordinal).ToList();
        var duplicate = list.GroupBy(module => module.Definition.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Há dois módulos de jogo com o mesmo id \"{duplicate.Key}\".");
        }

        All = list;
        _byId = list.ToDictionary(module => module.Definition.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<IGameModule> All { get; }

    public IGameModule? Find(string gameId) => _byId.GetValueOrDefault(gameId);

    public IGameModule Require(string gameId) =>
        Find(gameId) ?? throw AppException.NotFound("game.not_found", "Jogo não encontrado.");
}

/// <summary>Contexto entregue ao módulo: o horário vem do <see cref="TimeProvider"/> da aplicação (testável) e o sorteio é injetado.</summary>
public sealed class GameContext(DateTimeOffset now, IGameRandom random) : IGameContext
{
    public DateTimeOffset Now { get; } = now;

    public IGameRandom Random { get; } = random;
}

/// <summary>Sorteio de jogo baseado no gerador do sistema (não precisa ser criptográfico: é embaralhar cartas e times).</summary>
public sealed class SystemGameRandom : IGameRandom
{
    public int NextInt(int maxExclusive) => Random.Shared.Next(maxExclusive);

    public long NextInt64() => Random.Shared.NextInt64();

    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
