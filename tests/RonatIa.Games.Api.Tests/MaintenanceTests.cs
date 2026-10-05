using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Maintenance;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Api.Tests;

/// <summary>A limpeza de partidas abandonadas e de dados antigos.</summary>
public sealed class MaintenanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<MaintenanceReport> RunAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MaintenanceService>().RunAsync(CancellationToken.None);
    }

    private Task<SessionStatus> StatusOfAsync(Guid sessionId) =>
        factory.WithDbAsync(db => db.GameSessions.AsNoTracking().Where(s => s.Id == sessionId).Select(s => s.Status).SingleAsync());

    [Fact]
    public async Task An_abandoned_lobby_is_cancelled_and_frees_a_slot_of_the_group()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time).WithWebHostBuilder(builder => builder.UseSetting("Sessions:MaxActiveSessionsPerGroup", "1"));
        var ana = await host.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var forgotten = await ana.CreateSessionAsync(group.Id, "solo");
        Assert.Equal("session.limit_reached", await (await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "solo" })).ReadCodeAsync());

        time.Advance(TimeSpan.FromHours(11));
        Assert.Equal(0, (await RunAsync(host)).LobbiesCancelled); // ainda dentro do prazo (12 h)

        time.Advance(TimeSpan.FromHours(2));
        var report = await RunAsync(host);
        ana = await ana.ReloginAsync(host);

        Assert.True(report.LobbiesCancelled >= 1);
        Assert.Equal(SessionStatus.Cancelled, await StatusOfAsync(forgotten.Id));
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync("/api/v1/sessions", new { groupId = group.Id, gameId = "solo" })).StatusCode);
    }

    [Fact]
    public async Task Activity_keeps_a_lobby_alive()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var ana = await host.NewPersonAsync("Ana");
        var beto = await host.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var lobby = await ana.CreateSessionAsync(group.Id, "solo");

        time.Advance(TimeSpan.FromHours(10));
        ana = await ana.ReloginAsync(host);
        beto = await beto.ReloginAsync(host);
        await beto.PostOkAsync(lobby.SessionUrl("/join")); // movimento no lobby renova o prazo
        time.Advance(TimeSpan.FromHours(10));
        await RunAsync(host);

        Assert.Equal(SessionStatus.Waiting, await StatusOfAsync(lobby.Id));
    }

    [Fact]
    public async Task A_game_nobody_plays_for_a_day_is_cancelled_but_a_recent_one_is_not()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var ana = await host.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var stale = await ana.CreateSessionAsync(group.Id, "solo", new { target = 9 });
        await ana.PostOkAsync(stale.SessionUrl("/start"));

        time.Advance(TimeSpan.FromHours(23));
        await RunAsync(host);
        Assert.Equal(SessionStatus.InProgress, await StatusOfAsync(stale.Id));

        ana = await ana.ReloginAsync(host);
        var fresh = await ana.CreateSessionAsync(group.Id, "solo", new { target = 9 });
        await ana.PostOkAsync(fresh.SessionUrl("/start"));
        time.Advance(TimeSpan.FromHours(2)); // a velha passa de 24 h; a nova tem 2 h
        var report = await RunAsync(host);

        Assert.True(report.GamesCancelled >= 1);
        Assert.Equal(SessionStatus.Cancelled, await StatusOfAsync(stale.Id));
        Assert.Equal(SessionStatus.InProgress, await StatusOfAsync(fresh.Id));
    }

    [Fact]
    public async Task Old_events_of_ended_games_are_deleted_but_the_scoreboard_and_results_stay()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var ana = await host.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var ended = await ana.CreateSessionAsync(group.Id, "solo", new { target = 1 });
        await ana.PostOkAsync(ended.SessionUrl("/start"));
        await ana.ActOkAsync(ended.Id, "add", new { n = 1 });                // termina: Ana vence

        time.Advance(TimeSpan.FromDays(59));
        Assert.Equal(0, (await RunAsync(host)).EventsDeleted);              // ainda dentro da retenção de 60 dias

        ana = await ana.ReloginAsync(host);
        var recent = await ana.CreateSessionAsync(group.Id, "solo", new { target = 50 });
        await ana.PostOkAsync(recent.SessionUrl("/start"));
        await ana.ActOkAsync(recent.Id, "add", new { n = 1 });               // eventos de 59 dias depois do início

        time.Advance(TimeSpan.FromDays(2));                                   // a primeira tem 61 dias; a recente, 2
        var report = await RunAsync(host);

        Assert.True(report.EventsDeleted > 0);
        Assert.Equal(0, await factory.WithDbAsync(db => db.GameEvents.CountAsync(e => e.SessionId == ended.Id)));
        Assert.True(await factory.WithDbAsync(db => db.GameEvents.CountAsync(e => e.SessionId == recent.Id)) > 0); // recentes, ficam (mesmo a partida sendo cancelada por abandono)
        Assert.Equal(1, await factory.WithDbAsync(db => db.SessionResults.CountAsync(r => r.SessionId == ended.Id)));
        Assert.Equal(1, await factory.WithDbAsync(db => db.ScoreEntries.CountAsync(e => e.SessionId == ended.Id)));
    }

    [Fact]
    public async Task Expired_login_sessions_are_deleted_after_the_retention_but_active_ones_stay()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var old = await host.NewPersonAsync("Antiga");
        time.Advance(TimeSpan.FromDays(90 + 29));                              // o login (90 dias) venceu há 29 dias
        Assert.Equal(0, (await RunAsync(host)).LoginsDeleted);

        var current = await old.ReloginAsync(host);                           // outro login, de agora
        time.Advance(TimeSpan.FromDays(2));                                    // o primeiro venceu há 31 dias
        var report = await RunAsync(host);

        Assert.True(report.LoginsDeleted >= 1);
        // O login atual segue valendo: dá para renová-lo (o access token de 30 min já venceu, mas a sessão de login não).
        Assert.Equal(HttpStatusCode.OK, (await host.CreateClient().RefreshRawAsync(current.Auth.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Logins_revoked_long_ago_are_deleted_too()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var ana = await host.NewPersonAsync("Ana");
        await ana.PostAsync("/api/v1/auth/logout");
        time.Advance(TimeSpan.FromDays(31));

        var report = await RunAsync(host);

        Assert.True(report.LoginsDeleted >= 1);
    }

    [Fact]
    public async Task A_run_with_nothing_to_do_changes_nothing()
    {
        var report = await RunAsync(factory);

        Assert.Equal(0, report.LobbiesCancelled + report.GamesCancelled + report.EventsDeleted);
    }

    [Fact]
    public async Task The_background_worker_runs_the_cleanup_by_itself_when_enabled()
    {
        var host = factory.WithSettings(
            ("Maintenance:Enabled", "true"),
            ("Maintenance:Interval", "00:00:01"),
            ("Maintenance:AbandonedLobbyAfter", "00:00:00.100"));
        var ana = await host.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var lobby = await ana.CreateSessionAsync(group.Id, "solo");

        // O serviço espera até 1 s antes da primeira rodada (o intervalo) e então cancela o lobby "abandonado".
        SessionStatus status;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            await Task.Delay(300);
            status = await StatusOfAsync(lobby.Id);
        }
        while (status == SessionStatus.Waiting && DateTime.UtcNow < deadline);

        Assert.Equal(SessionStatus.Cancelled, status);
    }
}
