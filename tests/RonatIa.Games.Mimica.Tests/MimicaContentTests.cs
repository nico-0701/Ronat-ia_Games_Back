using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RonatIa.Games.Mimica;

namespace RonatIa.Games.Mimica.Tests;

/// <summary>O conteúdo oficial: as 640 cartas do app original, íntegras e sem repetição.</summary>
public sealed class MimicaContentTests
{
    private static readonly MimicaContent Content = MimicaContent.Default;

    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    [Fact]
    public void The_three_themes_have_the_original_card_counts()
    {
        Assert.Equal(["expressoes", "famosos", "cotidiano"], Content.Categories.Select(c => c.Id));
        Assert.Equal([339, 146, 155], Content.Categories.Select(c => c.Prompts.Count));
        Assert.Equal(640, Content.Categories.Sum(c => c.Prompts.Count));
    }

    [Fact]
    public void Card_ids_are_unique_stable_looking_and_resolvable()
    {
        var ids = Content.Categories.SelectMany(c => c.Prompts.Select(p => p.Id)).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(@"^(exp|fam|cot)-\d{3}$", id));
        Assert.Equal("Pagar o pato", Content.FindPrompt("exp-001")!.Text);
        Assert.Null(Content.FindPrompt("exp-999"));
        Assert.Null(Content.FindCategory("xadrez"));
    }

    [Fact]
    public void No_card_is_repeated_even_ignoring_accents_case_and_spacing()
    {
        var texts = Content.Categories.SelectMany(c => c.Prompts.Select(p => Normalize(p.Text))).ToList();

        var duplicated = texts.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void Card_texts_are_clean_short_and_single_line()
    {
        foreach (var prompt in Content.Categories.SelectMany(c => c.Prompts))
        {
            Assert.False(string.IsNullOrWhiteSpace(prompt.Text), prompt.Id);
            Assert.Equal(prompt.Text.Trim(), prompt.Text);
            Assert.DoesNotContain('\n', prompt.Text);
            Assert.DoesNotContain("  ", prompt.Text);
            Assert.InRange(prompt.Text.Length, 2, 60);
        }
    }

    [Fact]
    public void Each_theme_carries_what_the_client_needs_to_present_it()
    {
        var famosos = Content.FindCategory("famosos")!;

        Assert.Equal("Famosos e personagens", famosos.Title);
        Assert.Equal("Famoso ou personagem:", famosos.Kicker);
        Assert.False(string.IsNullOrWhiteSpace(famosos.Description));
        Assert.Equal("Pelé", famosos.Prompts[0].Text);
    }

    [Fact]
    public void The_content_refuses_empty_themes_and_repeated_ids()
    {
        Assert.Throws<ArgumentException>(() => new MimicaContent([]));
        Assert.Throws<ArgumentException>(() => new MimicaContent([new MimicaCategory("a", "A", "", "", [])]));
        Assert.Throws<ArgumentException>(() => new MimicaContent(
        [
            new MimicaCategory("a", "A", "", "", [new MimicaPrompt("x-1", "um")]),
            new MimicaCategory("b", "B", "", "", [new MimicaPrompt("x-1", "dois")]),
        ]));
    }
}
