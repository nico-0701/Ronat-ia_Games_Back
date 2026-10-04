namespace RonatIa.Games.Domain.Users;

/// <summary>
/// Avatares prontos para escolher. O servidor só guarda a <b>chave</b> (ex.: <c>preset-3</c>); as imagens vivem no Front.
/// Para acrescentar opções, inclua a imagem no Front e aumente <see cref="Count"/> (mudança compatível da API).
/// </summary>
public static class AvatarPresets
{
    public const int Count = 6;

    public const string Default = "preset-1";

    public static IReadOnlyList<string> Keys { get; } = Enumerable.Range(1, Count).Select(i => $"preset-{i}").ToArray();

    public static bool IsValid(string? key) => key is not null && Keys.Contains(key, StringComparer.Ordinal);
}
