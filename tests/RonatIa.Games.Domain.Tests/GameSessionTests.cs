using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Domain.Tests;

public sealed class GameSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static GameSession NewSession() =>
        GameSession.Create(Guid.CreateVersion7(), "relay", 1, Guid.CreateVersion7(), """{"turns":4}""", Now);

    private static GameSession Started()
    {
        var session = NewSession();
        session.Start("""{"turn":0}""", 1, eventsAdded: 2, Now.AddMinutes(1));
        return session;
    }

    [Fact]
    public void A_new_session_waits_in_the_lobby_with_no_state()
    {
        var session = NewSession();

        Assert.Equal(SessionStatus.Waiting, session.Status);
        Assert.True(session.IsActive);
        Assert.Null(session.StateJson);
        Assert.Null(session.StateSchemaVersion);
        Assert.Equal(0, session.Version);
        Assert.Equal(0, session.LastEventSeq);
        Assert.Equal("relay", session.GameId);
        Assert.Null(session.RematchOfId);
    }

    [Fact]
    public void Starting_stores_the_state_and_counts_the_opening_events()
    {
        var session = Started();

        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal("""{"turn":0}""", session.StateJson);
        Assert.Equal(1, session.StateSchemaVersion);
        Assert.Equal(2, session.LastEventSeq);
        Assert.Equal(Now.AddMinutes(1), session.StartedAt);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public void Advancing_updates_the_state_and_bumps_the_version_and_the_event_counter()
    {
        var session = Started();

        session.Advance("""{"turn":1}""", 1, eventsAdded: 3, Now.AddMinutes(2));

        Assert.Equal("""{"turn":1}""", session.StateJson);
        Assert.Equal(5, session.LastEventSeq);
        Assert.Equal(2, session.Version);
    }

    [Fact]
    public void The_lobby_only_operations_are_refused_after_the_start()
    {
        var session = Started();

        Assert.Equal("session.not_waiting", Assert.Throws<AppException>(() => session.UpdateConfig("{}", Now)).Code);
        Assert.Equal("session.not_waiting", Assert.Throws<AppException>(() => session.LobbyChanged(Now)).Code);
        Assert.Equal("session.not_waiting", Assert.Throws<AppException>(() => session.Start("{}", 1, 0, Now)).Code);
    }

    [Fact]
    public void Playing_operations_need_a_game_in_progress()
    {
        var session = NewSession();

        Assert.Equal("session.not_in_progress", Assert.Throws<AppException>(() => session.Advance("{}", 1, 1, Now)).Code);
        Assert.Equal("session.not_in_progress", Assert.Throws<AppException>(() => session.Finish(1, Now)).Code);
    }

    [Fact]
    public void Finishing_ends_the_session_and_keeps_it_from_changing_again()
    {
        var session = Started();

        session.Finish(eventsAdded: 1, Now.AddMinutes(9));

        Assert.Equal(SessionStatus.Finished, session.Status);
        Assert.False(session.IsActive);
        Assert.Equal(Now.AddMinutes(9), session.FinishedAt);
        Assert.Throws<AppException>(() => session.Advance("{}", 1, 1, Now));
        Assert.Throws<AppException>(() => session.Cancel(Now));
    }

    [Fact]
    public void Cancelling_works_in_the_lobby_and_in_progress_but_not_after_the_end()
    {
        var lobby = NewSession();
        var running = Started();
        var finished = Started();
        finished.Finish(1, Now);

        lobby.Cancel(Now.AddMinutes(1));
        running.Cancel(Now.AddMinutes(1));

        Assert.Equal(SessionStatus.Cancelled, lobby.Status);
        Assert.Equal(SessionStatus.Cancelled, running.Status);
        Assert.Equal(Now.AddMinutes(1), running.CancelledAt);
        Assert.Equal("session.already_ended", Assert.Throws<AppException>(() => finished.Cancel(Now)).Code);
        Assert.Equal("session.already_ended", Assert.Throws<AppException>(() => lobby.Cancel(Now)).Code);
    }

    [Fact]
    public void Lobby_changes_bump_the_version_so_clients_notice()
    {
        var session = NewSession();

        session.LobbyChanged(Now.AddSeconds(5));
        session.UpdateConfig("""{"turns":9}""", Now.AddSeconds(6));

        Assert.Equal(2, session.Version);
        Assert.Equal("""{"turns":9}""", session.ConfigJson);
        Assert.Equal(Now.AddSeconds(6), session.UpdatedAt);
    }

    [Fact]
    public void A_rematch_remembers_the_original()
    {
        var original = NewSession();

        var rematch = GameSession.Create(original.GroupId, "relay", 1, original.HostMemberId, "{}", Now, rematchOfId: original.Id);

        Assert.Equal(original.Id, rematch.RematchOfId);
        Assert.NotEqual(original.Id, rematch.Id);
    }
}

public sealed class SessionPlayerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_player_joins_active_without_a_team()
    {
        var player = SessionPlayer.Join(Guid.CreateVersion7(), Guid.CreateVersion7(), seat: 2, Now);

        Assert.True(player.IsActive);
        Assert.Equal(2, player.Seat);
        Assert.Null(player.TeamNo);
        Assert.Null(player.LeftAt);
    }

    [Fact]
    public void Leaving_or_being_removed_ends_the_stay_and_clears_the_team()
    {
        var left = SessionPlayer.Join(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, Now);
        var removed = SessionPlayer.Join(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Now);
        left.AssignTeam(1);
        removed.AssignTeam(0);

        left.Leave(Now.AddMinutes(1));
        removed.Remove(Now.AddMinutes(2));

        Assert.Equal(PlayerStatus.Left, left.Status);
        Assert.Equal(PlayerStatus.Removed, removed.Status);
        Assert.False(left.IsActive);
        Assert.Equal(Now.AddMinutes(1), left.LeftAt);
        Assert.Null(left.TeamNo);
        Assert.Null(removed.TeamNo);
    }

    [Fact]
    public void Rejoining_reactivates_the_same_row_at_the_end_of_the_line_without_a_team()
    {
        var player = SessionPlayer.Join(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, Now);
        player.AssignTeam(1);
        player.Leave(Now.AddMinutes(1));

        player.Rejoin(seat: 5, Now.AddMinutes(2));

        Assert.True(player.IsActive);
        Assert.Equal(5, player.Seat);
        Assert.Null(player.TeamNo);
        Assert.Null(player.LeftAt);
        Assert.Equal(Now.AddMinutes(2), player.JoinedAt);
    }
}
