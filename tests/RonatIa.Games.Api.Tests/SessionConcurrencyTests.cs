using System.Net;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Sessions;

namespace RonatIa.Games.Api.Tests;

/// <summary>Versão (concorrência otimista) e idempotência quando várias ações chegam ao mesmo tempo.</summary>
public sealed class SessionConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(Person Host, IReadOnlyList<Person> Others, GameSessionDto Session)> SoloTableAsync(int others = 3)
    {
        var host = await factory.NewPersonAsync("Ana");
        var group = await host.CreateGroupAsync();
        var people = new List<Person>();
        for (var i = 0; i < others; i++)
        {
            var person = await factory.NewPersonAsync($"Pessoa {i + 1}");
            await person.JoinAsync(group.InviteCode!);
            people.Add(person);
        }

        var session = await host.CreateSessionAsync(group.Id, "solo", new { target = 50 });
        foreach (var person in people)
        {
            await person.PostOkAsync(session.SessionUrl("/join"));
        }

        await host.PostOkAsync(session.SessionUrl("/start"));
        return (host, people, session);
    }

    [Fact]
    public async Task Several_players_acting_at_once_all_apply_in_a_single_ordered_history()
    {
        var (host, others, session) = await SoloTableAsync();
        var everyone = new[] { host }.Concat(others).ToList();
        var versionBefore = (await host.GetSessionAsync(session.Id)).Version;

        var responses = await Task.WhenAll(everyone.Select(p => p.ActAsync(session.Id, "add", new { n = 1 })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode)); // cada conflito é reavaliado sobre o estado novo
        var final = await host.GetSessionAsync(session.Id);
        Assert.Equal(everyone.Count, final.Players.Sum(p => p.Score));       // nenhum ponto perdido nem duplicado
        Assert.All(final.Players, p => Assert.Equal(1, p.Score));
        Assert.Equal(versionBefore + everyone.Count, final.Version);          // uma versão por ação, sem saltos

        var events = await host.EventsAsync(session.Id);
        Assert.Equal(Enumerable.Range(1, events.Count), events.Select(e => e.Seq)); // sequência contínua, sem buracos nem repetições
        Assert.Equal(everyone.Count, events.Count(e => e.Type == "solo.added"));
    }

    [Fact]
    public async Task The_same_client_action_id_sent_twice_at_once_is_applied_once()
    {
        var (host, others, session) = await SoloTableAsync(1);
        var id = Guid.NewGuid();

        var responses = await Task.WhenAll(host.ActAsync(session.Id, "add", new { n = 2 }, id), host.ActAsync(session.Id, "add", new { n = 2 }, id));
        var bodies = await Task.WhenAll(responses.Select(r => r.ReadAsAsync<ActionResponse>()));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(bodies, b => !b.Replayed);
        Assert.Single(bodies, b => b.Replayed);
        Assert.Equal(2, (await host.GetSessionAsync(session.Id)).Players.Single(p => p.IsMe).Score); // 2 pontos, não 4
        Assert.Equal(1, await factory.WithDbAsync(db => db.GameEvents.CountAsync(e => e.SessionId == session.Id && e.ClientActionId == id)));
        Assert.NotEmpty(others);
    }

    [Fact]
    public async Task Two_players_answering_the_same_turn_at_once_cannot_both_score()
    {
        var table = await factory.NewRelayTableAsync(new { turns = 6 }, start: true);
        var word = table.Session.WordOf()!;

        // Beto e Davi (mesmo time) acertam a mesma palavra ao mesmo tempo: o primeiro marca e passa a vez; o segundo
        // é reavaliado sobre o estado novo. Se quem perdeu foi o Davi, a palavra já é outra e o palpite vira "errado" (200);
        // se foi o Beto, ele agora é o mímico da vez e não pode adivinhar (403). Nos dois casos, só um ponto.
        var responses = await Task.WhenAll(
            table.Beto.ActAsync(table.Id, "guess", new { text = word }),
            table.Davi.ActAsync(table.Id, "guess", new { text = word }));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden }));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        var final = await table.Ana.GetSessionAsync(table.Id);
        Assert.Equal(1, final.Players.Sum(p => p.Score));
        Assert.Equal(1, final.TeamScores.Sum(t => t.Score));
        Assert.Equal(1, final.ViewOf().GetProperty("turn").GetInt32());

        var scoring = await factory.WithDbAsync(db => db.ScoreEntries.CountAsync(e => e.SessionId == table.Id));
        Assert.Equal(1, scoring);
    }

    [Fact]
    public async Task Finishing_and_acting_at_the_same_time_leaves_a_consistent_session()
    {
        var table = await factory.NewRelayTableAsync(new { turns = 6 }, start: true);

        var responses = await Task.WhenAll(
            table.Ana.PostAsync(table.Session.SessionUrl("/finish")),
            table.Beto.ActAsync(table.Id, "guess", new { text = "errado" }));

        var final = await table.Ana.GetSessionAsync(table.Id);
        Assert.Equal(RonatIa.Games.Domain.Sessions.SessionStatus.Finished, final.Status);
        Assert.Contains(HttpStatusCode.OK, responses.Select(r => r.StatusCode)); // quem chegou primeiro valeu
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));

        var results = await factory.WithDbAsync(db => db.SessionResults.CountAsync(r => r.SessionId == table.Id));
        Assert.Equal(4, results); // um único resultado por jogador, mesmo com a corrida
        var finishedEvents = (await table.Ana.EventsAsync(table.Id)).Count(e => e.Type == "session.finished");
        Assert.Equal(1, finishedEvents);
    }
}
