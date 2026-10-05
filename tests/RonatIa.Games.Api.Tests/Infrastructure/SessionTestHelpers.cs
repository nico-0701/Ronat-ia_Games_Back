using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Application.Sessions;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>Um grupo de teste com quatro pessoas, uma partida "relay" criada e (opcionalmente) já iniciada.</summary>
public sealed class RelayTable
{
    public required Person Ana { get; init; }

    public required Person Beto { get; init; }

    public required Person Carla { get; init; }

    public required Person Davi { get; init; }

    public required GroupDetailDto Group { get; init; }

    public required GameSessionDto Session { get; set; }

    public IReadOnlyList<Person> People => [Ana, Beto, Carla, Davi];

    public Guid Id => Session.Id;

    /// <summary>A pessoa de quem é o jogador <paramref name="playerId"/> da partida.</summary>
    public Person PersonOf(Guid playerId)
    {
        var player = Session.Players.Single(p => p.Id == playerId);
        return People.Single(person => person.Name == player.DisplayName);
    }

    public Guid PlayerIdOf(Person person) => Session.Players.Single(p => p.DisplayName == person.Name).Id;

    /// <summary>Atualiza o instantâneo (visto por <paramref name="viewer"/>) e devolve-o.</summary>
    public async Task<GameSessionDto> RefreshAsync(Person? viewer = null)
    {
        Session = await (viewer ?? Ana).GetSessionAsync(Id);
        return Session;
    }
}

public static class SessionTestHelpers
{
    public static async Task<GameSessionDto> CreateSessionAsync(this Person host, Guid groupId, string gameId = RelayGame.Id, object? config = null)
    {
        var response = await host.PostAsync("/api/v1/sessions", new { groupId, gameId, config });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsAsync<GameSessionDto>();
    }

    public static async Task<GameSessionDto> GetSessionAsync(this Person person, Guid sessionId)
    {
        var response = await person.GetAsync($"/api/v1/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<GameSessionDto>();
    }

    public static string SessionUrl(this GameSessionDto session, string suffix = "") => $"/api/v1/sessions/{session.Id}{suffix}";

    public static Task<HttpResponseMessage> ActAsync(this Person person, Guid sessionId, string type, object? payload = null, Guid? clientActionId = null) =>
        person.PostAsync($"/api/v1/sessions/{sessionId}/actions", new { clientActionId = clientActionId ?? Guid.NewGuid(), type, payload });

    public static async Task<ActionResponse> ActOkAsync(this Person person, Guid sessionId, string type, object? payload = null, Guid? clientActionId = null)
    {
        var response = await person.ActAsync(sessionId, type, payload, clientActionId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<ActionResponse>();
    }

    public static async Task<GameSessionDto> PostOkAsync(this Person person, string url, object? body = null)
    {
        var response = await person.PostAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<GameSessionDto>();
    }

    public static async Task<IReadOnlyList<EventDto>> EventsAsync(this Person person, Guid sessionId, int after = 0, int limit = 200)
    {
        var response = await person.GetAsync($"/api/v1/sessions/{sessionId}/events?after={after}&limit={limit}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<List<EventDto>>();
    }

    /// <summary>Quatro pessoas no mesmo grupo (Ana é a dona e anfitriã) e uma partida de relay no lobby, com todos dentro e os times 0,1,0,1.</summary>
    public static async Task<RelayTable> NewRelayTableAsync(this WebApplicationFactory<Program> factory, object? config = null, bool start = false)
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var davi = await factory.NewPersonAsync("Davi");
        var group = await ana.GroupWithAsync(beto, carla, davi);

        var session = await ana.CreateSessionAsync(group.Id, RelayGame.Id, config);
        foreach (var person in new[] { beto, carla, davi })
        {
            await person.PostOkAsync(session.SessionUrl("/join"));
        }

        session = await ana.GetSessionAsync(session.Id);
        var teams = session.Players.Select(p => new { playerId = p.Id, team = p.Seat % 2 }).ToArray();
        var assigned = await ana.PutAsync(session.SessionUrl("/teams"), new { assignments = teams });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var table = new RelayTable { Ana = ana, Beto = beto, Carla = carla, Davi = davi, Group = group, Session = await ana.GetSessionAsync(session.Id) };
        if (start)
        {
            await ana.PostOkAsync(table.Session.SessionUrl("/start"));
            await table.RefreshAsync();
        }

        return table;
    }

    public static async Task<HttpResponseMessage> PutAsync(this Person person, string url, object body) =>
        await person.Client.PutAsync(url, System.Net.Http.Json.JsonContent.Create(body));

    /// <summary>A visão do jogo, como objeto JSON, para conferir campos do jogo.</summary>
    public static JsonElement ViewOf(this GameSessionDto session) =>
        session.View ?? throw new InvalidOperationException("A partida ainda não tem visão (não começou).");

    /// <summary>A palavra secreta, lida da visão de quem está na vez (só ele a vê).</summary>
    public static string? WordOf(this GameSessionDto session) =>
        session.ViewOf().TryGetProperty("word", out var word) && word.ValueKind == JsonValueKind.String ? word.GetString() : null;
}
