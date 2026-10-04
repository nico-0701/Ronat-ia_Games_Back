using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Infrastructure.Security;

/// <summary>
/// Confere, a cada requisição autenticada, se a sessão de login ainda vale (logout, "sair de todos os aparelhos" e
/// suspensão têm efeito imediato, sem esperar o JWT expirar). Resultados positivos ficam 10 s em memória para
/// não consultar o banco a cada chamada; quem revoga uma sessão chama <see cref="Evict"/>.
/// </summary>
public sealed class SessionValidator(IAppDbContext db, IMemoryCache cache, TimeProvider time) : ISessionValidator
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    public async Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var key = CacheKey(sessionId);
        if (cache.TryGetValue(key, out Guid cachedUserId) && cachedUserId == userId)
        {
            return true;
        }

        var now = time.GetUtcNow();
        var sessionActive = await db.AuthSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);

        var active = sessionActive
            && await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken);

        if (active)
        {
            cache.Set(key, userId, CacheDuration);
        }

        return active;
    }

    public void Evict(Guid sessionId) => cache.Remove(CacheKey(sessionId));

    private static string CacheKey(Guid sessionId) => $"auth-session:{sessionId:N}";
}
