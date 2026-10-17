using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Expovice.Api.Data;
using Expovice.Api.Infrastructure;
using Expovice.Api.Options;
using Expovice.Contracts;
using Microsoft.Extensions.Options;

namespace Expovice.Api.Services;

public enum OtpSendResult
{
    Sent,
    TooSoon,
}

public enum OtpVerifyResult
{
    Valid,
    Invalid,
    Expired,
    TooManyAttempts,
}

/// <summary>Creates and checks one-time SMS codes. Codes are stored only as HMAC hashes.</summary>
/// <remarks>Scoped (uses DbContext) — not thread-safe.</remarks>
public sealed partial class OtpService(
    ExpoviceDbContext db,
    ISmsSender sms,
    IOptions<JwtOptions> jwt,
    TimeProvider time,
    ILogger<OtpService> logger)
{
    [GeneratedRegex(@"^\+91[6-9]\d{9}$")]
    private static partial Regex IndianMobile();

    public static bool IsValidPhone(string? phone) => phone is not null && IndianMobile().IsMatch(phone);

    public async Task<OtpSendResult> SendAsync(string phone, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var challenge = await db.OtpChallenges.FindAsync([phone], ct);
        if (challenge is not null && now - challenge.LastSentAt < TimeSpan.FromSeconds(Limits.OtpResendSeconds))
        {
            return OtpSendResult.TooSoon;
        }

        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, Limits.OtpLength)).ToString($"D{Limits.OtpLength}");
        if (challenge is null)
        {
            challenge = new OtpChallenge { PhoneNumber = phone };
            db.OtpChallenges.Add(challenge);
        }

        challenge.CodeHash = HashCode(phone, code);
        challenge.ExpiresAt = now.AddMinutes(Limits.OtpTtlMinutes);
        challenge.Attempts = 0;
        challenge.LastSentAt = now;
        await db.SaveChangesAsync(ct);

        await sms.SendAsync(phone, $"{code} is your Expovice code. It expires in {Limits.OtpTtlMinutes} minutes. Don't share it with anyone.", ct);
        logger.LogInformation("OTP sent to {Phone}", PhoneMask.Mask(phone));
        return OtpSendResult.Sent;
    }

    public async Task<OtpVerifyResult> VerifyAsync(string phone, string code, CancellationToken ct)
    {
        var challenge = await db.OtpChallenges.FindAsync([phone], ct);
        if (challenge is null)
        {
            return OtpVerifyResult.Invalid;
        }

        if (challenge.ExpiresAt <= time.GetUtcNow())
        {
            return OtpVerifyResult.Expired;
        }

        if (challenge.Attempts >= Limits.OtpMaxAttempts)
        {
            return OtpVerifyResult.TooManyAttempts;
        }

        // Constant-time comparison so response timing reveals nothing about the code.
        if (!CryptographicOperations.FixedTimeEquals(challenge.CodeHash, HashCode(phone, code)))
        {
            challenge.Attempts++;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Wrong OTP for {Phone} (attempt {Attempt})", PhoneMask.Mask(phone), challenge.Attempts);
            return challenge.Attempts >= Limits.OtpMaxAttempts ? OtpVerifyResult.TooManyAttempts : OtpVerifyResult.Invalid;
        }

        db.OtpChallenges.Remove(challenge); // single use
        await db.SaveChangesAsync(ct);
        return OtpVerifyResult.Valid;
    }

    // A separate key is derived (HKDF) so the OTP hash never uses the JWT signing key directly.
    private byte[] HashCode(string phone, string code)
    {
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromBase64String(jwt.Value.SigningKey), 32, info: "expovice-otp"u8.ToArray());
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{phone}:{code}"));
    }
}
