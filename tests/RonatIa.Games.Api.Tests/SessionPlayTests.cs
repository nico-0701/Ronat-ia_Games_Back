using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Sessions;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Api.Tests;

/// <summary>Começar e jogar: validações do início, segredo por jogador, permissões, prazo, pontos, trilha, fim e idempotência.</summary>
public sealed class SessionPlayTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Starting_needs_enough_players_and_complete_teams()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);
        var session = await ana.CreateSessionAsync(group.Id);

        var alone = await ana.PostAsync(session.SessionUrl("/start"));
        Assert.Equal("session.not_enough_players", await alone.ReadCodeAsync());

        await beto.PostOkAsync(session.SessionUrl("/join"));
        await carla.PostOkAsync(session.SessionUrl("/join"));
        var noTeams = await ana.PostAsync(session.SessionUrl("/start"));
        Assert.Equal("session.teams_incomplete", await noTeams.ReadCodeAsync());

        var players = (await ana.GetSessionAsync(session.Id)).Players;
        await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = players.Select(p => new { playerId = p.Id, team = 0 }).ToArray() });
        var emptyTeam = await ana.PostAsync(session.SessionUrl("/start"));
        Assert.Equal("session.teams_incomplete", await emptyTeam.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, emptyTeam.StatusCode);

        await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = players[1].Id, team = 1 } } });
        var started = await ana.PostOkAsync(session.SessionUrl("/start"));

        Assert.Equal(SessionStatus.InProgress, started.Status);
        Assert.NotNull(started.StartedAt);
        Assert.NotNull(started.View);
    }

    [Fact]
    public async Task Only_managers_start_and_a_session_starts_only_once()
    {
        var table = await factory.NewRelayTableAsync();

        var byMember = await table.Beto.PostAsync(table.Session.SessionUrl("/start"));
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/start"));
        var twice = await table.Ana.PostAsync(table.Session.SessionUrl("/start"));

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("session.not_waiting", await twice.ReadCodeAsync());
    }

    [Fact]
    public async Task The_lobby_cannot_be_changed_after_the_game_starts()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var anyone = table.Session.Players[0].Id;

        var attempts = new[]
        {
            await table.Ana.PostAsync(table.Session.SessionUrl("/players"), new { memberId = table.Group.MemberOf(table.Beto).Id }),
            await table.Ana.DeleteAsync(table.Session.SessionUrl($"/players/{anyone}")),
            await table.Ana.PutAsync(table.Session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = anyone, team = 1 } } }),
            await table.Ana.PostAsync(table.Session.SessionUrl("/teams/shuffle")),
            await table.Beto.PostAsync(table.Session.SessionUrl("/leave")),
            await table.Beto.PostAsync(table.Session.SessionUrl("/join")),
        };

        foreach (var response in attempts)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("session.not_waiting", await response.ReadCodeAsync());
        }
    }

    [Fact]
    public async Task Only_the_performer_sees_the_secret_word_anywhere()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var seenByAna = await table.Ana.GetSessionAsync(table.Id);
        var word = seenByAna.WordOf();
        Assert.False(string.IsNullOrEmpty(word));

        var eva = await factory.NewPersonAsync("Eva");
        await eva.JoinAsync(table.Group.InviteCode!); // do grupo, mas não joga: só assiste

        foreach (var viewer in new[] { table.Beto, table.Carla, table.Davi, eva })
        {
            var snapshot = await viewer.GetAsync(table.Session.SessionUrl());
            var text = await snapshot.Content.ReadAsStringAsync();
            Assert.DoesNotContain(word!, text, StringComparison.OrdinalIgnoreCase);

            var events = await viewer.GetAsync(table.Session.SessionUrl("/events"));
            Assert.DoesNotContain(word!, await events.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            var list = await viewer.GetAsync($"/api/v1/groups/{table.Group.Id}/sessions");
            Assert.DoesNotContain(word!, await list.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        // O segredo existe no estado guardado (a prova de que o teste enxerga o que protege), mas só sai pela visão de quem pode.
        var stored = await factory.WithDbAsync(db => db.GameSessions.AsNoTracking().Where(s => s.Id == table.Id).Select(s => s.StateJson).SingleAsync());
        Assert.Contains(word!, stored!);
    }

    [Fact]
    public async Task The_server_decides_the_allowed_actions_for_each_viewer()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var eva = await factory.NewPersonAsync("Eva");
        await eva.JoinAsync(table.Group.InviteCode!);

        var ana = await table.Ana.GetSessionAsync(table.Id);   // anfitriã e na vez
        var beto = await table.Beto.GetSessionAsync(table.Id);
        var spectator = await eva.GetSessionAsync(table.Id);

        Assert.Equal(["pass", "skip"], ana.AllowedActions);
        Assert.Equal(["guess"], beto.AllowedActions);
        Assert.Empty(spectator.AllowedActions);
        Assert.Null(spectator.MyPlayerId);
        Assert.False(spectator.CanManage);
        Assert.NotNull(ana.DeadlineAt);
        Assert.Equal(ana.DeadlineAt, beto.DeadlineAt); // o prazo é o mesmo para todos
    }

    [Fact]
    public async Task A_correct_guess_scores_for_the_guessers_team_and_passes_the_turn()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var word = table.Session.WordOf()!;
        var betoPlayer = table.PlayerIdOf(table.Beto);

        var result = await table.Beto.ActOkAsync(table.Id, "guess", new { text = word.ToUpperInvariant() }); // sem diferenciar maiúsculas

        Assert.False(result.Replayed);
        var view = result.Session.ViewOf();
        Assert.Equal(1, view.GetProperty("turn").GetInt32());
        Assert.Equal(betoPlayer, view.GetProperty("performerPlayerId").GetGuid()); // agora a vez é do Beto
        Assert.Equal([0, 1], result.Session.TeamScores.Select(t => t.Score));
        Assert.Equal(1, result.Session.Players.Single(p => p.Id == betoPlayer).Score);
        Assert.NotNull(result.Session.WordOf()); // o Beto é o mímico da vez e vê a palavra nova

        var events = await table.Beto.EventsAsync(table.Id);
        Assert.Equal(["session.started", "relay.turn_started", "action.guess", "relay.guessed"], events.Select(e => e.Type));
        Assert.Equal([1, 2, 3, 4], events.Select(e => e.Seq));
    }

    [Fact]
    public async Task A_wrong_guess_changes_nothing_but_is_recorded_without_the_guess()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var before = table.Session;

        var result = await table.Beto.ActOkAsync(table.Id, "guess", new { text = "banana-secreta-do-palpite" });

        Assert.Equal(0, result.Session.ViewOf().GetProperty("turn").GetInt32());
        Assert.Equal([0, 0], result.Session.TeamScores.Select(t => t.Score));
        Assert.True(result.Session.Version > before.Version);
        var raw = JsonSerializer.Serialize(await table.Beto.EventsAsync(table.Id), TestJson.Options);
        Assert.Contains("relay.wrong_guess", raw);
        Assert.DoesNotContain("banana-secreta-do-palpite", raw); // a trilha não guarda o conteúdo da ação
    }

    [Fact]
    public async Task Rejected_actions_map_to_http_codes_and_change_nothing()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var before = await table.Ana.GetSessionAsync(table.Id);
        var eventsBefore = (await table.Ana.EventsAsync(table.Id)).Count;

        var performerGuess = await table.Ana.ActAsync(table.Id, "guess", new { text = "x" });
        var guesserPass = await table.Beto.ActAsync(table.Id, "pass");
        var memberSkip = await table.Beto.ActAsync(table.Id, "skip");
        var unknown = await table.Beto.ActAsync(table.Id, "dancar");
        var noText = await table.Beto.ActAsync(table.Id, "guess", new { });
        var badPayload = await table.Beto.ActAsync(table.Id, "guess", new { text = 5 });
        var noType = await table.Beto.PostAsync(table.Session.SessionUrl("/actions"), new { clientActionId = Guid.NewGuid() });
        var noId = await table.Beto.PostAsync(table.Session.SessionUrl("/actions"), new { type = "pass" });

        Assert.Equal((HttpStatusCode.Forbidden, "relay.performer_cannot_guess"), (performerGuess.StatusCode, await performerGuess.ReadCodeAsync()));
        Assert.Equal((HttpStatusCode.Forbidden, "relay.not_performer"), (guesserPass.StatusCode, await guesserPass.ReadCodeAsync()));
        Assert.Equal((HttpStatusCode.Forbidden, "relay.host_only"), (memberSkip.StatusCode, await memberSkip.ReadCodeAsync()));
        Assert.Equal((HttpStatusCode.BadRequest, "relay.unknown_action"), (unknown.StatusCode, await unknown.ReadCodeAsync()));
        Assert.Equal((HttpStatusCode.BadRequest, "relay.text_required"), (noText.StatusCode, await noText.ReadCodeAsync()));
        Assert.Equal((HttpStatusCode.BadRequest, "action.invalid_payload"), (badPayload.StatusCode, await badPayload.ReadCodeAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, noType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noId.StatusCode);

        var after = await table.Ana.GetSessionAsync(table.Id);
        Assert.Equal(before.Version, after.Version);                              // nada mudou
        Assert.Equal(eventsBefore, (await table.Ana.EventsAsync(table.Id)).Count); // e nada foi registrado
    }

    [Fact]
    public async Task The_deadline_is_enforced_by_the_server_with_a_grace_period_and_the_host_can_still_skip()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var table = await factory.WithFakeTime(time).NewRelayTableAsync(start: true);
        var word = table.Session.WordOf()!;
        var deadline = table.Session.DeadlineAt!.Value;

        time.Advance(TimeSpan.FromSeconds(62));   // 2 s depois do prazo, dentro da tolerância de 3 s
        var inGrace = await table.Beto.ActAsync(table.Id, "guess", new { text = "errado" });
        time.Advance(TimeSpan.FromSeconds(2));    // 64 s: passou da tolerância
        var late = await table.Beto.ActAsync(table.Id, "guess", new { text = word });

        Assert.Equal(HttpStatusCode.OK, inGrace.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("relay.turn_expired", await late.ReadCodeAsync());
        Assert.True(time.GetUtcNow() > deadline);

        var skipped = await table.Ana.ActOkAsync(table.Id, "skip");
        var next = skipped.Session;
        Assert.Equal(1, next.ViewOf().GetProperty("turn").GetInt32());
        Assert.Equal(time.GetUtcNow().AddSeconds(60), next.DeadlineAt!.Value, TimeSpan.FromSeconds(1)); // prazo novo, contado do servidor
    }

    [Fact]
    public async Task A_host_who_is_not_playing_can_manage_the_game_but_not_guess()
    {
        var table = await factory.NewRelayTableAsync();
        await table.Ana.DeleteAsync(table.Session.SessionUrl($"/players/{table.PlayerIdOf(table.Ana)}"));
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/start"));
        var fromAna = await table.Ana.GetSessionAsync(table.Id);

        var guess = await table.Ana.ActAsync(table.Id, "guess", new { text = "x" });
        var skip = await table.Ana.ActOkAsync(table.Id, "skip");

        Assert.Null(fromAna.MyPlayerId);
        Assert.True(fromAna.CanManage);
        Assert.Equal(["skip"], fromAna.AllowedActions);
        Assert.Null(fromAna.WordOf());
        Assert.Equal(HttpStatusCode.Forbidden, guess.StatusCode);
        Assert.Equal("relay.not_a_player", await guess.ReadCodeAsync());
        Assert.Equal(1, skip.Session.ViewOf().GetProperty("turn").GetInt32());
    }

    [Fact]
    public async Task A_group_member_who_is_not_playing_only_watches()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var eva = await factory.NewPersonAsync("Eva");
        await eva.JoinAsync(table.Group.InviteCode!);

        var snapshot = await eva.GetSessionAsync(table.Id);
        var action = await eva.ActAsync(table.Id, "pass");
        var finish = await eva.PostAsync(table.Session.SessionUrl("/finish"));

        Assert.Equal(SessionStatus.InProgress, snapshot.Status);
        Assert.Equal(4, snapshot.Players.Count);
        Assert.Equal(HttpStatusCode.Forbidden, action.StatusCode);
        Assert.Equal("session.not_a_player", await action.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, finish.StatusCode);
    }

    [Fact]
    public async Task Actions_need_a_game_in_progress()
    {
        var table = await factory.NewRelayTableAsync();

        var inLobby = await table.Beto.ActAsync(table.Id, "guess", new { text = "x" });

        Assert.Equal(HttpStatusCode.Conflict, inLobby.StatusCode);
        Assert.Equal("session.not_in_progress", await inLobby.ReadCodeAsync());
    }

    [Fact]
    public async Task The_game_ends_by_itself_with_the_final_standings_and_results_saved()
    {
        var table = await factory.NewRelayTableAsync(new { turns = 2 }, start: true);
        await table.Beto.ActOkAsync(table.Id, "guess", new { text = table.Session.WordOf() }); // time 1 pontua; vez do Beto
        var betoView = await table.Beto.GetSessionAsync(table.Id);
        var last = await table.Davi.ActOkAsync(table.Id, "guess", new { text = betoView.WordOf() }); // time 1 pontua de novo e acaba

        var final = last.Session;
        Assert.Equal(SessionStatus.Finished, final.Status);
        Assert.NotNull(final.FinishedAt);
        Assert.Equal("finished", final.ViewOf().GetProperty("phase").GetString());
        Assert.Empty(final.AllowedActions);
        Assert.Null(final.DeadlineAt);
        Assert.Equal([0, 2], final.TeamScores.Select(t => t.Score));

        Assert.Equal(4, final.Standings.Count);
        var winners = final.Standings.Where(s => s.IsWinner).Select(s => s.DisplayName).Order().ToList();
        Assert.Equal(["Beto", "Davi"], winners);
        Assert.All(final.Standings.Where(s => s.IsWinner), s => Assert.Equal((1, 2), (s.Rank, s.Score)));
        Assert.All(final.Standings.Where(s => !s.IsWinner), s => Assert.Equal((3, 0), (s.Rank, s.Score))); // classificação de competição: 1, 1, 3, 3

        var rows = await factory.WithDbAsync(db => db.SessionResults.AsNoTracking().Where(r => r.SessionId == table.Id).ToListAsync());
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal("relay", r.GameId));
        Assert.Equal(table.Group.Id, rows[0].GroupId);

        var events = await table.Ana.EventsAsync(table.Id);
        Assert.Equal("session.finished", events[^1].Type);

        var after = await table.Beto.ActAsync(table.Id, "guess", new { text = "x" });
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
        Assert.Equal("session.not_in_progress", await after.ReadCodeAsync());
    }

    [Fact]
    public async Task The_host_can_end_the_game_early_and_the_current_score_decides()
    {
        var table = await factory.NewRelayTableAsync(new { turns = 10 }, start: true);
        await table.Beto.ActOkAsync(table.Id, "guess", new { text = table.Session.WordOf() });

        var notHost = await table.Beto.PostAsync(table.Session.SessionUrl("/finish"));
        var finished = await table.Ana.PostOkAsync(table.Session.SessionUrl("/finish"));
        var twice = await table.Ana.PostAsync(table.Session.SessionUrl("/finish"));

        Assert.Equal(HttpStatusCode.Forbidden, notHost.StatusCode);
        Assert.Equal(SessionStatus.Finished, finished.Status);
        Assert.Equal(["Beto", "Davi"], finished.Standings.Where(s => s.IsWinner).Select(s => s.DisplayName).Order());
        Assert.Equal("session.not_in_progress", await twice.ReadCodeAsync());
    }

    [Fact]
    public async Task A_tie_makes_everyone_a_winner()
    {
        var table = await factory.NewRelayTableAsync(start: true);

        var finished = await table.Ana.PostOkAsync(table.Session.SessionUrl("/finish")); // 0 x 0

        Assert.Equal(4, finished.Standings.Count(s => s.IsWinner));
        Assert.All(finished.Standings, s => Assert.Equal(1, s.Rank));
    }

    [Fact]
    public async Task Finishing_a_session_that_has_not_started_is_refused()
    {
        var table = await factory.NewRelayTableAsync();

        var response = await table.Ana.PostAsync(table.Session.SessionUrl("/finish"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("session.not_in_progress", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Cancelling_a_running_game_stops_the_actions_and_writes_no_results()
    {
        var table = await factory.NewRelayTableAsync(start: true);

        var cancelled = await table.Ana.PostOkAsync(table.Session.SessionUrl("/cancel"));
        var action = await table.Beto.ActAsync(table.Id, "guess", new { text = "x" });

        Assert.Equal(SessionStatus.Cancelled, cancelled.Status);
        Assert.Equal("session.not_in_progress", await action.ReadCodeAsync());
        Assert.Equal(0, await factory.WithDbAsync(db => db.SessionResults.CountAsync(r => r.SessionId == table.Id)));
    }

    [Fact]
    public async Task Repeating_a_client_action_id_does_not_apply_the_action_again()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var word = table.Session.WordOf()!;
        var id = Guid.NewGuid();

        var first = await table.Beto.ActOkAsync(table.Id, "guess", new { text = word }, id);
        var again = await table.Beto.ActOkAsync(table.Id, "guess", new { text = word }, id);

        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Session.Version, again.Session.Version);
        Assert.Equal([0, 1], again.Session.TeamScores.Select(t => t.Score)); // um ponto só, não dois
        Assert.Equal(4, (await table.Beto.EventsAsync(table.Id)).Count);
    }

    [Fact]
    public async Task The_events_trail_is_ordered_continuous_and_paged()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await table.Beto.ActOkAsync(table.Id, "guess", new { text = "errado" });
        await table.Carla.ActOkAsync(table.Id, "guess", new { text = "errado" });

        var all = await table.Ana.EventsAsync(table.Id);
        var page = await table.Ana.EventsAsync(table.Id, after: 2, limit: 2);
        var clamped = await table.Ana.EventsAsync(table.Id, after: 0, limit: 0); // limite mínimo: 1

        Assert.Equal(Enumerable.Range(1, all.Count), all.Select(e => e.Seq));
        Assert.Equal(["session.started", "relay.turn_started", "action.guess", "relay.wrong_guess", "action.guess", "relay.wrong_guess"], all.Select(e => e.Type));
        Assert.Equal([3, 4], page.Select(e => e.Seq));
        Assert.Single(clamped);
        Assert.Equal(table.PlayerIdOf(table.Beto), all[2].ActorPlayerId);
        Assert.Equal(table.PlayerIdOf(table.Beto), all[3].Payload!.Value.GetProperty("playerId").GetGuid());
    }

    [Fact]
    public async Task Ledger_points_are_the_source_of_the_scores()
    {
        var table = await factory.NewRelayTableAsync(new { turns = 5 }, start: true);
        await table.Beto.ActOkAsync(table.Id, "guess", new { text = table.Session.WordOf() });
        var betoView = await table.Beto.GetSessionAsync(table.Id);
        await table.Carla.ActOkAsync(table.Id, "guess", new { text = betoView.WordOf() });

        var entries = await factory.WithDbAsync(db => db.ScoreEntries.AsNoTracking().Where(e => e.SessionId == table.Id).OrderBy(e => e.Id).ToListAsync());
        var snapshot = await table.Ana.GetSessionAsync(table.Id);

        Assert.Equal(2, entries.Count);
        Assert.Equal([1, 0], entries.Select(e => e.TeamNo));
        Assert.All(entries, e => Assert.Equal((1, "guess"), (e.Points, e.Reason)));
        Assert.Equal([1, 1], snapshot.TeamScores.Select(t => t.Score));
        Assert.Equal(2, snapshot.Players.Sum(p => p.Score));
        Assert.Equal(3, entries[0].EventSeq); // aponta para o evento da ação que gerou os pontos
    }
}
