using System.Text.Json;

namespace RonatIa.Games.Mimica;

/// <summary>Uma carta: o <see cref="Id"/> é estável (o estado da partida guarda ids, não textos).</summary>
public sealed record MimicaPrompt(string Id, string Text);

/// <summary>Um tema (baralho): ex.: expressões populares, famosos, situações do dia a dia.</summary>
public sealed record MimicaCategory(string Id, string Title, string Description, string Kicker, IReadOnlyList<MimicaPrompt> Prompts);

/// <summary>O conteúdo do jogo. Uma implementação em memória deixa os testes usarem baralhos minúsculos.</summary>
public interface IMimicaContent
{
    IReadOnlyList<MimicaCategory> Categories { get; }

    MimicaCategory? FindCategory(string categoryId);

    MimicaPrompt? FindPrompt(string promptId);
}

/// <summary>
/// As cartas do jogo, lidas de <c>Content/prompts.pt-BR.json</c> (recurso embutido). Para acrescentar cartas ou um tema novo,
/// edite o JSON <b>sem reaproveitar ids</b>: o estado das partidas em andamento guarda ids de cartas já usadas.
/// </summary>
public sealed class MimicaContent : IMimicaContent
{
    private static readonly Lazy<MimicaContent> DefaultContent = new(LoadEmbedded);

    private readonly Dictionary<string, MimicaCategory> _categories;
    private readonly Dictionary<string, MimicaPrompt> _prompts;

    public MimicaContent(IReadOnlyList<MimicaCategory> categories)
    {
        if (categories.Count == 0)
        {
            throw new ArgumentException("O conteúdo precisa de pelo menos um tema.", nameof(categories));
        }

        _categories = categories.ToDictionary(c => c.Id, StringComparer.Ordinal);
        _prompts = [];

        foreach (var category in categories)
        {
            if (category.Prompts.Count == 0)
            {
                throw new ArgumentException($"O tema \"{category.Id}\" não tem cartas.", nameof(categories));
            }

            foreach (var prompt in category.Prompts)
            {
                if (!_prompts.TryAdd(prompt.Id, prompt))
                {
                    throw new ArgumentException($"A carta \"{prompt.Id}\" aparece duas vezes.", nameof(categories));
                }
            }
        }

        Categories = categories;
    }

    /// <summary>O conteúdo oficial (carregado uma vez).</summary>
    public static MimicaContent Default => DefaultContent.Value;

    public IReadOnlyList<MimicaCategory> Categories { get; }

    public MimicaCategory? FindCategory(string categoryId) => _categories.GetValueOrDefault(categoryId);

    public MimicaPrompt? FindPrompt(string promptId) => _prompts.GetValueOrDefault(promptId);

    public static MimicaContent FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<ContentFile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("O arquivo de cartas está vazio.");

        return new MimicaContent(file.Categories
            .Select(c => new MimicaCategory(c.Id, c.Title, c.Description, c.Kicker, c.Prompts.Select(p => new MimicaPrompt(p.Id, p.Text)).ToList()))
            .ToList());
    }

    private static MimicaContent LoadEmbedded()
    {
        using var stream = typeof(MimicaContent).Assembly.GetManifestResourceStream("prompts.pt-BR.json")
            ?? throw new InvalidOperationException("O recurso prompts.pt-BR.json não foi encontrado no módulo da Mímica.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    private sealed record ContentFile(int Version, List<CategoryFile> Categories);

    private sealed record CategoryFile(string Id, string Title, string Description, string Kicker, List<PromptFile> Prompts);

    private sealed record PromptFile(string Id, string Text);
}
