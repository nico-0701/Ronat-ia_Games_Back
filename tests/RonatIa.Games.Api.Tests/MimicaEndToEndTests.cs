using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Application.Sessions;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Api.Tests;

/// <summary>A Mímica de verdade (instalada na API) jogada de ponta a ponta pelos endpoints REST.</summary>
public sealed class MimicaEndToEndTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string? CardText(GameSessionDto session) =>
        session.View is { } view && view.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object
            ? card.GetProperty("text").GetString()
            : null;

    [Fact]
    public async Task The_catalog_offers_mimica_with_two_teams_and_the_default_config()
    {
        var ana = await factory.NewPersonAsync("Ana");

        var games = await (await ana.GetAsync("/api/v1/games")).ReadAsAsync<List<GameDto>>();
        var mimica = games.Single(g => g.Id == "mimica");

        Assert.Equal("Mímica", mimica.Name);
        Assert.Equal((2, 24, 2, 1), (mimica.MinPlayers, mimica.MaxPlayers, mimica.TeamCount, mimica.MinPlayersPerTeam));
        Assert.Equal(10, mimica.ConfigDefaults.GetProperty("rounds").GetInt32());
        Assert.Equal(3, mimica.ConfigDefaults.GetProperty("categories").GetArrayLength());
    }

    [Fact]
    public async Task An_invalid_mimica_config_is_refused_with_the_field_errors()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var response = await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "mimica", config = new { rounds = 99, categories = new[] { "xadrez" } } });
        var problem = await response.ReadProblemAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("session.invalid_config", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("rounds", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("categories", out _));
    }

    [Fact]
    public async Task A_full_game_with_a_steal_a_skip_and_a_performer_without_an_account()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var ana = await host.NewPersonAsync("Ana");
        var beto = await host.NewPersonAsync("Beto");
        var carla = await host.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa"); // sem conta: não tem celular

        // Lobby: Ana (anfitriã), Beto, Carla e a Vovó. Times: Ana e Carla no 0; Beto e Vovó no 1.
        var session = await ana.CreateSessionAsync(group.Id, "mimica", new { rounds = 2, turnSeconds = 30, categories = new[] { "famosos" } });
        await beto.PostOkAsync(session.SessionUrl("/join"));
        await carla.PostOkAsync(session.SessionUrl("/join"));
        await ana.PostOkAsync(session.SessionUrl("/players"), new { memberId = vovo.Id });
        var lobby = await ana.GetSessionAsync(session.Id);
        var teams = new Dictionary<string, int> { ["Ana"] = 0, ["Beto"] = 1, ["Carla"] = 0, ["Vovó Rosa"] = 1 };
        await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = lobby.Players.Select(p => new { playerId = p.Id, team = teams[p.DisplayName] }).ToArray() });

        var started = await ana.PostOkAsync(session.SessionUrl("/start"));
        var anaPlayer = started.Players.Single(p => p.DisplayName == "Ana").Id;
        var cardTexts = new List<string>();

        // ---- turno 0: Ana (time 0) faz a mímica; errou; o time 1 rouba ----
        Assert.Equal("turnIntro", started.ViewOf().GetProperty("phase").GetString());
        Assert.Equal(anaPlayer, started.ViewOf().GetProperty("performerPlayerId").GetGuid());
        Assert.Equal(["startTurn", "skipTurn"], started.AllowedActions);       // anfitriã e na vez
        Assert.Empty((await carla.GetSessionAsync(session.Id)).AllowedActions);   // o colega de time não age
        Assert.Null(CardText(started));

        var prep = (await ana.ActOkAsync(session.Id, "startTurn")).Session;
        cardTexts.Add(CardText(prep)!);
        Assert.NotNull(CardText(prep));                                         // o mímico vê a carta já no preparo
        Assert.Equal("prep", prep.ViewOf().GetProperty("phase").GetString());
        Assert.DoesNotContain(prep.AllowedActions, a => a.StartsWith("report", StringComparison.Ordinal)); // sem veredito durante o preparo

        // Quem não é o mímico não vê a carta em nenhuma resposta (partida, trilha, listagem).
        foreach (var other in new[] { beto, carla })
        {
            var snapshot = await other.GetAsync(session.SessionUrl());
            Assert.DoesNotContain(cardTexts[0], await snapshot.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(cardTexts[0], await (await other.GetAsync(session.SessionUrl("/events"))).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        // O adversário e o colega de time não dão veredito.
        Assert.Equal("mimica.not_performer", await (await beto.ActAsync(session.Id, "reportHit")).ReadCodeAsync());
        Assert.Equal("mimica.not_performer", await (await carla.ActAsync(session.Id, "reportMiss")).ReadCodeAsync());
        Assert.Equal("mimica.not_playing", await (await ana.ActAsync(session.Id, "reportHit")).ReadCodeAsync()); // ainda no preparo

        time.Advance(TimeSpan.FromSeconds(3));
        var miss = (await ana.ActOkAsync(session.Id, "reportMiss")).Session;
        Assert.Equal("steal", miss.ViewOf().GetProperty("phase").GetString());
        Assert.Equal(["reportStealHit", "reportStealMiss", "skipTurn"], miss.AllowedActions);

        var afterSteal = (await ana.ActOkAsync(session.Id, "reportStealHit")).Session;
        Assert.Equal([0, 1], afterSteal.TeamScores.Select(t => t.Score));      // o adversário levou o ponto
        var summary = afterSteal.ViewOf().GetProperty("lastTurn");
        Assert.Equal("stealHit", summary.GetProperty("outcome").GetString());
        Assert.Equal(cardTexts[0], summary.GetProperty("card").GetProperty("text").GetString()); // "A mímica era…" revelada a todos

        var seenByBeto = await beto.GetSessionAsync(session.Id);
        Assert.Equal(cardTexts[0], seenByBeto.ViewOf().GetProperty("lastTurn").GetProperty("card").GetProperty("text").GetString());

        // ---- turnos 1 e 2: o anfitrião pula (sem pontos) ----
        Assert.Equal(["startTurn"], seenByBeto.AllowedActions);                // agora a vez é do Beto
        var skip1 = (await ana.ActOkAsync(session.Id, "skipTurn")).Session;
        Assert.Equal(2, skip1.ViewOf().GetProperty("turn").GetInt32());        // virou o turno 2 (Carla)
        Assert.Equal("mimica.host_only", await (await beto.ActAsync(session.Id, "skipTurn")).ReadCodeAsync());
        var skip2 = (await ana.ActOkAsync(session.Id, "skipTurn")).Session;

        // ---- turno 3: a Vovó (sem conta) é a mímica; a Ana, como anfitriã, vê a carta e dá o veredito por ela ----
        var vovoPlayer = skip2.Players.Single(p => p.DisplayName == "Vovó Rosa");
        Assert.False(vovoPlayer.HasAccount);
        Assert.Equal(vovoPlayer.Id, skip2.ViewOf().GetProperty("performerPlayerId").GetGuid());
        Assert.Contains("startTurn", skip2.AllowedActions);

        var vovoPrep = (await ana.ActOkAsync(session.Id, "startTurn")).Session;
        cardTexts.Add(CardText(vovoPrep)!);
        Assert.NotNull(CardText(vovoPrep));                                    // a anfitriã enxerga por ela
        Assert.Null(CardText(await beto.GetSessionAsync(session.Id)));         // o colega de time da Vovó não
        Assert.Null(CardText(await carla.GetSessionAsync(session.Id)));

        time.Advance(TimeSpan.FromSeconds(3));
        var last = await ana.ActOkAsync(session.Id, "reportHit");              // o time 1 acerta de novo e a partida termina

        // ---- fim ----
        var final = last.Session;
        Assert.Equal(SessionStatus.Finished, final.Status);
        Assert.Equal([0, 2], final.TeamScores.Select(t => t.Score));
        Assert.Equal(["Beto", "Vovó Rosa"], final.Standings.Where(s => s.IsWinner).Select(s => s.DisplayName).Order());
        Assert.All(final.Standings.Where(s => !s.IsWinner), s => Assert.Equal((0, 2), (s.Score, s.Rank)));
        Assert.Equal(4, await factory.WithDbAsync(db => db.SessionResults.CountAsync(r => r.SessionId == session.Id)));
        Assert.Empty(final.AllowedActions);

        // A trilha toda: só fatos públicos, nenhum texto de carta.
        var events = await ana.EventsAsync(session.Id);
        var trail = JsonSerializer.Serialize(events, TestJson.Options);
        Assert.Equal(Enumerable.Range(1, events.Count), events.Select(e => e.Seq));
        Assert.Contains("mimica.turn_ended", trail);
        Assert.Equal("session.finished", events[^1].Type);
        foreach (var text in cardTexts)
        {
            Assert.DoesNotContain(text, trail, StringComparison.OrdinalIgnoreCase);
        }

        // Os pontos viraram livro-razão por time.
        var ledger = await factory.WithDbAsync(db => db.ScoreEntries.AsNoTracking().Where(e => e.SessionId == session.Id).OrderBy(e => e.Id).Select(e => new { e.TeamNo, e.Points, e.Reason }).ToListAsync());
        Assert.Equal([("steal_hit", 1), ("hit", 1)], ledger.Select(e => (e.Reason, e.TeamNo!.Value)));
    }
}
