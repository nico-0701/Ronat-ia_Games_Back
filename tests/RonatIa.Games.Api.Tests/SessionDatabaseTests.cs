using Microsoft.EntityFrameworkCore;
using Npgsql;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Infrastructure.Persistence;

namespace RonatIa.Games.Api.Tests;

/// <summary>As regras da partida que o banco garante sozinho (sequência, idempotência, consistência do estado, integridade).</summary>
public sealed class SessionDatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string?> FailedConstraintAsync(Func<AppDbContext, Task> action)
    {
        try
        {
            await factory.WithDbAsync(action);
        }
        catch (Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is PostgresException postgres)
                {
                    return postgres.ConstraintName;
                }
            }

            throw;
        }

        return null;
    }

    private static Task InsertEventAsync(AppDbContext db, Guid sessionId, int seq, Guid? clientActionId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.game_events (session_id, seq, type, client_action_id, created_at) VALUES ({sessionId}, {seq}, 'teste', {clientActionId}, {now})");
    }

    [Fact]
    public async Task Event_sequence_numbers_are_unique_per_session_and_start_at_one()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var lastSeq = (await table.Ana.EventsAsync(table.Id)).Max(e => e.Seq);

        var duplicate = await FailedConstraintAsync(db => InsertEventAsync(db, table.Id, lastSeq));
        var zero = await FailedConstraintAsync(db => InsertEventAsync(db, table.Id, 0));
        var next = await FailedConstraintAsync(db => InsertEventAsync(db, table.Id, lastSeq + 1));

        Assert.Equal("ux_game_events_session_seq", duplicate);
        Assert.Equal("ck_game_events_seq", zero);
        Assert.Null(next);
    }

    [Fact]
    public async Task A_client_action_id_is_unique_per_session_and_missing_ones_do_not_collide()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var otherTable = await factory.NewRelayTableAsync(start: true);
        var lastSeq = (await table.Ana.EventsAsync(table.Id)).Max(e => e.Seq);
        var id = Guid.NewGuid();

        var first = await FailedConstraintAsync(db => InsertEventAsync(db, table.Id, lastSeq + 1, id));
        var repeated = await FailedConstraintAsync(db => InsertEventAsync(db, table.Id, lastSeq + 2, id));
        var otherSession = await FailedConstraintAsync(db => InsertEventAsync(db, otherTable.Id, 99, id));
        var nulls = await FailedConstraintAsync(async db =>
        {
            await InsertEventAsync(db, table.Id, lastSeq + 3);
            await InsertEventAsync(db, table.Id, lastSeq + 4);
        });

        Assert.Null(first);
        Assert.Equal("ux_game_events_client_action", repeated);
        Assert.Null(otherSession); // o mesmo id em outra partida é outro assunto
        Assert.Null(nulls);
    }

    [Fact]
    public async Task A_running_or_finished_session_must_have_a_state_and_the_state_columns_go_together()
    {
        var table = await factory.NewRelayTableAsync(); // no lobby: sem estado

        var noState = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.game_sessions SET status = 'in_progress' WHERE id = {table.Id}"));
        var halfState = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.game_sessions SET state = '{{}}'::jsonb WHERE id = {table.Id}"));
        var badStatus = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.game_sessions SET status = 'pausada' WHERE id = {table.Id}"));
        var negative = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.game_sessions SET version = -1 WHERE id = {table.Id}"));

        Assert.Equal("ck_game_sessions_state_present", noState);
        Assert.Equal("ck_game_sessions_state_pair", halfState);
        Assert.Equal("ck_game_sessions_status", badStatus);
        Assert.Equal("ck_game_sessions_version", negative);
    }

    [Fact]
    public async Task A_member_has_one_row_per_session_and_the_team_and_left_at_are_coherent()
    {
        var table = await factory.NewRelayTableAsync();
        var playerId = table.PlayerIdOf(table.Beto);
        var betoMember = table.Session.Players.Single(p => p.Id == playerId).MemberId;
        var now = DateTimeOffset.UtcNow;

        var duplicate = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.game_session_players (id, session_id, member_id, seat, status, joined_at) VALUES ({Guid.CreateVersion7()}, {table.Id}, {betoMember}, 9, 'joined', {now})"));
        var badTeam = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.game_session_players SET team_no = 16 WHERE id = {playerId}"));
        var leftWithoutDate = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.game_session_players SET status = 'left' WHERE id = {playerId}"));
        var badStatus = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.game_session_players SET status = 'banido', left_at = now() WHERE id = {playerId}"));

        Assert.Equal("ux_game_session_players_session_member", duplicate);
        Assert.Equal("ck_game_session_players_team", badTeam);
        Assert.Equal("ck_game_session_players_left_at", leftWithoutDate);
        Assert.Equal("ck_game_session_players_status", badStatus);
    }

    [Fact]
    public async Task A_score_entry_needs_a_player_or_a_team()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        var now = DateTimeOffset.UtcNow;

        var nobody = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.score_entries (session_id, points, reason, event_seq, created_at) VALUES ({table.Id}, 1, 'teste', 1, {now})"));
        var teamOnly = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.score_entries (session_id, team_no, points, reason, event_seq, created_at) VALUES ({table.Id}, 0, 1, 'teste', 1, {now})"));

        Assert.Equal("ck_score_entries_target", nobody);
        Assert.Null(teamOnly);
    }

    [Fact]
    public async Task A_player_has_at_most_one_result_per_session()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/finish"));
        var playerId = table.PlayerIdOf(table.Beto);
        var memberId = table.Session.Players.Single(p => p.Id == playerId).MemberId;
        var now = DateTimeOffset.UtcNow;

        var duplicate = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO app.session_results (session_id, player_id, member_id, group_id, game_id, rank, score, is_winner, finished_at)
            VALUES ({table.Id}, {playerId}, {memberId}, {table.Group.Id}, 'relay', 1, 0, true, {now})
            """));
        var zeroRank = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.session_results SET rank = 0 WHERE session_id = {table.Id}"));

        Assert.Equal("pk_session_results", duplicate);
        Assert.Equal("ck_session_results_rank", zeroRank);
    }

    [Fact]
    public async Task Deleting_a_session_removes_everything_that_belongs_to_it()
    {
        var table = await factory.NewRelayTableAsync(start: true);
        await table.Beto.ActOkAsync(table.Id, "guess", new { text = table.Session.WordOf() });
        await table.Ana.PostOkAsync(table.Session.SessionUrl("/finish"));

        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.game_sessions WHERE id = {table.Id}"));

        var counts = await factory.WithDbAsync(async db => new[]
        {
            await db.SessionPlayers.CountAsync(p => p.SessionId == table.Id),
            await db.GameEvents.CountAsync(e => e.SessionId == table.Id),
            await db.ScoreEntries.CountAsync(e => e.SessionId == table.Id),
            await db.SessionResults.CountAsync(r => r.SessionId == table.Id),
        });
        Assert.All(counts, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task A_group_member_with_game_history_cannot_be_hard_deleted()
    {
        var table = await factory.NewRelayTableAsync();
        var betoMember = table.Session.Players.Single(p => p.DisplayName == "Beto").MemberId;

        var constraint = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.group_members WHERE id = {betoMember}"));

        Assert.Equal("fk_game_session_players_group_members_member_id", constraint);
    }
}
