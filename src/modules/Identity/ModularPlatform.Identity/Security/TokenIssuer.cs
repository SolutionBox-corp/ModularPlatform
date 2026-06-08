using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModularPlatform.Abstractions;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Security;

internal sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
internal sealed record RefreshTokenValue(string Raw, string Hash);

internal interface ITokenIssuer
{
    AccessToken IssueAccessToken(Guid userId, Guid? tenantId, string email);
    RefreshTokenValue CreateRefreshToken();
    string HashRefreshToken(string raw);
}

/// <summary>Issues HMAC-signed JWT access tokens and CSPRNG refresh tokens (stored hashed).</summary>
internal sealed class JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock) : ITokenIssuer
{
    private readonly JwtOptions _jwt = options.Value;

    public AccessToken IssueAccessToken(Guid userId, Guid? tenantId, string email)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };
        if (tenantId is not null)
        {
            claims.Add(new Claim(HttpTenantContext.TenantClaim, tenantId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expires);
    }

    public RefreshTokenValue CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return new RefreshTokenValue(raw, HashRefreshToken(raw));
    }

    public string HashRefreshToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
