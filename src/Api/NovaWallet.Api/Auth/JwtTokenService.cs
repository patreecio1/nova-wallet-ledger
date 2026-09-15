using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace NovaWallet.Api.Auth;

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// Mints demo JWTs for the take-home. This stands in for a real identity provider (e.g. a BVN/NIN-
/// verified KYC login flow) — it signs whatever customerId/role it is asked for, with no
/// credential check at all. That is why the endpoint that calls this is only mapped outside
/// Production (see Program.cs) — a real deployment would replace this whole class with a call
/// to an actual auth service and never expose token minting like this.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public IssuedToken Issue(Guid customerId, string role)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, customerId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimNames.Role, role),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
