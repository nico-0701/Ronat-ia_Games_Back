using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Application.Users;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Auth;

/// <summary>
/// Contas e sessões. O telefone é só o identificador (ver ADR-0003): quem entra recebe um JWT curto e um refresh token
/// rotativo; toda requisição seguinte deriva a identidade <b>do token</b>, nunca de um telefone enviado pelo cliente.
/// </summary>
public sealed class AuthService(
    IAppDbContext db,
    IPhoneNormalizer phoneNormalizer,
    IPhoneHasher phoneHasher,
    ITokenService tokenService,
    ICaptchaVerifier captcha,
    ISessionValidator sessionValidator,
    IDbExceptionClassifier dbErrors,
    IOptions<AuthOptions> authOptions,
    IOptions<RegistrationOptions> registrationOptions,
    TimeProvider time,
    ILogger<AuthService> logger)
{
    public async Task<AuthResponse> LoginAsync(LoginRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        await EnsureCaptchaAsync(request.CaptchaToken, client, cancellationToken);

        var phone = phoneNormalizer.Normalize(request.Phone);
        var hash = phoneHasher.Hash(phone.E164);

        var user = await db.Users.SingleOrDefaultAsync(u => u.PhoneHash == hash, cancellationToken);
        if (user is null || user.Status == UserStatus.Deleted)
        {
            throw AppException.NotFound("auth.user_not_found", "Não existe conta com este telefone. Cadastre-se para continuar.");
        }

        if (user.Status == UserStatus.Suspended)
        {
            throw AppException.Forbidden("auth.account_suspended", "Esta conta está suspensa.");
        }

        return await StartSessionAsync(user, client, isNewUser: false, cancellationToken);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        if (registrationOptions.Value.Mode == RegistrationMode.Closed)
        {
            throw AppException.Forbidden("auth.registration_closed", "Os cadastros estão temporariamente fechados.");
        }

        if (!request.AcceptTerms)
        {
            throw AppException.Validation(
                "auth.terms_not_accepted",
                "É preciso aceitar os termos de uso para criar a conta.",
                new Dictionary<string, string[]> { ["acceptTerms"] = ["Aceite os termos de uso para continuar."] });
        }

        await EnsureCaptchaAsync(request.CaptchaToken, client, cancellationToken);

        var phone = phoneNormalizer.Normalize(request.Phone);
        var hash = phoneHasher.Hash(phone.E164);

        if (await db.Users.AnyAsync(u => u.PhoneHash == hash, cancellationToken))
        {
            throw PhoneTaken();
        }

        var user = User.Register(hash, phone.Last4, request.DisplayName, request.AvatarPreset, authOptions.Value.TermsVersion, time.GetUtcNow());
        db.Users.Add(user);

        try
        {
            return await StartSessionAsync(user, client, isNewUser: true, cancellationToken);
        }
        catch (DbUpdateException exception) when (dbErrors.IsUniqueViolation(exception, "ux_users_phone_hash"))
        {
            // Duas pessoas cadastraram o mesmo número ao mesmo tempo; o índice único decidiu quem ficou.
            throw PhoneTaken();
        }
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var options = authOptions.Value;

        if (!RefreshTokens.LooksValid(request.RefreshToken))
        {
            throw InvalidRefreshToken();
        }

        var hash = RefreshTokens.Hash(request.RefreshToken);
        var session = await db.AuthSessions.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        var isRetry = false;

        if (session is null)
        {
            // Pode ser um token que já foi trocado: nova tentativa legítima (resposta perdida) ou token roubado.
            session = await db.AuthSessions.SingleOrDefaultAsync(s => s.PreviousTokenHash == hash, cancellationToken);
            if (session is null)
            {
                throw InvalidRefreshToken();
            }

            var withinGrace = session.RotatedAt is { } rotatedAt && now - rotatedAt <= options.RefreshReuseGrace;
            if (!withinGrace)
            {
                if (session.RevokedAt is null)
                {
                    session.Revoke(AuthSession.ReasonReuseDetected, now);
                    await db.SaveChangesAsync(cancellationToken);
                    sessionValidator.Evict(session.Id);
                    logger.LogWarning("Reutilização de refresh token detectada; sessão {SessionId} revogada", session.Id);
                }

                throw InvalidRefreshToken();
            }

            isRetry = true;
        }

        if (!session.IsActive(now))
        {
            throw InvalidRefreshToken();
        }

        var user = await db.Users.SingleAsync(u => u.Id == session.UserId, cancellationToken);
        if (user.Status != UserStatus.Active)
        {
            session.Revoke(AuthSession.ReasonAccountUnavailable, now);
            await db.SaveChangesAsync(cancellationToken);
            sessionValidator.Evict(session.Id);
            throw InvalidRefreshToken();
        }

        var (refreshToken, refreshHash) = RefreshTokens.Generate();
        if (isRetry)
        {
            session.ReplaceCurrentToken(refreshHash, now, options.RefreshTokenLifetime);
        }
        else
        {
            session.Rotate(refreshHash, now, options.RefreshTokenLifetime);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Duas renovações simultâneas com o mesmo token: a segunda perde; o cliente tenta de novo (cai na janela de tolerância).
            throw AppException.Conflict("auth.refresh_conflict", "A sessão estava sendo renovada em outro lugar. Tente de novo.");
        }

        var access = tokenService.Issue(user, session.Id, now);
        return new AuthResponse(access.Value, access.ExpiresAt, refreshToken, session.ExpiresAt, false, user.ToDto());
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.AuthSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(AuthSession.ReasonLogout, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        sessionValidator.Evict(session.Id);
    }

    public async Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var sessions = await db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(AuthSession.ReasonLogoutAll, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var session in sessions)
        {
            sessionValidator.Evict(session.Id);
        }
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, Guid currentSessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var sessions = await db.AuthSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt)
            .ToListAsync(cancellationToken);

        return sessions
            .Select(s => new SessionDto(s.Id, s.DeviceLabel, s.CreatedAt, s.LastUsedAt, s.Id == currentSessionId))
            .ToList();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.AuthSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("auth.session_not_found", "Sessão não encontrada.");

        session.Revoke(AuthSession.ReasonLogout, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        sessionValidator.Evict(session.Id);
    }

    private async Task<AuthResponse> StartSessionAsync(User user, ClientContext client, bool isNewUser, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var options = authOptions.Value;
        var (refreshToken, refreshHash) = RefreshTokens.Generate();

        var session = AuthSession.Start(user.Id, refreshHash, client.DeviceLabel, now, options.RefreshTokenLifetime);
        db.AuthSessions.Add(session);
        user.RecordLogin(now);

        await RevokeSurplusSessionsAsync(user.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokenService.Issue(user, session.Id, now);
        return new AuthResponse(access.Value, access.ExpiresAt, refreshToken, session.ExpiresAt, isNewUser, user.ToDto());
    }

    /// <summary>Mantém no máximo N aparelhos conectados: ao passar disso, desconecta o menos usado.</summary>
    private async Task RevokeSurplusSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var max = Math.Max(1, authOptions.Value.MaxSessionsPerUser);
        var active = await db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt)
            .ToListAsync(cancellationToken);

        foreach (var surplus in active.Skip(max - 1))
        {
            surplus.Revoke(AuthSession.ReasonSessionLimit, now);
            sessionValidator.Evict(surplus.Id);
        }
    }

    private async Task EnsureCaptchaAsync(string? token, ClientContext client, CancellationToken cancellationToken)
    {
        if (!captcha.IsEnabled)
        {
            return;
        }

        if (!await captcha.VerifyAsync(token, client.IpAddress, cancellationToken))
        {
            throw AppException.Validation(
                "auth.captcha_failed",
                "Não foi possível confirmar que você não é um robô. Tente de novo.",
                new Dictionary<string, string[]> { ["captchaToken"] = ["Verificação anti-robô inválida ou expirada."] });
        }
    }

    private static AppException PhoneTaken() =>
        AppException.Conflict("auth.phone_taken", "Já existe uma conta com este telefone. Entre com ele.");

    private static AppException InvalidRefreshToken() =>
        AppException.Unauthorized("auth.invalid_refresh_token", "Sessão expirada. Entre novamente.");
}
