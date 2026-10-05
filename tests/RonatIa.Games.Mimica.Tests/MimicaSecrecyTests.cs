using System.Text.Json;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Mimica;

namespace RonatIa.Games.Mimica.Tests;

/// <summary>A carta é segredo do mímico: só sai pela projeção de quem pode vê-la, e a trilha de eventos nunca a carrega.</summary>
public sealed class MimicaSecrecyTests
{
    private static readonly GameActor A1 = Table.Player(Table.A1);
    private static readonly GameActor A2 = Table.Player(Table.A2);
    private static readonly GameActor B1 = Table.Player(Table.B1);

    /// <summary>O texto da carta da vez, lido do estado guardado no servidor (o que a visão do mímico mostra).</summary>
    private static string CurrentCardText(Table table)
    {
        var card = table.Raw.GetProperty("card");
        var content = MimicaContent.Default;
        return content.FindPrompt(card.GetProperty("promptId").GetString()!)!.Text;
    }

    [Fact]
    public void Nobody_sees_the_card_while_the_turn_has_not_started()
    {
        var table = Table.New();

        foreach (var viewer in new[] { A1, A2, B1, Table.Host, Table.Spectator })
        {
            Assert.Equal(JsonValueKind.Null, table.ViewJson(viewer).GetProperty("card").ValueKind);
        }
    }

    [Theory]
    [InlineData("prep")]
    [InlineData("playing")]
    [InlineData("steal")]
    public void Only_the_performer_sees_the_card_during_prep_play_and_steal(string phase)
    {
        var table = Table.New();
        table.Act(A1, "startTurn");
        if (phase != "prep")
        {
            table.Now += TimeSpan.FromSeconds(MimicaGame.PrepSeconds);
        }

        if (phase == "steal")
        {
            table.Act(A1, "reportMiss");
        }

        Assert.Equal(phase, table.Phase);
        var text = CurrentCardText(table);

        var seen = table.ViewJson(A1).GetProperty("card");
        Assert.Equal(text, seen.GetProperty("text").GetString());
        Assert.False(string.IsNullOrEmpty(seen.GetProperty("kicker").GetString()));

        foreach (var viewer in new[] { A2, B1, Table.Player(Table.B2), Table.Host, Table.Spectator })
        {
            var json = table.ViewJson(viewer).GetRawText();
            Assert.Equal(JsonValueKind.Null, table.ViewJson(viewer).GetProperty("card").ValueKind);
            Assert.DoesNotContain(text, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(table.Raw.GetProperty("card").GetProperty("promptId").GetString()!, json); // nem o id da carta
        }
    }

    [Fact]
    public void The_host_sees_the_card_only_when_the_performer_has_no_account()
    {
        var withAccount = Table.New();
        withAccount.Act(A1, "startTurn");
        var withProfile = Table.New(noAccount: Table.A1); // o mímico da vez é um perfil sem celular
        withProfile.Act(Table.Host, "startTurn");

        Assert.Equal(JsonValueKind.Null, withAccount.ViewJson(Table.Host).GetProperty("card").ValueKind);
        Assert.Equal(CurrentCardText(withProfile), withProfile.ViewJson(Table.Host).GetProperty("card").GetProperty("text").GetString());
        // um colega de time do perfil, mesmo sendo "anfitrião" só por ser jogador, não vê: o que vale é o papel de anfitrião
        Assert.Equal(JsonValueKind.Null, withProfile.ViewJson(A2).GetProperty("card").ValueKind);
    }

    [Fact]
    public void After_the_turn_ends_the_card_is_revealed_to_everyone_in_the_summary()
    {
        var table = Table.New();
        table.BeginPlaying();
        var text = CurrentCardText(table);

        table.Act(A1, "reportHit");

        foreach (var viewer in new[] { A1, A2, B1, Table.Host, Table.Spectator })
        {
            var last = table.ViewJson(viewer).GetProperty("lastTurn");
            Assert.Equal(text, last.GetProperty("card").GetProperty("text").GetString());
            Assert.Equal("hit", last.GetProperty("outcome").GetString());
            Assert.Equal(Table.A1, last.GetProperty("performerPlayerId").GetGuid());
        }

        Assert.Equal(JsonValueKind.Null, table.ViewJson(A1).GetProperty("card").ValueKind); // a carta nova ainda não começou
    }

    [Fact]
    public void The_events_trail_carries_ids_never_the_text_of_a_card()
    {
        var table = Table.New(new { rounds = 3 });
        var texts = new List<string>();
        for (var turn = 0; turn < 6; turn++)
        {
            table.Act(Table.Player(table.Performer), "startTurn");
            texts.Add(CurrentCardText(table));
            table.Now += TimeSpan.FromSeconds(MimicaGame.PrepSeconds);
            table.Act(Table.Player(table.Performer), turn % 2 == 0 ? "reportHit" : "reportMiss");
            if (turn % 2 == 1)
            {
                table.Act(Table.Player(table.Performer), "reportStealMiss");
            }
        }

        var trail = JsonSerializer.Serialize(table.Events.Select(e => new { e.Type, e.Payload, e.ActorPlayerId }), GameJson.Options);
        foreach (var text in texts)
        {
            Assert.DoesNotContain(text, trail, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("promptId", trail);
    }

    [Fact]
    public void Who_can_act_is_computed_by_the_server_for_each_viewer_and_phase()
    {
        var table = Table.New();

        Assert.Equal(["startTurn"], table.View(A1).AllowedActions);
        Assert.Equal(["startTurn", "skipTurn"], table.View(Table.Host).AllowedActions);
        Assert.Empty(table.View(A2).AllowedActions);
        Assert.Empty(table.View(B1).AllowedActions);
        Assert.Empty(table.View(Table.Spectator).AllowedActions);

        table.Act(A1, "startTurn");
        Assert.Empty(table.View(A1).AllowedActions); // durante o preparo de 3 s não há veredito

        table.Now += TimeSpan.FromSeconds(MimicaGame.PrepSeconds);
        Assert.Equal(["reportHit", "reportMiss"], table.View(A1).AllowedActions);
        Assert.Equal(["reportHit", "reportMiss", "skipTurn"], table.View(Table.Host).AllowedActions);
        Assert.Empty(table.View(B1).AllowedActions);

        table.Act(A1, "reportMiss");
        Assert.Equal(["reportStealHit", "reportStealMiss"], table.View(A1).AllowedActions);
    }

    [Fact]
    public void The_hit_button_disappears_after_the_deadline_plus_grace_but_the_miss_button_stays()
    {
        var table = Table.New(new { turnSeconds = 30, lateGraceSeconds = 3 });
        table.BeginPlaying();

        table.Now += TimeSpan.FromSeconds(30 + 3 + 1);

        Assert.Equal(["reportMiss"], table.View(A1).AllowedActions);
    }

    [Fact]
    public void The_deadline_shown_follows_the_phase()
    {
        var table = Table.New(new { turnSeconds = 40 });
        Assert.Null(table.View(A1).DeadlineAt);

        table.Act(A1, "startTurn");
        Assert.Equal(table.Now.AddSeconds(3), table.View(A1).DeadlineAt);

        table.Now += TimeSpan.FromSeconds(3);
        Assert.Equal(Table.Start.AddSeconds(3 + 40), table.View(Table.Spectator).DeadlineAt);

        table.Act(A1, "reportMiss");
        Assert.Equal(table.Now.AddSeconds(30), table.View(Table.Spectator).DeadlineAt);

        table.Act(A1, "reportStealMiss");
        Assert.Null(table.View(Table.Spectator).DeadlineAt);
    }

    [Fact]
    public void A_finished_game_shows_no_card_no_actions_and_no_deadline()
    {
        var table = Table.New(new { rounds = 1 });
        table.PlayTurn("hit");
        table.PlayTurn("hit");

        foreach (var viewer in new[] { A1, B1, Table.Host, Table.Spectator })
        {
            var view = table.View(viewer);
            Assert.Empty(view.AllowedActions);
            Assert.Null(view.DeadlineAt);
            Assert.Equal(JsonValueKind.Null, view.View.GetProperty("card").ValueKind);
            Assert.Equal("finished", view.View.GetProperty("phase").GetString());
        }
    }

    [Fact]
    public void The_spectator_view_is_the_public_view_with_scores_and_whose_turn_it_is()
    {
        var table = Table.New();
        table.PlayTurn("hit");

        var view = table.ViewJson(Table.Spectator);

        Assert.Equal(Table.B1, view.GetProperty("performerPlayerId").GetGuid());
        Assert.Equal(1, view.GetProperty("scores").GetProperty("0").GetInt32());
        Assert.Equal(10, view.GetProperty("totalRounds").GetInt32());
        Assert.Equal(60, view.GetProperty("turnSeconds").GetInt32());
        Assert.Equal((3, 30), (view.GetProperty("prepSeconds").GetInt32(), view.GetProperty("stealSeconds").GetInt32()));
    }
}
