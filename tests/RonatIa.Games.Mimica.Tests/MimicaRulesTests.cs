using RonatIa.Games.Abstractions;
using RonatIa.Games.Mimica;

namespace RonatIa.Games.Mimica.Tests;

/// <summary>As regras do jogo (as do app original, agora no servidor), sem banco nem rede: só o módulo puro.</summary>
public sealed class MimicaRulesTests
{
    private static readonly GameActor A1 = Table.Player(Table.A1);
    private static readonly GameActor A2 = Table.Player(Table.A2);
    private static readonly GameActor B1 = Table.Player(Table.B1);
    private static readonly GameActor B2 = Table.Player(Table.B2);

    [Fact]
    public void The_game_starts_on_team_one_with_the_first_player_of_the_roster_waiting_to_begin()
    {
        var table = Table.New();

        Assert.Equal("turnIntro", table.Phase);
        Assert.Equal(Table.A1, table.Performer);
        Assert.Equal(0, table.ViewJson(Table.Spectator).GetProperty("team").GetInt32());
        Assert.Equal((0, 20), (table.ViewJson(Table.Spectator).GetProperty("turn").GetInt32(), table.ViewJson(Table.Spectator).GetProperty("totalTurns").GetInt32()));
        Assert.Equal(1, table.ViewJson(Table.Spectator).GetProperty("round").GetInt32());
        Assert.Equal([0, 0], [table.Score(0), table.Score(1)]);
        Assert.Equal("mimica.turn_ready", Assert.Single(table.LastTransition.Events).Type);
        Assert.Empty(table.LastTransition.Points);
        Assert.False(table.LastTransition.IsFinished);
    }

    [Fact]
    public void The_performer_rotates_inside_each_team_exactly_like_the_original_formula()
    {
        var table = Table.New(new { rounds = 4 });
        var performers = new List<Guid>();

        for (var turn = 0; turn < 8; turn++)
        {
            performers.Add(table.Performer);
            table.PlayTurn("hit");
        }

        // turno par = time 0, ímpar = time 1; roster[time][⌊turno/2⌋ mod tamanho]
        Assert.Equal([Table.A1, Table.B1, Table.A2, Table.B2, Table.A1, Table.B1, Table.A2, Table.B2], performers);
    }

    [Fact]
    public void A_team_with_one_player_always_uses_the_same_performer()
    {
        var game = new MimicaGame();
        var context = new TestContext(Table.Start);
        var config = game.ValidateConfig(GameJson.Parse("""{"rounds":3}""")).Normalized!.Value;
        var setup = new GameSetup(
            Guid.NewGuid(),
            [new SetupPlayer(Table.A1, 0, 0), new SetupPlayer(Table.B1, 1, 1), new SetupPlayer(Table.B2, 1, 2)],
            config);

        var state = game.Start(setup, context).State;
        var performers = new List<Guid>();
        for (var turn = 0; turn < 6; turn++)
        {
            performers.Add(game.Project(state, Table.Spectator, context).View.GetProperty("performerPlayerId").GetGuid());
            state = game.Apply(state, new GameAction("skipTurn", GameJson.EmptyObject), Table.Host, context).State;
        }

        Assert.Equal([Table.A1, Table.B1, Table.A1, Table.B2, Table.A1, Table.B1], performers);
    }

    [Fact]
    public void A_hit_scores_one_point_for_the_team_in_turn_and_passes_the_turn()
    {
        var table = Table.New();
        table.BeginPlaying();

        var transition = table.Act(A1, "reportHit");

        var point = Assert.Single(transition.Points);
        Assert.Equal((null, 0, 1, "hit"), (point.PlayerId, point.Team, point.Points, point.Reason));
        Assert.Equal([1, 0], [table.Score(0), table.Score(1)]);
        Assert.Equal("turnIntro", table.Phase);
        Assert.Equal(Table.B1, table.Performer);
        Assert.Equal(["mimica.turn_ended", "mimica.turn_ready"], transition.Events.Select(e => e.Type));
        Assert.False(transition.IsFinished);
    }

    [Fact]
    public void A_miss_opens_a_steal_chance_and_a_steal_hit_scores_for_the_opponent()
    {
        var table = Table.New();
        table.BeginPlaying();

        var miss = table.Act(A1, "reportMiss");
        Assert.Equal("steal", table.Phase);
        Assert.Empty(miss.Points);
        Assert.Equal("mimica.miss", Assert.Single(miss.Events).Type);

        var steal = table.Act(A1, "reportStealHit");

        var point = Assert.Single(steal.Points);
        Assert.Equal((1, 1, "steal_hit"), (point.Team, point.Points, point.Reason)); // o adversário (time 1) leva o ponto
        Assert.Equal([0, 1], [table.Score(0), table.Score(1)]);
        Assert.Equal(Table.B1, table.Performer);
    }

    [Fact]
    public void A_steal_miss_scores_nobody()
    {
        var table = Table.New();
        table.PlayTurn("stealMiss");

        Assert.Equal([0, 0], [table.Score(0), table.Score(1)]);
        Assert.Equal(Table.B1, table.Performer);
        Assert.Equal("turnIntro", table.Phase);
    }

    [Fact]
    public void The_host_can_skip_any_turn_without_points()
    {
        var table = Table.New();

        var skipped = table.Act(Table.Host, "skipTurn");

        Assert.Empty(skipped.Points);
        Assert.Equal(Table.B1, table.Performer);
        Assert.Equal("skipped", skipped.Events[0].Payload!.Value.GetProperty("outcome").GetString());
    }

    [Fact]
    public void The_host_can_skip_in_the_middle_of_a_turn_and_in_a_steal()
    {
        var table = Table.New();
        table.BeginPlaying();
        table.Act(Table.Host, "skipTurn");
        Assert.Equal(Table.B1, table.Performer);

        table.BeginPlaying();
        table.Act(B1, "reportMiss");
        table.Act(Table.Host, "skipTurn");

        Assert.Equal(Table.A2, table.Performer);
        Assert.Equal([0, 0], [table.Score(0), table.Score(1)]);
    }

    [Fact]
    public void Only_the_performer_or_the_host_gives_the_verdict()
    {
        var table = Table.New();
        table.BeginPlaying();

        foreach (var intruder in new[] { A2, B1, B2, Table.Spectator })
        {
            foreach (var type in new[] { "startTurn", "reportHit", "reportMiss", "reportStealHit", "reportStealMiss" })
            {
                var violation = Assert.Throws<RuleViolation>(() => table.Try(intruder, type));
                Assert.Equal((RuleViolationKind.NotAllowed, "mimica.not_performer"), (violation.Kind, violation.Code));
            }

            var skip = Assert.Throws<RuleViolation>(() => table.Try(intruder, "skipTurn"));
            Assert.Equal("mimica.host_only", skip.Code);
        }

        table.Act(Table.Host, "reportHit"); // o anfitrião cobre o mímico (celular na mesa, desconexão)
        Assert.Equal([1, 0], [table.Score(0), table.Score(1)]);
    }

    [Fact]
    public void Starting_the_turn_is_for_the_performer_or_the_host_and_only_once()
    {
        var table = Table.New();

        Assert.Equal("mimica.not_performer", Assert.Throws<RuleViolation>(() => table.Try(B1, "startTurn")).Code);
        table.Act(Table.Host, "startTurn");

        var again = Assert.Throws<RuleViolation>(() => table.Try(A1, "startTurn"));
        Assert.Equal((RuleViolationKind.InvalidState, "mimica.turn_already_started"), (again.Kind, again.Code));
    }

    [Fact]
    public void The_three_second_prep_turns_into_playing_by_the_clock_alone()
    {
        var table = Table.New();
        table.Act(A1, "startTurn");

        Assert.Equal("prep", table.Phase);
        var early = Assert.Throws<RuleViolation>(() => table.Try(A1, "reportHit"));
        Assert.Equal((RuleViolationKind.InvalidState, "mimica.not_playing"), (early.Kind, early.Code));

        table.Now += TimeSpan.FromSeconds(2.9);
        Assert.Equal("prep", table.Phase);

        table.Now += TimeSpan.FromSeconds(0.1);
        Assert.Equal("playing", table.Phase); // sem nenhuma ação: derivado do relógio
        table.Act(A1, "reportHit");
        Assert.Equal(1, table.Score(0));
    }

    [Fact]
    public void Timeouts_follow_the_original_the_clock_hitting_zero_does_not_end_the_turn()
    {
        var table = Table.New(new { turnSeconds = 30, lateGraceSeconds = 3 });
        table.BeginPlaying();                       // jogando desde t+3; o prazo é t+33

        table.Now += TimeSpan.FromSeconds(30 + 3);  // t+36 = prazo + tolerância: ainda vale o acerto
        Assert.Equal("playing", table.Phase);
        table.AssertHitAccepted();

        var late = Table.New(new { turnSeconds = 30, lateGraceSeconds = 3 });
        late.BeginPlaying();
        late.Now += TimeSpan.FromSeconds(30 + 3 + 0.01);
        var expired = Assert.Throws<RuleViolation>(() => late.Try(A1, "reportHit"));
        Assert.Equal((RuleViolationKind.InvalidState, "mimica.turn_expired"), (expired.Kind, expired.Code));

        // Passado o prazo, o turno continua aberto: o mímico ainda pode registrar que não acertou e abrir o roubo.
        late.Now += TimeSpan.FromMinutes(10);
        late.Act(A1, "reportMiss");
        Assert.Equal("steal", late.Phase);
    }

    [Fact]
    public void The_grace_period_is_configurable_and_can_be_zero()
    {
        var table = Table.New(new { turnSeconds = 30, lateGraceSeconds = 0 });
        table.BeginPlaying();

        table.Now += TimeSpan.FromSeconds(30);
        table.AssertHitAccepted(); // exatamente no prazo, ainda vale

        var strict = Table.New(new { turnSeconds = 30, lateGraceSeconds = 0 });
        strict.BeginPlaying();
        strict.Now += TimeSpan.FromSeconds(30.001);
        Assert.Equal("mimica.turn_expired", Assert.Throws<RuleViolation>(() => strict.Try(A1, "reportHit")).Code);
    }

    [Fact]
    public void The_steal_chance_lasts_thirty_seconds_plus_the_grace_and_a_steal_miss_is_always_allowed()
    {
        var table = Table.New();
        table.BeginPlaying();
        table.Act(A1, "reportMiss");

        var view = table.ViewJson(Table.Spectator);
        Assert.Equal(table.Now.AddSeconds(MimicaGame.StealSeconds), view.GetProperty("stealEndsAt").GetDateTimeOffset());

        table.Now += TimeSpan.FromSeconds(MimicaGame.StealSeconds + 3 + 0.01);
        var expired = Assert.Throws<RuleViolation>(() => table.Try(A1, "reportStealHit"));
        Assert.Equal((RuleViolationKind.InvalidState, "mimica.steal_expired"), (expired.Kind, expired.Code));

        table.Act(A1, "reportStealMiss");
        Assert.Equal("turnIntro", table.Phase);
        Assert.Equal([0, 0], [table.Score(0), table.Score(1)]);
    }

    [Fact]
    public void Actions_out_of_phase_are_refused_with_conflict_codes()
    {
        var table = Table.New();

        Assert.Equal("mimica.not_playing", Assert.Throws<RuleViolation>(() => table.Try(A1, "reportHit")).Code);
        Assert.Equal("mimica.not_playing", Assert.Throws<RuleViolation>(() => table.Try(A1, "reportMiss")).Code);
        Assert.Equal("mimica.not_stealing", Assert.Throws<RuleViolation>(() => table.Try(A1, "reportStealHit")).Code);
        Assert.Equal("mimica.not_stealing", Assert.Throws<RuleViolation>(() => table.Try(A1, "reportStealMiss")).Code);

        table.BeginPlaying();
        Assert.Equal("mimica.not_stealing", Assert.Throws<RuleViolation>(() => table.Try(A1, "reportStealHit")).Code);
        table.Act(A1, "reportMiss");
        Assert.Equal("mimica.not_playing", Assert.Throws<RuleViolation>(() => table.Try(A1, "reportHit")).Code);
        Assert.Equal("mimica.not_playing", Assert.Throws<RuleViolation>(() => table.Try(A1, "reportMiss")).Code);
    }

    [Fact]
    public void Unknown_actions_are_refused_as_malformed()
    {
        var table = Table.New();

        var violation = Assert.Throws<RuleViolation>(() => table.Try(A1, "dancar"));

        Assert.Equal((RuleViolationKind.InvalidAction, "mimica.unknown_action"), (violation.Kind, violation.Code));
    }

    [Fact]
    public void A_refused_action_leaves_the_state_untouched()
    {
        var table = Table.New();
        var before = table.State.Data.GetRawText();

        Assert.Throws<RuleViolation>(() => table.Try(B1, "reportHit"));
        Assert.Throws<RuleViolation>(() => table.Try(A1, "reportHit"));

        Assert.Equal(before, table.State.Data.GetRawText());
    }

    [Fact]
    public void Apply_never_changes_the_state_it_receives()
    {
        var table = Table.New();
        table.BeginPlaying();
        var before = table.State.Data.GetRawText();

        var next = table.Try(A1, "reportHit");

        Assert.Equal(before, table.State.Data.GetRawText());
        Assert.NotEqual(before, next.State.Data.GetRawText());
    }

    [Fact]
    public void The_game_ends_after_rounds_times_two_turns_and_the_best_score_wins()
    {
        var table = Table.New(new { rounds = 2 }); // 4 turnos
        table.PlayTurn("hit");        // time 0: +1
        table.PlayTurn("stealMiss");  // time 1: 0
        table.PlayTurn("hit");        // time 0: +1
        var last = LastTurn(table);   // time 1: acerta

        Assert.True(last.IsFinished);
        Assert.Equal("finished", table.Phase);
        Assert.Equal("mimica.finished", last.Events[^1].Type);
        Assert.Equal([2, 1], [table.Score(0), table.Score(1)]);

        var result = table.Game.Finish(table.State);
        Assert.Equal(4, result.Standings.Count);
        Assert.All(result.Standings.Where(s => s.Team == 0), s => Assert.Equal((2, 1, true), (s.Score, s.Rank, s.IsWinner)));
        Assert.All(result.Standings.Where(s => s.Team == 1), s => Assert.Equal((1, 2, false), (s.Score, s.Rank, s.IsWinner)));
        Assert.Equal([Table.A1, Table.A2, Table.B1, Table.B2], result.Standings.Select(s => s.PlayerId).Order());
    }

    private static GameTransition LastTurn(Table table)
    {
        table.BeginPlaying();
        return table.Act(Table.Player(table.Performer), "reportHit");
    }

    [Fact]
    public void A_tie_makes_everyone_a_winner()
    {
        var table = Table.New(new { rounds = 1 }); // 2 turnos
        table.PlayTurn("hit");
        table.PlayTurn("hit");

        var result = table.Game.Finish(table.State);

        Assert.Equal([1, 1], [table.Score(0), table.Score(1)]);
        Assert.All(result.Standings, s => Assert.Equal((1, true), (s.Rank, s.IsWinner)));
    }

    [Fact]
    public void Finish_can_classify_a_game_stopped_halfway()
    {
        var table = Table.New(new { rounds = 10 });
        table.PlayTurn("hit");

        var result = table.Game.Finish(table.State);

        Assert.Equal(4, result.Standings.Count);
        Assert.Equal([Table.A1, Table.A2], result.Standings.Where(s => s.IsWinner).Select(s => s.PlayerId).Order());
    }

    [Fact]
    public void After_the_end_every_action_is_refused()
    {
        var table = Table.New(new { rounds = 1 });
        table.PlayTurn("hit");
        table.PlayTurn("hit");

        foreach (var type in new[] { "startTurn", "reportHit", "reportMiss", "skipTurn" })
        {
            var violation = Assert.Throws<RuleViolation>(() => table.Try(Table.Host, type));
            Assert.Equal((RuleViolationKind.InvalidState, "mimica.finished"), (violation.Kind, violation.Code));
        }
    }

    [Fact]
    public void Starting_needs_both_teams()
    {
        var game = new MimicaGame();
        var config = game.ValidateConfig(null).Normalized!.Value;
        var setup = new GameSetup(Guid.NewGuid(), [new SetupPlayer(Table.A1, 0, 0), new SetupPlayer(Table.A2, 0, 1)], config);

        var violation = Assert.Throws<RuleViolation>(() => game.Start(setup, new TestContext(Table.Start)));

        Assert.Equal("mimica.team_empty", violation.Code);
    }

    [Fact]
    public void Points_always_target_a_team_never_a_single_player()
    {
        var table = Table.New(new { rounds = 3 });
        var all = new List<ScoreChange>();
        foreach (var outcome in new[] { "hit", "stealHit", "stealMiss", "hit", "hit", "stealHit" })
        {
            table.PlayTurn(outcome);
            all.AddRange(table.LastTransition.Points);
        }

        Assert.NotEmpty(all);
        Assert.All(all, p => Assert.True(p.PlayerId is null && p.Team is 0 or 1 && p.Points == 1));
    }

    [Fact]
    public void The_state_stays_small_even_after_a_long_game()
    {
        var table = Table.New(new { rounds = 30 });
        for (var turn = 0; turn < 60; turn++)
        {
            table.PlayTurn("hit");
        }

        Assert.True(table.State.Data.GetRawText().Length < 4_000, $"estado com {table.State.Data.GetRawText().Length} caracteres");
    }
}

internal static class TableExtensions
{
    /// <summary>Confere que o acerto do mímico da vez é aceito agora.</summary>
    public static void AssertHitAccepted(this Table table)
    {
        var before = table.Score(0) + table.Score(1);
        table.Act(Table.Player(table.Performer), "reportHit");
        Assert.Equal(before + 1, table.Score(0) + table.Score(1));
    }
}
