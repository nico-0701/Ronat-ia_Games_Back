using RonatIa.Games.Abstractions;
using RonatIa.Games.Mimica;

namespace RonatIa.Games.Mimica.Tests;

/// <summary>O baralho: sorteio no servidor, sem repetir até esgotar e sem depender do que o cliente vê.</summary>
public sealed class MimicaDeckTests
{
    private static MimicaContent Tiny(params (string Category, string[] Ids)[] categories) => new(categories
        .Select(c => new MimicaCategory(c.Category, c.Category, "", c.Category + ":", c.Ids.Select(id => new MimicaPrompt(id, "Texto " + id)).ToList()))
        .ToList());

    private static string CurrentPromptId(Table table) => table.Raw.GetProperty("card").GetProperty("promptId").GetString()!;

    private static string CurrentCategory(Table table) => table.Raw.GetProperty("card").GetProperty("categoryId").GetString()!;

    [Fact]
    public void Cards_do_not_repeat_until_the_theme_is_exhausted()
    {
        var content = Tiny(("t", ["t-1", "t-2", "t-3", "t-4", "t-5"]));
        var table = Table.New(new { rounds = 3 }, content);
        var drawn = new List<string> { CurrentPromptId(table) };

        for (var turn = 0; turn < 4; turn++)
        {
            table.Act(Table.Host, "skipTurn");
            drawn.Add(CurrentPromptId(table));
        }

        Assert.Equal(5, drawn.Distinct().Count()); // as 5 cartas saíram uma vez cada, em ordem sorteada
    }

    [Fact]
    public void When_a_theme_runs_out_it_starts_over_without_repeating_the_last_card_at_once()
    {
        var content = Tiny(("t", ["t-1", "t-2", "t-3"]));
        var table = Table.New(new { rounds = 30 }, content);
        var drawn = new List<string> { CurrentPromptId(table) };

        for (var turn = 0; turn < 40; turn++)
        {
            table.Act(Table.Host, "skipTurn");
            drawn.Add(CurrentPromptId(table));
        }

        for (var i = 1; i < drawn.Count; i++)
        {
            Assert.NotEqual(drawn[i - 1], drawn[i]); // nunca a mesma carta duas vezes seguidas, nem na virada do baralho
        }

        foreach (var cycle in drawn.Chunk(3).Where(c => c.Length == 3))
        {
            Assert.Equal(3, cycle.Distinct().Count()); // cada volta usa todas as cartas antes de repetir
        }
    }

    [Fact]
    public void A_theme_with_a_single_card_can_repeat_it_because_there_is_nothing_else()
    {
        var content = Tiny(("t", ["t-1"]));
        var table = Table.New(new { rounds = 3 }, content);

        table.Act(Table.Host, "skipTurn");

        Assert.Equal("t-1", CurrentPromptId(table));
    }

    [Fact]
    public void Each_theme_keeps_its_own_deck_and_themes_are_drawn_evenly()
    {
        var content = Tiny(("a", ["a-1", "a-2", "a-3", "a-4"]), ("b", ["b-1", "b-2", "b-3", "b-4"]));
        var table = Table.New(new { rounds = 30 }, content, seed: 11);
        var themes = new List<string> { CurrentCategory(table) };
        var ids = new List<string> { CurrentPromptId(table) };

        for (var turn = 0; turn < 59; turn++)
        {
            table.Act(Table.Host, "skipTurn");
            themes.Add(CurrentCategory(table));
            ids.Add(CurrentPromptId(table));
        }

        Assert.Equal(60, themes.Count);
        var a = themes.Count(t => t == "a");
        Assert.InRange(a, 15, 45); // sorteio uniforme entre os temas ativos (com folga estatística)
        Assert.Equal(["a", "b"], themes.Distinct().Order());
        Assert.All(ids, id => Assert.Contains(id, new[] { "a-1", "a-2", "a-3", "a-4", "b-1", "b-2", "b-3", "b-4" }));
    }

    [Fact]
    public void Only_the_active_themes_are_drawn()
    {
        var content = Tiny(("a", ["a-1", "a-2"]), ("b", ["b-1", "b-2"]), ("c", ["c-1", "c-2"]));
        var table = Table.New(new { rounds = 20, categories = new[] { "b" } }, content);

        var themes = new HashSet<string> { CurrentCategory(table) };
        for (var turn = 0; turn < 30; turn++)
        {
            table.Act(Table.Host, "skipTurn");
            themes.Add(CurrentCategory(table));
        }

        Assert.Equal(["b"], themes);
    }

    [Fact]
    public void The_deck_is_kept_in_the_state_so_resuming_does_not_repeat_cards()
    {
        var content = Tiny(("t", Enumerable.Range(1, 8).Select(i => $"t-{i}").ToArray()));
        var table = Table.New(new { rounds = 10 }, content);
        var drawn = new List<string> { CurrentPromptId(table) };

        for (var turn = 0; turn < 4; turn++)
        {
            table.Act(Table.Host, "skipTurn");
            drawn.Add(CurrentPromptId(table));
        }

        // "Retomar" = outra instância do módulo lendo o mesmo estado (como depois de o servidor reiniciar).
        var resumed = new MimicaGame(content);
        var state = table.State;
        var context = new TestContext(Table.Start, seed: 999);
        for (var turn = 0; turn < 3; turn++)
        {
            state = resumed.Apply(state, new GameAction("skipTurn", GameJson.EmptyObject), Table.Host, context).State;
            drawn.Add(state.Data.GetProperty("card").GetProperty("promptId").GetString()!);
        }

        Assert.Equal(8, drawn.Count);
        Assert.Equal(8, drawn.Distinct().Count()); // nenhuma das 8 cartas se repetiu, mesmo com outro sorteio no meio
    }

    [Fact]
    public void The_same_seed_gives_the_same_game_so_tests_are_reproducible()
    {
        string Run(int seed)
        {
            var table = Table.New(new { rounds = 5 }, seed: seed);
            var ids = new List<string> { CurrentPromptId(table) };
            for (var turn = 0; turn < 6; turn++)
            {
                table.Act(Table.Host, "skipTurn");
                ids.Add(CurrentPromptId(table));
            }

            return string.Join(",", ids);
        }

        Assert.Equal(Run(5), Run(5));
        Assert.NotEqual(Run(5), Run(6));
    }

    [Fact]
    public void The_official_content_plays_a_full_game_without_repeating_any_card()
    {
        var table = Table.New(new { rounds = 30 }); // 60 turnos com as 640 cartas
        var ids = new List<string> { CurrentPromptId(table) };

        for (var turn = 0; turn < 59; turn++)
        {
            table.Act(Table.Host, "skipTurn");
            ids.Add(CurrentPromptId(table));
        }

        Assert.Equal(60, ids.Distinct().Count());
        Assert.All(ids, id => Assert.NotNull(MimicaContent.Default.FindPrompt(id)));
    }
}
