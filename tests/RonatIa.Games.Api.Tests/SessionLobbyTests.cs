using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Application.Sessions;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Api.Tests;

/// <summary>O lobby: catálogo, criação, entrada e saída, times, configuração, cancelamento e revanche.</summary>
public sealed class SessionLobbyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_catalog_lists_the_installed_games_with_their_limits()
    {
        var ana = await factory.NewPersonAsync("Ana");

        var response = await ana.GetAsync("/api/v1/games");
        var games = await response.ReadAsAsync<List<GameDto>>();
        var relay = games.Single(g => g.Id == "relay");
        var solo = games.Single(g => g.Id == "solo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((2, 6, 2, 1), (relay.MinPlayers, relay.MaxPlayers, relay.TeamCount, relay.MinPlayersPerTeam));
        Assert.Equal(4, relay.ConfigDefaults.GetProperty("turns").GetInt32());
        Assert.Equal(0, solo.TeamCount);

        var one = await ana.GetAsync("/api/v1/games/relay");
        Assert.Equal("relay", (await one.ReadAsAsync<GameDto>()).Id);
        var missing = await ana.GetAsync("/api/v1/games/xadrez");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("game.not_found", await missing.ReadCodeAsync());
    }

    [Fact]
    public async Task The_catalog_requires_a_login()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/games")).StatusCode);
    }

    [Fact]
    public async Task Creating_a_session_makes_the_creator_the_host_and_a_player_with_a_normalized_config()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var response = await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "relay", config = new { turns = 7 } });
        var session = await response.ReadAsAsync<GameSessionDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/v1/sessions/{session.Id}", response.Headers.Location?.ToString());
        Assert.Equal(SessionStatus.Waiting, session.Status);
        Assert.Equal(group.MyMemberId, session.HostMemberId);
        Assert.True(session.CanManage);
        Assert.Equal(7, session.Config.GetProperty("turns").GetInt32());
        Assert.Equal(60, session.Config.GetProperty("turnSeconds").GetInt32()); // o que faltou assume o padrão
        var host = Assert.Single(session.Players);
        Assert.Equal("Ana", host.DisplayName);
        Assert.True(host.IsMe);
        Assert.Equal(session.MyPlayerId, host.Id);
        Assert.Null(session.View);
        Assert.Empty(session.AllowedActions);
        Assert.Equal([0, 1], session.TeamScores.Select(t => t.Team));
    }

    [Theory]
    [InlineData("""{"turns":0}""", "turns")]
    [InlineData("""{"turns":99}""", "turns")]
    [InlineData("""{"turns":"muitos"}""", "turns")]
    [InlineData("""{"turnSeconds":1}""", "turnSeconds")]
    public async Task An_invalid_config_is_refused_by_the_game_with_field_errors(string config, string field)
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var response = await ana.Client.PostAsync(
            "/api/v1/sessions",
            new StringContent($$"""{"groupId":"{{group.Id}}","gameId":"relay","config":{{config}}}""", System.Text.Encoding.UTF8, "application/json"));
        var problem = await response.ReadProblemAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("session.invalid_config", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(await ana.ListSessionsAsync(group.Id));
    }

    [Fact]
    public async Task Unknown_games_and_outsiders_cannot_create_sessions()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var intruder = await factory.NewPersonAsync("Intruso");
        var group = await ana.CreateGroupAsync();

        var unknown = await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "xadrez" });
        var outsider = await intruder.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "relay" });
        var missing = await ana.PostAsync("/api/v1/sessions", new { gameId = "relay" });

        Assert.Equal("game.not_found", await unknown.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.Equal("group.not_found", await outsider.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task A_group_can_have_only_so_many_open_sessions()
    {
        var limited = factory.WithSettings(("Sessions:MaxActiveSessionsPerGroup", "2"));
        var ana = await limited.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var first = await ana.CreateSessionAsync(group.Id);
        await ana.CreateSessionAsync(group.Id);

        var third = await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "relay" });
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("session.limit_reached", await third.ReadCodeAsync());

        await ana.PostOkAsync(first.SessionUrl("/cancel")); // cancelar libera a vaga
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "relay" })).StatusCode);
    }

    [Fact]
    public async Task Creating_sessions_is_rate_limited_per_person()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:SessionCreatePerHour", "2"));
        var ana = await limited.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "relay" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Listing_shows_open_sessions_first_and_only_to_group_members()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var intruder = await factory.NewPersonAsync("Intruso");
        var group = await ana.CreateGroupAsync();
        var old = await ana.CreateSessionAsync(group.Id, "solo");
        await ana.PostOkAsync(old.SessionUrl("/cancel"));
        var open = await ana.CreateSessionAsync(group.Id, "relay");

        var list = await ana.ListSessionsAsync(group.Id);
        var outsider = await intruder.GetAsync($"/api/v1/groups/{group.Id}/sessions");

        Assert.Equal([open.Id, old.Id], list.Select(s => s.Id));
        Assert.Equal([SessionStatus.Waiting, SessionStatus.Cancelled], list.Select(s => s.Status));
        Assert.Equal(1, list[0].PlayerCount);
        Assert.Equal("relay", list[0].GameId);
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
    }

    [Fact]
    public async Task Only_members_of_the_group_see_a_session_and_outsiders_get_404_on_every_route()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var intruder = await factory.NewPersonAsync("Intruso");
        var group = await ana.CreateGroupAsync();
        var session = await ana.CreateSessionAsync(group.Id);
        var someone = Guid.NewGuid();

        var attempts = new[]
        {
            await intruder.GetAsync(session.SessionUrl()),
            await intruder.PostAsync(session.SessionUrl("/join")),
            await intruder.PostAsync(session.SessionUrl("/leave")),
            await intruder.PostAsync(session.SessionUrl("/players"), new { memberId = someone }),
            await intruder.DeleteAsync(session.SessionUrl($"/players/{someone}")),
            await intruder.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = someone, team = 0 } } }),
            await intruder.PostAsync(session.SessionUrl("/teams/shuffle")),
            await intruder.PatchAsync(session.SessionUrl("/config"), new { config = new { turns = 3 } }),
            await intruder.PostAsync(session.SessionUrl("/start")),
            await intruder.PostAsync(session.SessionUrl("/cancel")),
            await intruder.PostAsync(session.SessionUrl("/finish")),
            await intruder.ActAsync(session.Id, "pass"),
            await intruder.GetAsync(session.SessionUrl("/events")),
            await intruder.PostAsync(session.SessionUrl("/rematch")),
            await intruder.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}"),
        };

        Assert.All(attempts, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(SessionStatus.Waiting, (await ana.GetSessionAsync(session.Id)).Status);
    }

    [Fact]
    public async Task Group_members_join_leave_and_rejoin_the_lobby()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var session = await ana.CreateSessionAsync(group.Id);

        var joined = await beto.PostOkAsync(session.SessionUrl("/join"));
        var again = await beto.PostOkAsync(session.SessionUrl("/join")); // idempotente

        Assert.Equal(["Ana", "Beto"], joined.Players.Select(p => p.DisplayName));
        Assert.Equal(joined.Players.Count, again.Players.Count);
        Assert.False(joined.CanManage);

        var left = await beto.PostOkAsync(session.SessionUrl("/leave"));
        Assert.Equal(["Ana"], left.Players.Select(p => p.DisplayName));
        Assert.Null(left.MyPlayerId);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.PostAsync(session.SessionUrl("/leave"))).StatusCode);

        var back = await beto.PostOkAsync(session.SessionUrl("/join"));
        Assert.Equal(["Ana", "Beto"], back.Players.Select(p => p.DisplayName));
        Assert.Equal(2, back.Players.Single(p => p.DisplayName == "Beto").Seat); // volta no fim da fila: os assentos não são reaproveitados
    }

    [Fact]
    public async Task The_lobby_changes_bump_the_version()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var session = await ana.CreateSessionAsync(group.Id);

        var joined = await beto.PostOkAsync(session.SessionUrl("/join"));

        Assert.True(joined.Version > session.Version);
    }

    [Fact]
    public async Task The_game_limits_the_number_of_players()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var people = new List<Person>();
        for (var i = 0; i < 4; i++)
        {
            people.Add(await factory.NewPersonAsync($"Pessoa {i}"));
            await people[i].JoinAsync(group.InviteCode!);
        }

        var session = await ana.CreateSessionAsync(group.Id, "solo"); // máximo de 4
        foreach (var person in people.Take(3))
        {
            await person.PostOkAsync(session.SessionUrl("/join"));
        }

        var full = await people[3].PostAsync(session.SessionUrl("/join"));

        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("session.full", await full.ReadCodeAsync());
        await people[0].PostOkAsync(session.SessionUrl("/leave")); // quem sai libera a vaga
        await people[3].PostOkAsync(session.SessionUrl("/join"));
    }

    [Fact]
    public async Task The_host_adds_group_members_including_profiles_without_an_account_and_removes_players()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa", "preset-3");
        var session = await ana.CreateSessionAsync(group.Id);

        var withProfile = await ana.PostOkAsync(session.SessionUrl("/players"), new { memberId = vovo.Id });
        var addedBeto = await ana.PostOkAsync(session.SessionUrl("/players"), new { memberId = group.MemberOf(beto).Id });

        var profilePlayer = addedBeto.Players.Single(p => p.MemberId == vovo.Id);
        Assert.False(profilePlayer.HasAccount);
        Assert.Equal("Vovó Rosa", profilePlayer.DisplayName);
        Assert.Equal("preset-3", profilePlayer.Avatar.Preset);
        Assert.Equal(3, addedBeto.Players.Count);
        Assert.Equal(2, withProfile.Players.Count);

        var removed = await ana.DeleteAsync(session.SessionUrl($"/players/{profilePlayer.Id}"));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(["Ana", "Beto"], (await removed.ReadAsAsync<GameSessionDto>()).Players.Select(p => p.DisplayName));
        Assert.Equal(HttpStatusCode.NotFound, (await ana.DeleteAsync(session.SessionUrl($"/players/{profilePlayer.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Only_active_members_of_the_same_group_can_be_added()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var outsider = await factory.NewPersonAsync("De fora");
        var group = await ana.GroupWithAsync(beto);
        var other = await outsider.CreateGroupAsync("Outro");
        var session = await ana.CreateSessionAsync(group.Id);
        var betoId = group.MemberOf(beto).Id;
        await ana.DeleteAsync(group.Url($"/members/{betoId}")); // removido do grupo

        var foreign = await ana.PostAsync(session.SessionUrl("/players"), new { memberId = other.MyMemberId });
        var removedMember = await ana.PostAsync(session.SessionUrl("/players"), new { memberId = betoId });
        var unknown = await ana.PostAsync(session.SessionUrl("/players"), new { memberId = Guid.NewGuid() });
        var missing = await ana.PostAsync(session.SessionUrl("/players"), new { });

        Assert.All(new[] { foreign, removedMember, unknown }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal("member.not_found", await foreign.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Plain_members_cannot_manage_the_lobby_but_group_admins_can()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);
        await ana.PatchAsync(group.Url($"/members/{group.MemberOf(carla).Id}"), new { role = "admin" });
        var session = await ana.CreateSessionAsync(group.Id);
        await beto.PostOkAsync(session.SessionUrl("/join"));
        var betoPlayer = (await ana.GetSessionAsync(session.Id)).Players.Single(p => p.DisplayName == "Beto");

        var attempts = new[]
        {
            await beto.PostAsync(session.SessionUrl("/players"), new { memberId = group.MemberOf(carla).Id }),
            await beto.DeleteAsync(session.SessionUrl($"/players/{betoPlayer.Id}")),
            await beto.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = betoPlayer.Id, team = 1 } } }),
            await beto.PostAsync(session.SessionUrl("/teams/shuffle")),
            await beto.PatchAsync(session.SessionUrl("/config"), new { config = new { turns = 3 } }),
            await beto.PostAsync(session.SessionUrl("/start")),
            await beto.PostAsync(session.SessionUrl("/cancel")),
            await beto.PostAsync(session.SessionUrl("/finish")),
            await beto.PostAsync(session.SessionUrl("/rematch")),
        };

        Assert.All(attempts, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal("session.forbidden", await attempts[0].ReadCodeAsync());

        // Uma administradora do grupo gerencia mesmo sem ser a anfitriã.
        var asAdmin = await carla.GetSessionAsync(session.Id);
        Assert.True(asAdmin.CanManage);
        Assert.Equal(HttpStatusCode.OK, (await carla.PatchAsync(session.SessionUrl("/config"), new { config = new { turns = 3 } })).StatusCode);
    }

    [Fact]
    public async Task Teams_are_assigned_and_validated_in_games_that_have_them()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var session = await ana.CreateSessionAsync(group.Id);
        await beto.PostOkAsync(session.SessionUrl("/join"));
        var players = (await ana.GetSessionAsync(session.Id)).Players;
        var anaId = players.Single(p => p.DisplayName == "Ana").Id;
        var betoId = players.Single(p => p.DisplayName == "Beto").Id;

        var ok = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = anaId, team = 1 }, new { playerId = betoId, team = 0 } } });
        var tooHigh = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = anaId, team = 2 } } });
        var negative = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = anaId, team = -1 } } });
        var duplicate = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = anaId, team = 0 }, new { playerId = anaId, team = 1 } } });
        var unknown = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = Guid.NewGuid(), team = 0 } } });
        var empty = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = Array.Empty<object>() });

        var seen = (await ok.ReadAsAsync<GameSessionDto>()).Players;
        Assert.Equal(1, seen.Single(p => p.Id == anaId).Team);
        Assert.Equal(0, seen.Single(p => p.Id == betoId).Team);
        Assert.Equal("session.invalid_team", await tooHigh.ReadCodeAsync());
        Assert.Equal("session.invalid_team", await negative.ReadCodeAsync());
        Assert.Equal("session.invalid_team", await duplicate.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(1, (await ana.GetSessionAsync(session.Id)).Players.Single(p => p.Id == anaId).Team); // nada mudou nos pedidos inválidos
    }

    [Fact]
    public async Task Shuffling_splits_the_players_evenly_between_the_teams()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var session = await ana.CreateSessionAsync(group.Id);
        foreach (var name in new[] { "Beto", "Carla", "Davi", "Eva", "Fábio" })
        {
            var person = await factory.NewPersonAsync(name);
            await person.JoinAsync(group.InviteCode!);
            await person.PostOkAsync(session.SessionUrl("/join"));
        }

        var shuffled = await ana.PostOkAsync(session.SessionUrl("/teams/shuffle"));

        Assert.Equal(6, shuffled.Players.Count);
        Assert.All(shuffled.Players, p => Assert.NotNull(p.Team));
        Assert.Equal(3, shuffled.Players.Count(p => p.Team == 0));
        Assert.Equal(3, shuffled.Players.Count(p => p.Team == 1));
    }

    [Fact]
    public async Task Games_without_teams_refuse_team_operations()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var session = await ana.CreateSessionAsync(group.Id, "solo");

        var shuffle = await ana.PostAsync(session.SessionUrl("/teams/shuffle"));
        var assign = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = new[] { new { playerId = session.MyPlayerId, team = 0 } } });

        Assert.Equal(HttpStatusCode.Conflict, shuffle.StatusCode);
        Assert.Equal("session.no_teams", await shuffle.ReadCodeAsync());
        Assert.Equal("session.no_teams", await assign.ReadCodeAsync());
        Assert.Empty(session.TeamScores);
    }

    [Fact]
    public async Task The_config_changes_only_in_the_lobby_and_is_validated()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var session = await ana.CreateSessionAsync(group.Id);

        var ok = await ana.PatchAsync(session.SessionUrl("/config"), new { config = new { turns = 9, turnSeconds = 30 } });
        var invalid = await ana.PatchAsync(session.SessionUrl("/config"), new { config = new { turns = 100 } });
        var missing = await ana.PatchAsync(session.SessionUrl("/config"), new { });

        var updated = await ok.ReadAsAsync<GameSessionDto>();
        Assert.Equal(9, updated.Config.GetProperty("turns").GetInt32());
        Assert.Equal(30, updated.Config.GetProperty("turnSeconds").GetInt32());
        Assert.Equal("session.invalid_config", await invalid.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        await ana.PostOkAsync(session.SessionUrl("/cancel"));
        var late = await ana.PatchAsync(session.SessionUrl("/config"), new { config = new { turns = 3 } });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("session.not_waiting", await late.ReadCodeAsync());
    }

    [Fact]
    public async Task Cancelling_ends_the_session_for_good()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var session = await ana.CreateSessionAsync(group.Id);

        var cancelled = await ana.PostOkAsync(session.SessionUrl("/cancel"));
        var again = await ana.PostAsync(session.SessionUrl("/cancel"));
        var join = await ana.PostAsync(session.SessionUrl("/join"));

        Assert.Equal(SessionStatus.Cancelled, cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("session.already_ended", await again.ReadCodeAsync());
        Assert.Equal("session.not_waiting", await join.ReadCodeAsync());
    }

    [Fact]
    public async Task A_rematch_copies_game_config_players_and_teams_into_a_new_lobby()
    {
        var table = await factory.NewRelayTableAsync(new { turns = 3, turnSeconds = 45 });
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/cancel"));

        var response = await table.Ana.PostAsync(table.Session.SessionUrl("/rematch"));
        var rematch = await response.ReadAsAsync<GameSessionDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(table.Id, rematch.Id);
        Assert.Equal(SessionStatus.Waiting, rematch.Status);
        Assert.Equal(3, rematch.Config.GetProperty("turns").GetInt32());
        Assert.Equal(45, rematch.Config.GetProperty("turnSeconds").GetInt32());
        Assert.Equal(["Ana", "Beto", "Carla", "Davi"], rematch.Players.Select(p => p.DisplayName));
        Assert.Equal([0, 1, 0, 1], rematch.Players.Select(p => p.Team));

        var origin = await factory.WithDbAsync(db => db.GameSessions.AsNoTracking().Where(s => s.Id == rematch.Id).Select(s => s.RematchOfId).SingleAsync());
        Assert.Equal(table.Id, origin);
    }

    [Fact]
    public async Task A_rematch_only_after_the_session_has_ended_and_only_for_managers()
    {
        var table = await factory.NewRelayTableAsync();

        var early = await table.Ana.PostAsync(table.Session.SessionUrl("/rematch"));
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/cancel"));
        var byMember = await table.Beto.PostAsync(table.Session.SessionUrl("/rematch"));

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("session.not_ended", await early.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
    }

    [Fact]
    public async Task A_rematch_leaves_out_players_who_are_no_longer_in_the_group()
    {
        var table = await factory.NewRelayTableAsync();
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/cancel"));
        await table.Ana.DeleteAsync(table.Group.Url($"/members/{table.Group.MemberOf(table.Davi).Id}"));

        var rematch = await (await table.Ana.PostAsync(table.Session.SessionUrl("/rematch"))).ReadAsAsync<GameSessionDto>();

        Assert.Equal(["Ana", "Beto", "Carla"], rematch.Players.Select(p => p.DisplayName));
    }

    [Fact]
    public async Task Two_hosts_changing_the_lobby_at_once_never_corrupt_it()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var people = new List<Person>();
        foreach (var name in new[] { "Beto", "Carla", "Davi" })
        {
            var person = await factory.NewPersonAsync(name);
            await person.JoinAsync(group.InviteCode!);
            people.Add(person);
        }

        var session = await ana.CreateSessionAsync(group.Id);
        var responses = await Task.WhenAll(people.Select(p => p.PostAsync(session.SessionUrl("/join"))));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("session.concurrent_update", await conflict.ReadCodeAsync());
        }

        var seats = await factory.WithDbAsync(db => db.SessionPlayers.AsNoTracking().Where(p => p.SessionId == session.Id).Select(p => p.Seat).ToListAsync());
        Assert.Equal(seats.Distinct().Count(), seats.Count); // nenhum assento repetido
        Assert.Equal(1 + responses.Count(r => r.StatusCode == HttpStatusCode.OK), seats.Count);
    }
}

internal static class SessionListHelpers
{
    public static async Task<IReadOnlyList<GameSessionSummaryDto>> ListSessionsAsync(this Person person, Guid groupId)
    {
        var response = await person.GetAsync($"/api/v1/groups/{groupId}/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<List<GameSessionSummaryDto>>();
    }
}
