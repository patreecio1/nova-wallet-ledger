using Microsoft.EntityFrameworkCore;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Domain;
using NovaWallet.Wallet.Infrastructure.Persistence;
using Npgsql;

namespace NovaWallet.Wallet.Infrastructure.Idempotency;

public sealed class IdempotencyStore(WalletDbContext dbContext) : IIdempotencyStore
{
    public async Task<IdempotencyClaim> TryClaimAsync(string key, string requestHash, CancellationToken cancellationToken)
    {
        var existing = await dbContext.IdempotencyRecords.AsTracking().FirstOrDefaultAsync(r => r.Key == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
                return new IdempotencyClaim(IdempotencyOutcome.ReusedWithDifferentPayload);

            return existing.ResponsePayload is null
                ? new IdempotencyClaim(IdempotencyOutcome.InFlight)
                : new IdempotencyClaim(IdempotencyOutcome.Completed, existing.ResponsePayload);
        }

        var record = IdempotencyRecord.Claim(key, requestHash, DateTime.UtcNow);
        dbContext.IdempotencyRecords.Add(record);

        try
        {
            // A standalone SaveChanges (own implicit transaction) so that a concurrent claim
            // racing for the same brand-new key hits the unique constraint on Key right here,
            // rather than later once real work has already started against the wallets.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            return new IdempotencyClaim(IdempotencyOutcome.InFlight);
        }

        return new IdempotencyClaim(IdempotencyOutcome.Claimed);
    }

    public void Complete(string key, string responsePayload)
    {
        var record = dbContext.ChangeTracker.Entries<IdempotencyRecord>()
            .Select(e => e.Entity)
            .FirstOrDefault(r => r.Key == key);

        if (record is null)
            throw new InvalidOperationException($"Idempotency record '{key}' is not tracked — TryClaimAsync must be awaited first, on the same DbContext.");

        record.Complete(responsePayload, DateTime.UtcNow);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
