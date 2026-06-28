using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
