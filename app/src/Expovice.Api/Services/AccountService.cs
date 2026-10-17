using Expovice.Api.Data;
using Expovice.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Services;

/// <summary>Finds or creates the account behind a phone number or an external (Google/Apple) identity.</summary>
/// <remarks>Scoped (uses DbContext) — not thread-safe.</remarks>
public sealed class AccountService(ExpoviceDbContext db, TimeProvider time, ILogger<AccountService> logger)
{
    public async Task<User> FindOrCreateByPhoneAsync(string phone, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.PhoneNumber == phone, ct)
        ?? await CreateAsync(new User { PhoneNumber = phone }, ct);

    public async Task<User> FindOrCreateExternalAsync(ExternalProvider provider, string subject, string? name, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.ExternalProvider == provider && u.ExternalSubject == subject, ct)
        ?? await CreateAsync(new User
        {
            ExternalProvider = provider,
            ExternalSubject = subject,
            Name = (name ?? "").Trim() is { Length: <= Limits.NameMaxLength } n ? n : "",
        }, ct);

    private async Task<User> CreateAsync(User user, CancellationToken ct)
    {
        user.CreatedAt = time.GetUtcNow();
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two sign-ins for the same identity raced; the unique index rejected ours, so load the winner.
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.SingleAsync(
                u => (user.PhoneNumber != null && u.PhoneNumber == user.PhoneNumber)
                    || (user.ExternalSubject != null && u.ExternalProvider == user.ExternalProvider && u.ExternalSubject == user.ExternalSubject),
                ct);
        }

        logger.LogInformation("Created user {UserId} via {Method}", user.Id, user.ExternalProvider?.ToString() ?? "Phone");
        return user;
    }
}
