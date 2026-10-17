using System.Security.Claims;
using System.Security.Cryptography;
using Expovice.Api.Data;
using Expovice.Api.Options;
using Expovice.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Expovice.Api.Services;

/// <summary>Issues access JWTs and manages rotating, hashed refresh tokens.</summary>
/// <remarks>Scoped (uses the scoped DbContext) — not thread-safe; one instance per request.</remarks>
public sealed class TokenService(
    ExpoviceDbContext db,
    IOptions<JwtOptions> options,
    TimeProvider time,
    ILogger<TokenService> logger)
{
    private static readonly JsonWebTokenHandler Handler = new();
    private readonly JwtOptions _jwt = options.Value;

    public static SymmetricSecurityKey CreateSigningKey(string base64) => new(Convert.FromBase64String(base64));

    /// <summary>Signs in <paramref name="user"/>: new access token + new refresh token.</summary>
    public async Task<AuthResponse> IssueAsync(User user, CancellationToken ct)
    {
        var refresh = await CreateRefreshTokenAsync(user.Id, ct);
        return CreateResponse(user, refresh);
    }

    /// <summary>Creates and persists a refresh token, returning the raw (unhashed) value.</summary>
    public async Task<string> CreateRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>
    /// Rotates a refresh token. Returns null when invalid. Presenting an already-revoked token
    /// means it was stolen or replayed, so every session of that user is revoked.
    /// </summary>
    public async Task<AuthResponse?> RefreshAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = time.GetUtcNow();
        if (token is null || token.ExpiresAt <= now)
        {
            return null;
        }

        if (token.RevokedAt is not null)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", token.UserId);
            await RevokeAllAsync(token.UserId, ct);
            return null;
        }

        // Atomic claim of the token: only one concurrent refresh with the same token can win.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        if (claimed == 0)
        {
            return null;
        }

        var user = await db.Users.SingleAsync(u => u.Id == token.UserId, ct);
        return await IssueAsync(user, ct);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var now = time.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private Task<int> RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private AuthResponse CreateResponse(User user, string refreshToken)
    {
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
            ]),
            SigningCredentials = new SigningCredentials(CreateSigningKey(_jwt.SigningKey), SecurityAlgorithms.HmacSha256),
        };
        return new AuthResponse(Handler.CreateToken(descriptor), expires, refreshToken, string.IsNullOrWhiteSpace(user.Name));
    }

    private static byte[] Hash(string raw) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
}
