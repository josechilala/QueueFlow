using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Infrastructure.Authentication;

internal sealed class JwtTokenService(IConfiguration configuration) : ITokenService
{
    public string CreateAccessToken(Guid userId, Guid organizationId, UserRole role, string email)
    {
        var key = configuration["Authentication:JwtKey"] ?? throw new InvalidOperationException("JWT key is not configured.");
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("identity_type", "tenant"), new Claim("organization_id", organizationId.ToString()), new Claim(ClaimTypes.Role, role.ToString()), new Claim(JwtRegisteredClaimNames.Email, email) };
        var token = new JwtSecurityToken(configuration["Authentication:Issuer"], configuration["Authentication:Audience"], claims, expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public string CreatePlatformAccessToken(Guid platformUserId, string email)
    {
        var key = configuration["Authentication:JwtKey"] ?? throw new InvalidOperationException("JWT key is not configured.");
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, platformUserId.ToString()), new Claim(ClaimTypes.NameIdentifier, platformUserId.ToString()), new Claim("identity_type", "platform"), new Claim(ClaimTypes.Role, "PlatformAdmin"), new Claim(JwtRegisteredClaimNames.Email, email) };
        var token = new JwtSecurityToken(configuration["Authentication:Issuer"], configuration["Authentication:Audience"], claims, expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    // This envelope proves only a rate-limit identity. Authentication still requires
    // the exact hash in PostgreSQL and an atomic, single-use rotation.
    public string CreateRefreshToken(Guid userId, IdentityType identityType)
    {
        var payload = $"qfr1.{identityType}.{userId:N}.{DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()}.{Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64))}";
        return $"{payload}.{Base64UrlEncoder.Encode(SignRefresh(payload))}";
    }

    public Guid? GetRefreshRateLimitIdentity(string token, IdentityType identityType)
    {
        if (token.Length > 512) return null;
        var parts = token.Split('.');
        if (parts.Length != 6 || parts[0] != "qfr1" || parts[1] != identityType.ToString() ||
            !Guid.TryParseExact(parts[2], "N", out var userId) || userId == Guid.Empty ||
            !long.TryParse(parts[3], out var expires) || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
        try
        {
            var signature = Base64UrlEncoder.DecodeBytes(parts[5]);
            return CryptographicOperations.FixedTimeEquals(signature, SignRefresh(string.Join('.', parts, 0, 5))) ? userId : null;
        }
        catch (FormatException) { return null; }
    }

    private byte[] SignRefresh(string payload)
    {
        var key = configuration["Authentication:JwtKey"] ?? throw new InvalidOperationException("JWT key is not configured.");
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes("queueflow-refresh-budget:" + payload));
    }
    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
