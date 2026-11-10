using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Crystal.Control.Api.Auth;

public sealed class TokenService
{
    private readonly ControlApiOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public TokenService(ControlApiOptions options)
    {
        _options = options;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public TokenValidationParameters CreateValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = _options.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingCredentials.Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    }

    public AccessTokenMaterial IssueAccessToken(AccountRecord account, Guid sessionId)
    {
        var now = DateTime.UtcNow;
        var accessExpiresAt = now.Add(_options.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.NameIdentifier, account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, account.Username),
            new(JwtRegisteredClaimNames.UniqueName, account.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("sid", sessionId.ToString("D")),
            new("display_name", account.DisplayName),
            new("auth_method", "password")
        };

        claims.AddRange(account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(account.Permissions.Select(permission => new Claim(Permissions.ClaimType, permission)));

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            notBefore: now,
            expires: accessExpiresAt,
            signingCredentials: _signingCredentials);

        return new AccessTokenMaterial(new JwtSecurityTokenHandler().WriteToken(token), accessExpiresAt);
    }

    public RefreshTokenMaterial CreateRefreshTokenMaterial()
    {
        var token = CreateRefreshToken();
        return new RefreshTokenMaterial(token, HashRefreshToken(token), DateTime.UtcNow.Add(_options.RefreshTokenLifetime));
    }

    public static string HashRefreshToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static string CreateRefreshToken()
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }
}
