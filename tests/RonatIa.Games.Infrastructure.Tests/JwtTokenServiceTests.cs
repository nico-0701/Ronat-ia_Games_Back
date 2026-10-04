using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Users;
using RonatIa.Games.Infrastructure.Security;

namespace RonatIa.Games.Infrastructure.Tests;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static JwtTokenService Service(JwtOptions? options = null) =>
        new(Options.Create(options ?? new JwtOptions { SigningKey = Key }));

    private static User NewUser() =>
        User.Register(RandomNumberGenerator.GetBytes(32), "7777", "Nicole Silva", null, "2026-10", Now);

    private static async Task<TokenValidationResult> ValidateAsync(string token, string? key = null) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "ronat-ia-games",
            ValidAudience = "ronat-ia-games-clients",
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(key ?? Key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = false,
        });

    [Fact]
    public async Task The_token_is_signed_with_hs256_and_carries_only_the_expected_claims()
    {
        var user = NewUser();
        var sessionId = Guid.CreateVersion7();

        var access = Service().Issue(user, sessionId, Now);
        var result = await ValidateAsync(access.Value);
        var token = (JsonWebToken)result.SecurityToken;

        Assert.True(result.IsValid);
        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Alg);
        Assert.Equal(JwtKeys.CurrentKeyId, token.Kid);
        Assert.Equal(user.Id.ToString(), token.GetClaim("sub").Value);
        Assert.Equal(sessionId.ToString(), token.GetClaim("sid").Value);
        Assert.Equal("ronat-ia-games", token.Issuer);
        Assert.Contains("ronat-ia-games-clients", token.Audiences);

        var claimTypes = token.Claims.Select(claim => claim.Type).ToHashSet();
        Assert.Subset(new HashSet<string> { "sub", "sid", "jti", "iss", "aud", "iat", "nbf", "exp" }, claimTypes);
    }

    [Fact]
    public void The_token_lifetime_follows_the_configuration()
    {
        var access = Service(new JwtOptions { SigningKey = Key, AccessTokenLifetime = TimeSpan.FromMinutes(7) }).Issue(NewUser(), Guid.CreateVersion7(), Now);

        var token = new JsonWebToken(access.Value);

        Assert.Equal(Now.AddMinutes(7), access.ExpiresAt);
        Assert.Equal(Now.AddMinutes(7).UtcDateTime, token.ValidTo);
        Assert.Equal(Now.UtcDateTime, token.ValidFrom);
    }

    [Fact]
    public void Each_token_has_a_unique_id()
    {
        var user = NewUser();
        var service = Service();

        var first = new JsonWebToken(service.Issue(user, Guid.CreateVersion7(), Now).Value);
        var second = new JsonWebToken(service.Issue(user, Guid.CreateVersion7(), Now).Value);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void The_token_never_contains_the_name_or_the_phone()
    {
        var access = Service().Issue(NewUser(), Guid.CreateVersion7(), Now);
        var payload = new JsonWebToken(access.Value).EncodedPayload;
        var json = System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(payload));

        Assert.DoesNotContain("Nicole", json);
        Assert.DoesNotContain("7777", json);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", json);
    }

    [Fact]
    public async Task Only_a_token_signed_with_the_same_key_validates()
    {
        var access = Service().Issue(NewUser(), Guid.CreateVersion7(), Now);
        var otherKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        Assert.True((await ValidateAsync(access.Value)).IsValid);
        Assert.False((await ValidateAsync(access.Value, otherKey)).IsValid);
    }

    [Fact]
    public void The_previous_key_is_used_only_for_validation_during_a_rotation()
    {
        var previous = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var options = new JwtOptions { SigningKey = Key, PreviousSigningKey = previous };

        var keys = JwtKeys.ForValidation(options);

        Assert.Equal(2, keys.Count);
        Assert.Equal(JwtKeys.CurrentKeyId, keys[0].KeyId);
        Assert.Equal(JwtKeys.PreviousKeyId, keys[1].KeyId);
        Assert.Single(JwtKeys.ForValidation(new JwtOptions { SigningKey = Key }));
    }
}

public sealed class RefreshTokenTests
{
    [Fact]
    public void Generated_tokens_have_the_expected_format_and_a_matching_hash()
    {
        var (token, hash) = RefreshTokens.Generate();

        Assert.Equal(RefreshTokens.TokenLength, token.Length);
        Assert.True(RefreshTokens.LooksValid(token));
        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, RefreshTokens.Hash(token));
    }

    [Fact]
    public void Tokens_are_unique()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => RefreshTokens.Generate().Token).ToHashSet();

        Assert.Equal(200, tokens.Count);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("curto", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+/", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    public void Only_well_formed_tokens_pass_the_cheap_format_check(string? token, bool expected)
    {
        Assert.Equal(expected, RefreshTokens.LooksValid(token));
    }
}
