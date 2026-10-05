using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Sessions;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Api.Tests;

/// <summary>O tempo real: o hub entrega a cada assinante a visão própria dele e nada mais.</summary>
public sealed class RealtimeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string CardOf(GameSessionDto session) => session.View!.Value.GetProperty("card").GetProperty("text").GetString()!;

    [Fact]
    public async Task The_hub_refuses_anonymous_and_forged_connections()
    {
        var anonymous = LiveConnection.Build(factory, accessToken: null);
        var forged = LiveConnection.Build(factory, TokenForgery.Create(Convert.ToBase64String(new byte[64]), Guid.NewGuid(), Guid.NewGuid()));

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => anonymous.StartAsync());
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => forged.StartAsync());
    }

    [Fact]
    public async Task A_token_in_the_query_string_is_accepted_only_for_the_hub()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var token = ana.Auth.AccessToken;
        var anonymous = factory.CreateClient();

        var rest = await anonymous.GetAsync($"/api/v1/users/me?access_token={token}");
        var negotiate = await anonymous.PostAsync($"/hubs/sessions/negotiate?negotiateVersion=1&access_token={token}", null);
        var negotiateWithoutToken = await anonymous.PostAsync("/hubs/sessions/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, negotiateWithoutToken.StatusCode);
    }

    [Fact]
    public async Task The_connection_is_closed_by_the_server_when_the_access_token_expires()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var original = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().ReadJsonWebToken(ana.Auth.AccessToken);
        var shortLived = TokenForgery.Create(
            factory.JwtSigningKey,
            Guid.Parse(original.GetClaim("sub").Value),
            Guid.Parse(original.GetClaim("sid").Value),
            lifetime: TimeSpan.FromSeconds(2));

        var connection = LiveConnection.Build(factory, shortLived);
        var closed = new TaskCompletionSource();
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        await connection.StartAsync();
        var first = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.Same(closed.Task, first); // o servidor encerrou sozinho; o cliente reconecta com o token renovado
    }

    [Fact]
    public async Task A_revoked_login_cannot_open_the_hub()
    {
        var ana = await factory.NewPersonAsync("Ana");
        await ana.PostAsync("/api/v1/auth/logout");

        var connection = LiveConnection.Build(factory, ana.Auth.AccessToken);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Subscribing_returns_the_current_view_of_the_subscriber_and_who_is_online()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await using var ana = await LiveConnection.ConnectAsync(factory, table.Ana);
        await using var beto = await LiveConnection.ConnectAsync(factory, table.Beto);

        var first = await ana.SubscribeAsync(table.Id);
        var second = await beto.SubscribeAsync(table.Id);

        Assert.Equal(table.Id, first.Session.Id);
        Assert.Equal(table.Session.Version, first.Session.Version);
        Assert.Equal(table.Session.Players.Single(p => p.DisplayName == "Ana").MemberId, first.Session.MyMemberId);
        Assert.NotNull(first.Session.WordOf());           // a Ana é a mímica: vê a palavra
        Assert.Null(second.Session.WordOf());             // o Beto não
        Assert.Equal(["guess"], second.Session.AllowedActions);
        Assert.Equal([first.Session.MyMemberId], first.OnlineMemberIds);
        Assert.Equal(2, second.OnlineMemberIds.Count);
        Assert.Contains(second.Session.MyMemberId, second.OnlineMemberIds);
    }

    [Fact]
    public async Task Only_members_of_the_group_can_subscribe_and_the_error_matches_the_rest()
    {
        var table = await factory.NewRelayTableAsync();
        var intruder = await factory.NewPersonAsync("Intruso");
        await using var live = await LiveConnection.ConnectAsync(factory, intruder);

        var denied = await Assert.ThrowsAsync<HubException>(() => live.SubscribeAsync(table.Id));
        var unknown = await Assert.ThrowsAsync<HubException>(() => live.SubscribeAsync(Guid.NewGuid()));
        var groupDenied = await Assert.ThrowsAsync<HubException>(() => live.SubscribeGroupAsync(table.Group.Id));

        Assert.StartsWith("session.not_found:", denied.Message.Split("HubException: ").Last());
        Assert.Equal(denied.Message, unknown.Message); // não revela se a partida existe
        Assert.Contains("group.not_found:", groupDenied.Message);
    }

    [Fact]
    public async Task Every_subscriber_gets_only_their_own_view_after_a_change_and_the_secret_never_leaks()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var eva = await factory.NewPersonAsync("Eva");
        await eva.JoinAsync(table.Group.InviteCode!); // do grupo, mas só assiste

        await using var ana = await LiveConnection.ConnectAsync(factory, table.Ana);
        await using var beto = await LiveConnection.ConnectAsync(factory, table.Beto);
        await using var carla = await LiveConnection.ConnectAsync(factory, table.Carla);
        await using var spectator = await LiveConnection.ConnectAsync(factory, eva);
        foreach (var live in new[] { ana, beto, carla, spectator })
        {
            await live.SubscribeAsync(table.Id);
        }

        // A Ana (mímica da vez) "vê a palavra" por uma ação normal pelo REST; o tempo real entrega a cada um a sua visão.
        await table.Beto.ActOkAsync(table.Id, "guess", new { text = "errado" });

        var forAna = (await ana.NextAsync("SessionUpdated")).As<GameSessionDto>();
        var secret = forAna.WordOf();
        Assert.False(string.IsNullOrEmpty(secret));

        foreach (var (name, live) in new[] { ("Beto", beto), ("Carla", carla), ("Eva", spectator) })
        {
            var message = await live.NextAsync("SessionUpdated");
            Assert.DoesNotContain(secret!, message.Raw, StringComparison.OrdinalIgnoreCase);
            var view = message.As<GameSessionDto>();
            Assert.Null(view.WordOf());
            Assert.Equal(name == "Eva" ? null : view.Players.Single(p => p.DisplayName == name).Id, view.MyPlayerId);
        }

        Assert.Equal(["pass", "skip"], forAna.AllowedActions);
    }

    [Fact]
    public async Task The_pushed_view_is_identical_to_what_the_rest_endpoint_returns_for_the_same_person()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await using var beto = await LiveConnection.ConnectAsync(factory, table.Beto);
        await beto.SubscribeAsync(table.Id);

        await table.Carla.ActOkAsync(table.Id, "guess", new { text = "errado" });

        var pushed = await beto.NextAsync("SessionUpdated");
        var rest = await (await table.Beto.GetAsync(table.Session.SessionUrl())).Content.ReadAsStringAsync();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(pushed.Raw), JsonNode.Parse(rest)), "o tempo real e o REST devem mostrar a mesma partida");
    }

    [Fact]
    public async Task Lobby_changes_are_pushed_with_a_growing_version()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var session = await ana.CreateSessionAsync(group.Id);
        await using var live = await LiveConnection.ConnectAsync(factory, ana);
        await live.SubscribeAsync(session.Id);

        await beto.PostOkAsync(session.SessionUrl("/join"));
        var joined = (await live.NextAsync("SessionUpdated", m => m.As<GameSessionDto>().Players.Count == 2)).As<GameSessionDto>();
        await beto.PostOkAsync(session.SessionUrl("/leave"));
        var left = (await live.NextAsync("SessionUpdated", m => m.As<GameSessionDto>().Players.Count == 1)).As<GameSessionDto>();

        Assert.True(left.Version > joined.Version);
        Assert.True(joined.Version > session.Version);
    }

    [Fact]
    public async Task All_connections_of_the_same_person_receive_the_update()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await using var phone = await LiveConnection.ConnectAsync(factory, table.Beto);
        await using var laptop = await LiveConnection.ConnectAsync(factory, table.Beto);
        await phone.SubscribeAsync(table.Id);
        await laptop.SubscribeAsync(table.Id);

        await table.Carla.ActOkAsync(table.Id, "guess", new { text = "errado" });

        Assert.NotNull(await phone.NextAsync("SessionUpdated"));
        Assert.NotNull(await laptop.NextAsync("SessionUpdated"));
    }

    [Fact]
    public async Task Unsubscribed_connections_and_other_sessions_receive_nothing()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var other = await factory.NewRelayTableAsync(start: true);
        await using var watching = await LiveConnection.ConnectAsync(factory, table.Beto);
        await using var left = await LiveConnection.ConnectAsync(factory, table.Carla);
        await watching.SubscribeAsync(table.Id);
        await left.SubscribeAsync(table.Id);
        await left.UnsubscribeAsync(table.Id);

        await other.Beto.ActOkAsync(other.Id, "guess", new { text = "errado" });  // outra partida
        await table.Davi.ActOkAsync(table.Id, "guess", new { text = "errado" }); // esta

        Assert.NotNull(await watching.NextAsync("SessionUpdated"));
        Assert.True(await left.GetsNoneAsync("SessionUpdated"));
        Assert.True(await watching.GetsNoneAsync("SessionUpdated")); // e só uma atualização: a da outra partida não veio
    }

    [Fact]
    public async Task Refused_actions_do_not_broadcast_anything()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await using var live = await LiveConnection.ConnectAsync(factory, table.Carla);
        await live.SubscribeAsync(table.Id);

        var refused = await table.Beto.ActAsync(table.Id, "pass"); // Beto não é o mímico

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.True(await live.GetsNoneAsync("SessionUpdated"));
    }

    [Fact]
    public async Task Presence_is_announced_when_someone_subscribes_unsubscribes_or_drops()
    {
        var table = await factory.NewRelayTableAsync();
        await using var ana = await LiveConnection.ConnectAsync(factory, table.Ana);
        var anaView = await ana.SubscribeAsync(table.Id);
        var betoMember = table.Session.Players.Single(p => p.DisplayName == "Beto").MemberId;

        var beto = await LiveConnection.ConnectAsync(factory, table.Beto);
        await beto.SubscribeAsync(table.Id);
        var online = (await ana.NextAsync("PresenceChanged")).As<PresenceView>();
        Assert.Equal((table.Id, betoMember, true), (online.SessionId, online.MemberId, online.Online));

        // uma segunda conexão do mesmo membro não muda a presença
        var betoLaptop = await LiveConnection.ConnectAsync(factory, table.Beto);
        await betoLaptop.SubscribeAsync(table.Id);
        Assert.True(await ana.GetsNoneAsync("PresenceChanged"));

        // fechar uma das duas ainda deixa o membro online; fechar a última, não
        await betoLaptop.UnsubscribeAsync(table.Id);
        Assert.True(await ana.GetsNoneAsync("PresenceChanged"));
        await beto.StopAsync();
        var offline = (await ana.NextAsync("PresenceChanged")).As<PresenceView>();
        Assert.Equal((betoMember, false), (offline.MemberId, offline.Online));

        await beto.DisposeAsync();
        await betoLaptop.DisposeAsync();
        Assert.Equal([anaView.Session.MyMemberId], (await ana.SubscribeAsync(table.Id)).OnlineMemberIds);
    }

    [Fact]
    public async Task Someone_removed_from_the_group_stops_receiving_and_is_told_so()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await using var beto = await LiveConnection.ConnectAsync(factory, table.Beto);
        await beto.SubscribeAsync(table.Id);
        await table.Ana.DeleteAsync(table.Group.Url($"/members/{table.Group.MemberOf(table.Beto).Id}")); // removido do grupo

        await table.Carla.ActOkAsync(table.Id, "guess", new { text = "errado" }); // a próxima mudança

        var revoked = (await beto.NextAsync("AccessRevoked")).As<RevokedView>();
        Assert.Equal(table.Id, revoked.SessionId);
        Assert.True(await beto.GetsNoneAsync("SessionUpdated")); // nada da partida chegou depois da remoção

        await table.Carla.ActOkAsync(table.Id, "guess", new { text = "errado" });
        Assert.True(await beto.GetsNoneAsync("AccessRevoked")); // a assinatura já foi encerrada
    }

    [Fact]
    public async Task A_device_that_logged_out_stops_receiving_while_the_persons_other_devices_keep_receiving()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var second = await factory.CreateClient().LoginAsync(table.Beto.Phone!);
        var otherDevice = new Person { Auth = second, Client = factory.ClientFor(second.AccessToken), Phone = table.Beto.Phone };
        await using var loggedOut = await LiveConnection.ConnectAsync(factory, table.Beto);
        await using var stillIn = await LiveConnection.ConnectAsync(factory, otherDevice);
        await loggedOut.SubscribeAsync(table.Id);
        await stillIn.SubscribeAsync(table.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await table.Beto.PostAsync("/api/v1/auth/logout")).StatusCode); // encerra só o primeiro aparelho
        await table.Carla.ActOkAsync(table.Id, "guess", new { text = "errado" });

        Assert.NotNull(await stillIn.NextAsync("SessionUpdated"));
        Assert.NotNull(await loggedOut.NextAsync("AccessRevoked"));
        Assert.True(await loggedOut.GetsNoneAsync("SessionUpdated")); // o aparelho deslogado não recebe a partida
    }

    [Fact]
    public async Task A_rematch_is_announced_to_the_watchers_of_the_old_session()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/finish"));
        await using var beto = await LiveConnection.ConnectAsync(factory, table.Beto);
        await beto.SubscribeAsync(table.Id);

        var response = await table.Ana.PostAsync(table.Session.SessionUrl("/rematch"));
        var created = await response.ReadAsAsync<GameSessionDto>();

        var notice = (await beto.NextAsync("RematchCreated")).As<RematchView>();
        Assert.Equal((table.Id, created.Id), (notice.SessionId, notice.NewSessionId));
    }

    [Fact]
    public async Task Group_listeners_hear_when_sessions_are_created_started_or_finished()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await using var live = await LiveConnection.ConnectAsync(factory, beto);
        await live.SubscribeGroupAsync(group.Id);

        var session = await ana.CreateSessionAsync(group.Id, "solo");
        var created = (await live.NextAsync("GroupSessionsChanged")).As<GroupView>();
        await ana.PostOkAsync(session.SessionUrl("/start"));
        Assert.NotNull(await live.NextAsync("GroupSessionsChanged"));
        await ana.PostOkAsync(session.SessionUrl("/finish"));
        Assert.NotNull(await live.NextAsync("GroupSessionsChanged"));

        Assert.Equal(group.Id, created.GroupId);

        await live.UnsubscribeGroupAsync(group.Id);
        await ana.CreateSessionAsync(group.Id, "solo");
        Assert.True(await live.GetsNoneAsync("GroupSessionsChanged"));
    }

    [Fact]
    public async Task Plays_in_progress_do_not_flood_the_group_list_but_the_final_move_does_announce_the_end()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var session = await ana.CreateSessionAsync(group.Id, "solo", new { target = 2 });
        await ana.PostOkAsync(session.SessionUrl("/start"));
        await using var live = await LiveConnection.ConnectAsync(factory, ana);
        await live.SubscribeGroupAsync(group.Id);

        await ana.ActOkAsync(session.Id, "add", new { n = 1 });
        Assert.True(await live.GetsNoneAsync("GroupSessionsChanged"));

        var last = await ana.ActOkAsync(session.Id, "add", new { n = 1 });
        Assert.Equal(SessionStatus.Finished, last.Session.Status);
        Assert.NotNull(await live.NextAsync("GroupSessionsChanged"));
    }

    [Fact]
    public async Task One_connection_can_only_hold_so_many_subscriptions()
    {
        var generous = factory.WithSettings(("Sessions:MaxActiveSessionsPerGroup", "40"));
        var ana = await generous.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var sessions = new List<GameSessionDto>();
        for (var i = 0; i < 21; i++)
        {
            sessions.Add(await ana.CreateSessionAsync(group.Id, "solo"));
        }

        await using var live = await LiveConnection.ConnectAsync(generous, ana);
        foreach (var session in sessions.Take(20))
        {
            await live.SubscribeAsync(session.Id);
        }

        var tooMany = await Assert.ThrowsAsync<HubException>(() => live.SubscribeAsync(sessions[20].Id));
        await live.SubscribeAsync(sessions[0].Id); // assinar de novo uma que já tem não conta

        Assert.Contains("realtime.too_many_subscriptions", tooMany.Message);
    }

    [Fact]
    public async Task A_real_websocket_connection_authenticates_with_the_query_token_and_receives_updates()
    {
        await using var real = factory.WithSettings();
        real.UseKestrel();
        real.StartServer();
        var ana = await real.NewPersonAsync("Ana");
        var beto = await real.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var session = await ana.CreateSessionAsync(group.Id, "solo");
        await beto.PostOkAsync(session.SessionUrl("/join"));

        await using var live = await LiveConnection.ConnectAsync(real, beto, HttpTransportType.WebSockets, real.CreateClient().BaseAddress!);
        var subscribed = await live.SubscribeAsync(session.Id);
        await ana.PostOkAsync(session.SessionUrl("/start"));

        var update = (await live.NextAsync("SessionUpdated", m => m.As<GameSessionDto>().Status == SessionStatus.InProgress)).As<GameSessionDto>();
        Assert.Equal(session.Id, subscribed.Session.Id);
        Assert.Equal(["add"], update.AllowedActions);
    }

    private sealed record PresenceView(Guid SessionId, Guid MemberId, bool Online);

    private sealed record RevokedView(Guid SessionId);

    private sealed record RematchView(Guid SessionId, Guid NewSessionId);

    private sealed record GroupView(Guid GroupId);
}
