using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Security;
using ModularPlatform.Web;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

public sealed class TokenIssuerTests
{
    [Fact]
    public void Access_token_expiry_honors_configured_lifetime()
    {
        var now = new DateTimeOffset(2026, 6, 28, 10, 30, 0, TimeSpan.Zero);
        var issuer = CreateIssuer(now, accessTokenMinutes: 17);

        var token = issuer.IssueAccessToken(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "user@example.com",
            ["admin"],
            ["billing.read"]);

        token.Value.ShouldNotBeNullOrWhiteSpace();
        token.ExpiresAt.ShouldBe(now.AddMinutes(17));
    }

    [Fact]
    public void Refresh_token_hash_is_deterministic_sha256_hex_and_never_the_raw_token()
    {
        var issuer = CreateIssuer(DateTimeOffset.UnixEpoch);
        const string raw = "raw-refresh-token";

        var hash = issuer.HashRefreshToken(raw);
        var sameHash = issuer.HashRefreshToken(raw);
        var otherHash = issuer.HashRefreshToken("different-refresh-token");
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

        hash.ShouldBe(expected);
        sameHash.ShouldBe(hash);
        otherHash.ShouldNotBe(hash);
        hash.ShouldNotBe(raw);
        hash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task Access_token_validates_with_configured_key_and_rejects_wrong_key()
    {
        var now = new DateTimeOffset(2026, 6, 28, 10, 30, 0, TimeSpan.Zero);
        var issuer = CreateIssuer(now);
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        var token = issuer.IssueAccessToken(userId, tenantId, "user@example.com", ["admin"], ["billing.read"]);

        var valid = await ValidateAsync(token.Value, "0123456789abcdef0123456789abcdef");
        valid.IsValid.ShouldBeTrue(valid.Exception?.ToString());
        valid.Claims[ClaimTypes.NameIdentifier].ToString().ShouldBe(userId.ToString());
        valid.Claims[HttpTenantContext.TenantClaim].ToString().ShouldBe(tenantId.ToString());
        valid.Claims[AuthorizationClaims.Role].ToString().ShouldBe("admin");
        valid.Claims[AuthorizationClaims.Permission].ToString().ShouldBe("billing.read");

        var wrongKey = await ValidateAsync(token.Value, "abcdef0123456789abcdef0123456789");
        wrongKey.IsValid.ShouldBeFalse("a token signed with a different HMAC key must be rejected");
    }

    private static JwtTokenIssuer CreateIssuer(DateTimeOffset now, int accessTokenMinutes = 10)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "modular-platform-tests",
            Audience = "modular-platform-tests",
            SigningKey = "0123456789abcdef0123456789abcdef",
            AccessTokenMinutes = accessTokenMinutes,
        });

        return new JwtTokenIssuer(options, new FixedClock(now));
    }

    private static async Task<TokenValidationResult> ValidateAsync(string token, string signingKey)
    {
        var handler = new JsonWebTokenHandler();
        return await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "modular-platform-tests",
            ValidateAudience = true,
            ValidAudience = "modular-platform-tests",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = false,
            RoleClaimType = AuthorizationClaims.Role,
        });
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
