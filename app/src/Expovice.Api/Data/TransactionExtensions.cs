using Microsoft.EntityFrameworkCore;

namespace Expovice.Api.Data;

public static class TransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction under the provider's execution strategy,
    /// so Azure SQL transient-fault retries replay the whole unit instead of half of it.
    /// </summary>
    public static Task<T> InTransactionAsync<T>(this ExpoviceDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var result = await work(token);
            await tx.CommitAsync(token);
            return result;
        }, ct);
}
