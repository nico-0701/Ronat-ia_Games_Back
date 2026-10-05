using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Application.Maintenance;

/// <summary>O que uma rodada de limpeza fez.</summary>
public sealed record MaintenanceReport(int LobbiesCancelled, int GamesCancelled, int EventsDeleted, int LoginsDeleted)
{
    public bool IsEmpty => LobbiesCancelled + GamesCancelled + EventsDeleted + LoginsDeleted == 0;
}

/// <summary>
/// Limpeza do banco e das partidas abandonadas, em lote (um comando SQL por tipo). Libera o limite de partidas abertas de um
/// grupo quando alguém esquece um lobby, e mantém o banco gratuito pequeno: a trilha de eventos de partidas encerradas é
/// apagada depois de um tempo (o placar, o livro-razão e o resultado ficam) e as sessões de login velhas também.
/// </summary>
public sealed class MaintenanceService(IAppDbContext db, IOptions<MaintenanceOptions> options, TimeProvider time)
{
    public async Task<MaintenanceReport> RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();

        var lobbyCutoff = now - settings.AbandonedLobbyAfter;
        var gameCutoff = now - settings.AbandonedGameAfter;
        var eventCutoff = now - settings.EventRetention;
        var loginCutoff = now - settings.LoginRetention;

        var lobbies = await db.GameSessions
            .Where(s => s.Status == SessionStatus.Waiting && s.UpdatedAt < lobbyCutoff)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, SessionStatus.Cancelled)
                .SetProperty(s => s.CancelledAt, now)
                .SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.Version, s => s.Version + 1), cancellationToken);

        var games = await db.GameSessions
            .Where(s => s.Status == SessionStatus.InProgress && s.UpdatedAt < gameCutoff)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, SessionStatus.Cancelled)
                .SetProperty(s => s.CancelledAt, now)
                .SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.Version, s => s.Version + 1), cancellationToken);

        var events = await db.GameEvents
            .Where(e => e.CreatedAt < eventCutoff
                && db.GameSessions.Any(s => s.Id == e.SessionId && (s.Status == SessionStatus.Finished || s.Status == SessionStatus.Cancelled)))
            .ExecuteDeleteAsync(cancellationToken);

        var logins = await db.AuthSessions
            .Where(s => s.ExpiresAt < loginCutoff || (s.RevokedAt != null && s.RevokedAt < loginCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        return new MaintenanceReport(lobbies, games, events, logins);
    }
}
