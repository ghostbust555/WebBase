using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Services;

/// <summary>
/// Makes the sign-in token (a JWT) the browser sends back on every request as
/// "Authorization: Bearer ...". It is signed with Jwt:Key, which is how the
/// server knows it made it and nobody edited it.
/// </summary>
public class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _jwt = options.Value;

    public string CreateToken(User user)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()),
            new Claim("email", user.Email),
            new Claim("name", user.DisplayName),
            // When it was issued, so the client can renew it at half-life.
            new Claim("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddDays(_jwt.LifetimeDays),
            signingCredentials: new SigningCredentials(SigningKey(_jwt.Key), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static SymmetricSecurityKey SigningKey(string key) => new(Encoding.UTF8.GetBytes(key));
}
