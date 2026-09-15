using Microsoft.EntityFrameworkCore;
using NovaWallet.BuildingBlocks.Application.Paging;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Infrastructure.Persistence;

public sealed class WalletRepository(WalletDbContext dbContext) : IWalletRepository
{
    public Task<WalletAccount?> GetByIdAsync(Guid walletId, CancellationToken cancellationToken) =>
        dbContext.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);

    public void Add(WalletAccount wallet) => dbContext.Wallets.Add(wallet);

    public async Task<IReadOnlyDictionary<Guid, WalletAccount>> LockForUpdateAsync(IReadOnlyCollection<Guid> walletIds, CancellationToken cancellationToken)
    {
        // Always lock in ascending Id order, regardless of caller-supplied order, so two
        // transfers between the same pair of wallets (in opposite directions) always try to
        // acquire the locks in the same sequence and one simply waits for the other instead
        // of deadlocking.
        var sortedIds = walletIds.Distinct().OrderBy(id => id).ToArray();

        // "xmin" must be selected explicitly for raw SQL — unlike LINQ queries, EF does not
        // inject it automatically, and it's required because WalletAccount is configured with
        // UseXminAsConcurrencyToken.
        var wallets = await dbContext.Wallets
            .FromSqlInterpolated($"SELECT *, xmin FROM wallet.wallets WHERE \"Id\" = ANY({sortedIds}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);

        return wallets.ToDictionary(w => w.Id);
    }

    public void AddLedgerEntry(LedgerEntry entry) => dbContext.LedgerEntries.Add(entry);

    public void AddAuditEntry(AuditEntry entry) => dbContext.AuditEntries.Add(entry);

    public async Task<PagedResult<LedgerEntry>> GetStatementAsync(Guid walletId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = dbContext.LedgerEntries.AsNoTracking().Where(l => l.WalletId == walletId);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenByDescending(l => l.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<LedgerEntry>(items, page.Page, page.PageSize, totalCount);
    }

    public async Task<long> GetOutboundTotalAsync(Guid walletId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await dbContext.LedgerEntries
            .Where(l => l.WalletId == walletId
                        && l.Type == LedgerEntryType.TransferOut
                        && l.CreatedAtUtc >= fromUtc
                        && l.CreatedAtUtc < toUtc)
            .SumAsync(l => (long?)l.AmountKobo, cancellationToken) ?? 0L;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
