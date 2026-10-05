using System.Net;
using Microsoft.Extensions.Time.Testing;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Application.Ranking;
using RonatIa.Games.Application.Sessions;

namespace RonatIa.Games.Api.Tests;

/// <summary>Ranking, histórico e estatísticas, calculados dos resultados das partidas encerradas.</summary>
public sealed class RankingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>Uma partida "solo" de meta 1 que termina no primeiro ponto: quem pontua vence; os demais ficam em segundo.</summary>
    private static async Task<GameSessionDto> PlaySoloAsync(Person host, GroupDetailDto group, Person winner, params Person[] others)
    {
        var session = await host.CreateSessionAsync(group.Id, "solo", new { target = 1 });
        foreach (var person in new[] { winner }.Concat(others).Where(p => p != host).Distinct())
        {
            await person.PostOkAsync(session.SessionUrl("/join"));
        }

        await host.PostOkAsync(session.SessionUrl("/start"));
        var result = await winner.ActOkAsync(session.Id, "add", new { n = 1 });
        Assert.Equal(RonatIa.Games.Domain.Sessions.SessionStatus.Finished, result.Session.Status);
        return result.Session;
    }

    private static async Task<RankingDto> RankingOfAsync(Person person, Guid groupId, string query = "")
    {
        var response = await person.GetAsync($"/api/v1/groups/{groupId}/ranking{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<RankingDto>();
    }

    private static async Task<HistoryPageDto> HistoryOfAsync(Person person, Guid groupId, string query = "")
    {
        var response = await person.GetAsync($"/api/v1/groups/{groupId}/history{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<HistoryPageDto>();
    }

    [Fact]
    public async Task The_ranking_orders_by_wins_then_win_rate_then_games_and_ties_share_the_rank()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);

        await PlaySoloAsync(ana, group, winner: ana, beto, carla); // Ana vence (A, B, C)
        await PlaySoloAsync(ana, group, winner: ana, beto);        // Ana vence (A, B)
        await PlaySoloAsync(beto, group, winner: beto, carla);     // Beto vence (B, C)
        await PlaySoloAsync(carla, group, winner: carla);          // Carla vence sozinha

        var ranking = await RankingOfAsync(beto, group.Id);

        Assert.Null(ranking.GameId);
        Assert.Equal(RankingPeriod.All, ranking.Period);
        Assert.Null(ranking.Since);
        Assert.Equal(["Ana", "Beto", "Carla"], ranking.Entries.Select(e => e.DisplayName));
        Assert.Equal([1, 2, 2], ranking.Entries.Select(e => e.Rank));            // Beto e Carla empatam em tudo
        Assert.Equal([2, 3, 3], ranking.Entries.Select(e => e.Played));
        Assert.Equal([2, 1, 1], ranking.Entries.Select(e => e.Wins));
        Assert.Equal([1.0, 0.3333, 0.3333], ranking.Entries.Select(e => e.WinRate));
        Assert.Equal([2, 1, 1], ranking.Entries.Select(e => e.Score));
        Assert.Equal(["Beto"], ranking.Entries.Where(e => e.IsMe).Select(e => e.DisplayName));
        Assert.All(ranking.Entries, e => Assert.True(e.HasAccount));
    }

    [Fact]
    public async Task A_better_win_rate_breaks_a_tie_in_wins()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await PlaySoloAsync(ana, group, winner: ana);                // Ana vence sozinha: 1 vitória em 1 partida
        await PlaySoloAsync(beto, group, winner: beto, ana);         // Beto vence e a Ana joga e perde: Ana 1 em 2; Beto 1 em 1

        var ranking = await RankingOfAsync(ana, group.Id);

        // Empate em vitórias (1 a 1): o aproveitamento desempata.
        Assert.Equal(["Beto", "Ana"], ranking.Entries.Select(e => e.DisplayName));
        Assert.Equal([1, 2], ranking.Entries.Select(e => e.Rank));
        Assert.Equal([1, 1], ranking.Entries.Select(e => e.Wins));
        Assert.Equal([1, 2], ranking.Entries.Select(e => e.Played));
        Assert.Equal([1.0, 0.5], ranking.Entries.Select(e => e.WinRate));
    }

    [Fact]
    public async Task Running_and_cancelled_sessions_do_not_count()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var running = await ana.CreateSessionAsync(group.Id, "solo", new { target = 9 });
        await ana.PostOkAsync(running.SessionUrl("/start"));
        var cancelled = await ana.CreateSessionAsync(group.Id, "solo");
        await ana.PostOkAsync(cancelled.SessionUrl("/cancel"));

        Assert.Empty((await RankingOfAsync(ana, group.Id)).Entries);
        Assert.Empty((await HistoryOfAsync(ana, group.Id)).Items);
    }

    [Fact]
    public async Task The_ranking_filters_by_game_and_by_time_window()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var ana = await host.NewPersonAsync("Ana");
        var beto = await host.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);

        await PlaySoloAsync(ana, group, winner: ana, beto);                  // antiga (Ana vence)
        time.Advance(TimeSpan.FromDays(20));
        ana = await ana.ReloginAsync(host);   // os tokens duram 30 min: depois de avançar o relógio, entra de novo
        beto = await beto.ReloginAsync(host);
        await PlaySoloAsync(beto, group, winner: beto, ana);                 // 20 dias atrás (Beto vence)
        var relay = await ana.CreateSessionAsync(group.Id, "relay");
        await beto.PostOkAsync(relay.SessionUrl("/join"));
        var lobby = await ana.GetSessionAsync(relay.Id);
        await ana.PutAsync(relay.SessionUrl("/teams"), new { assignments = lobby.Players.Select(p => new { playerId = p.Id, team = p.Seat % 2 }).ToArray() });
        await ana.PostOkAsync(relay.SessionUrl("/start"));
        await ana.PostOkAsync(relay.SessionUrl("/finish"));                  // 0 x 0: todos vencem
        time.Advance(TimeSpan.FromDays(20));                                  // agora: as de 40 e 20 dias atrás são antigas para "semana"
        ana = await ana.ReloginAsync(host);

        var all = await RankingOfAsync(ana, group.Id);
        var onlySolo = await RankingOfAsync(ana, group.Id, "?gameId=solo");
        var onlyRelay = await RankingOfAsync(ana, group.Id, "?gameId=relay");
        var week = await RankingOfAsync(ana, group.Id, "?period=week");
        var month = await RankingOfAsync(ana, group.Id, "?period=month");

        Assert.Equal(3, all.Entries.Single(e => e.DisplayName == "Ana").Played);
        Assert.Equal(2, onlySolo.Entries.Single(e => e.DisplayName == "Ana").Played);
        Assert.Equal("solo", onlySolo.GameId);
        Assert.Equal([1, 1], onlyRelay.Entries.Select(e => e.Wins)); // o empate do relay: os dois vencem
        Assert.Equal(["relay"], [onlyRelay.GameId!]);
        Assert.Empty(week.Entries);                                  // nada nos últimos 7 dias
        Assert.Equal(RankingPeriod.Month, month.Period);
        Assert.NotNull(month.Since);
        Assert.Equal(2, month.Entries.Single(e => e.DisplayName == "Beto").Played); // só a de 20 dias e o relay (a primeira tem 40 dias)
    }

    [Fact]
    public async Task An_unknown_period_or_game_is_handled_cleanly()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var badPeriod = await ana.GetAsync($"/api/v1/groups/{group.Id}/ranking?period=decada");
        var unknownGame = await RankingOfAsync(ana, group.Id, "?gameId=xadrez");

        Assert.Equal(HttpStatusCode.BadRequest, badPeriod.StatusCode);
        Assert.Empty(unknownGame.Entries);
    }

    [Fact]
    public async Task Profiles_without_an_account_appear_and_their_history_goes_to_whoever_claims_them()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        // A Vovó (sem celular) joga, com a Ana agindo por ela, e vence.
        var session = await ana.CreateSessionAsync(group.Id, "solo", new { target = 1 });
        await ana.PostOkAsync(session.SessionUrl("/players"), new { memberId = vovo.Id });
        await ana.PostOkAsync(session.SessionUrl("/start"));
        var vovoPlayer = (await ana.GetSessionAsync(session.Id)).Players.Single(p => !p.HasAccount).Id;
        Assert.NotEqual(Guid.Empty, vovoPlayer);

        // O jogo exige que quem pontua seja um jogador: a Ana encerra e o placar do momento decide (0 x 0 = todos vencem).
        await ana.PostOkAsync(session.SessionUrl("/finish"));

        var before = await RankingOfAsync(ana, group.Id);
        var profileEntry = before.Entries.Single(e => e.MemberId == vovo.Id);
        Assert.False(profileEntry.HasAccount);
        Assert.Equal(("Vovó Rosa", 1, 1), (profileEntry.DisplayName, profileEntry.Played, profileEntry.Wins));

        // A Carla entra assumindo o perfil: o histórico vai com ela, sob o nome dela.
        await carla.JoinAsync(group.InviteCode!, vovo.Id);
        var after = await RankingOfAsync(ana, group.Id);
        var claimed = after.Entries.Single(e => e.MemberId == vovo.Id);
        Assert.Equal(("Carla", true, 1, 1), (claimed.DisplayName, claimed.HasAccount, claimed.Played, claimed.Wins));
        var stats = await (await carla.GetAsync("/api/v1/users/me/stats")).ReadAsAsync<MyStatsDto>();
        Assert.Equal((1, 1, 1), (stats.Played, stats.Wins, stats.Groups));
    }

    [Fact]
    public async Task People_who_left_the_group_keep_appearing_with_the_results_they_earned()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await PlaySoloAsync(ana, group, winner: beto, ana);
        await beto.DeleteAsync(group.Url("/members/me"));

        var ranking = await RankingOfAsync(ana, group.Id);

        Assert.Equal(["Beto", "Ana"], ranking.Entries.Select(e => e.DisplayName));
        Assert.Equal(1, ranking.Entries[0].Wins);
        // e quem saiu já não enxerga o grupo
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync($"/api/v1/groups/{group.Id}/ranking")).StatusCode);
    }

    [Fact]
    public async Task The_history_lists_finished_games_newest_first_with_standings_and_pages_by_cursor()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var ana = await host.NewPersonAsync("Ana");
        var beto = await host.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var first = await PlaySoloAsync(ana, group, winner: ana, beto);
        time.Advance(TimeSpan.FromMinutes(5));
        var second = await PlaySoloAsync(ana, group, winner: beto, ana);
        time.Advance(TimeSpan.FromMinutes(5));
        var third = await PlaySoloAsync(ana, group, winner: ana);

        var page1 = await HistoryOfAsync(beto, group.Id, "?limit=2");
        var page2 = await HistoryOfAsync(beto, group.Id, $"?limit=2&before={Uri.EscapeDataString(page1.NextBefore!.Value.ToString("O"))}");

        Assert.Equal([third.Id, second.Id], page1.Items.Select(i => i.SessionId));
        Assert.NotNull(page1.NextBefore);
        Assert.Equal([first.Id], page2.Items.Select(i => i.SessionId));
        Assert.Null(page2.NextBefore);

        var entry = page1.Items[1]; // a segunda partida: Beto venceu, Ana ficou em segundo
        Assert.Equal("solo", entry.GameId);
        Assert.Equal(["Beto", "Ana"], entry.Standings.Select(s => s.DisplayName));
        Assert.Equal([1, 2], entry.Standings.Select(s => s.Rank));
        Assert.Equal([true, false], entry.Standings.Select(s => s.IsWinner));
        Assert.True(entry.FinishedAt >= entry.StartedAt);
    }

    [Fact]
    public async Task The_history_filters_by_game()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        await PlaySoloAsync(ana, group, winner: ana);

        Assert.Single((await HistoryOfAsync(ana, group.Id, "?gameId=solo")).Items);
        Assert.Empty((await HistoryOfAsync(ana, group.Id, "?gameId=mimica")).Items);
    }

    [Fact]
    public async Task Only_members_of_the_group_see_the_ranking_and_the_history()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var intruder = await factory.NewPersonAsync("Intruso");
        var group = await ana.CreateGroupAsync();
        await PlaySoloAsync(ana, group, winner: ana);

        var ranking = await intruder.GetAsync($"/api/v1/groups/{group.Id}/ranking");
        var history = await intruder.GetAsync($"/api/v1/groups/{group.Id}/history");
        var anonymous = await factory.CreateClient().GetAsync($"/api/v1/groups/{group.Id}/ranking");

        Assert.Equal(HttpStatusCode.NotFound, ranking.StatusCode);
        Assert.Equal("group.not_found", await ranking.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Personal_stats_add_up_every_group_by_game()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var first = await ana.GroupWithAsync(beto);
        var second = await ana.CreateGroupAsync("Segundo grupo");
        await PlaySoloAsync(ana, first, winner: ana, beto);
        await PlaySoloAsync(ana, first, winner: beto, ana);
        await PlaySoloAsync(ana, second, winner: ana);

        var stats = await (await ana.GetAsync("/api/v1/users/me/stats")).ReadAsAsync<MyStatsDto>();
        var betoStats = await (await beto.GetAsync("/api/v1/users/me/stats")).ReadAsAsync<MyStatsDto>();

        Assert.Equal((3, 2, 2), (stats.Played, stats.Wins, stats.Groups));
        var solo = Assert.Single(stats.ByGame);
        Assert.Equal(("solo", 3, 2, 2), (solo.GameId, solo.Played, solo.Wins, solo.Score));
        Assert.Equal((2, 1, 1), (betoStats.Played, betoStats.Wins, betoStats.Groups));
    }

    [Fact]
    public async Task A_person_without_games_has_empty_stats()
    {
        var ana = await factory.NewPersonAsync("Ana");

        var stats = await (await ana.GetAsync("/api/v1/users/me/stats")).ReadAsAsync<MyStatsDto>();

        Assert.Equal((0, 0, 0), (stats.Played, stats.Wins, stats.Groups));
        Assert.Empty(stats.ByGame);
    }
}
