using System.Security.Cryptography;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Domain.Tests;

public sealed class AuthSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    private static byte[] Hash() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void A_new_session_is_active_until_it_expires()
    {
        var session = AuthSession.Start(Guid.CreateVersion7(), Hash(), "Pixel 7 · Chrome", Now, Lifetime);

        Assert.True(session.IsActive(Now));
        Assert.True(session.IsActive(Now + Lifetime - TimeSpan.FromSeconds(1)));
        Assert.False(session.IsActive(Now + Lifetime));
        Assert.Equal("Pixel 7 · Chrome", session.DeviceLabel);
    }

    [Fact]
    public void Rotating_keeps_the_previous_token_hash_and_extends_the_lifetime()
    {
        var first = Hash();
        var second = Hash();
        var session = AuthSession.Start(Guid.CreateVersion7(), first, null, Now, Lifetime);

        session.Rotate(second, Now.AddDays(10), Lifetime);

        Assert.Equal(second, session.TokenHash);
        Assert.Equal(first, session.PreviousTokenHash);
        Assert.Equal(Now.AddDays(10), session.RotatedAt);
        Assert.Equal(Now.AddDays(10) + Lifetime, session.ExpiresAt);
    }

    [Fact]
    public void Revoking_is_idempotent_and_keeps_the_first_reason()
    {
        var session = AuthSession.Start(Guid.CreateVersion7(), Hash(), null, Now, Lifetime);

        session.Revoke(AuthSession.ReasonLogout, Now.AddMinutes(1));
        session.Revoke(AuthSession.ReasonAdmin, Now.AddMinutes(2));

        Assert.False(session.IsActive(Now.AddMinutes(3)));
        Assert.Equal(AuthSession.ReasonLogout, session.RevokedReason);
        Assert.Equal(Now.AddMinutes(1), session.RevokedAt);
    }

    [Fact]
    public void Device_label_is_trimmed_truncated_and_optional()
    {
        var long_ = AuthSession.Start(Guid.CreateVersion7(), Hash(), "  " + new string('x', 300) + "  ", Now, Lifetime);
        var blank = AuthSession.Start(Guid.CreateVersion7(), Hash(), "   ", Now, Lifetime);

        Assert.Equal(100, long_.DeviceLabel!.Length);
        Assert.Null(blank.DeviceLabel);
    }
}
