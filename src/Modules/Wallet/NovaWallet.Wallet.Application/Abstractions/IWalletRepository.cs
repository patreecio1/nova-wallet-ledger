using NovaWallet.BuildingBlocks.Application.Paging;
using NovaWallet.BuildingBlocks.Domain.Outbox;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Application.Abstractions;

public interface IWalletRepository
{
    Task<WalletAccount?> GetByIdAsync(Guid walletId, CancellationToken cancellationToken);

    void Add(WalletAccount wallet);

    /// <summary>
    /// Locks the given wallets with SELECT ... FOR UPDATE, always acquired in ascending Id
    /// order regardless of the order the ids are supplied in — this is what prevents two
    /// concurrent transfers between the same pair of wallets (in opposite directions) from
    /// deadlocking each other. Must be called from inside <see cref="ExecuteInTransactionAsync{TResult}"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, WalletAccount>> LockForUpdateAsync(IReadOnlyCollection<Guid> walletIds, CancellationToken cancellationToken);

    void AddLedgerEntry(LedgerEntry entry);

    void AddAuditEntry(AuditEntry entry);

    /// <summary>Written in the same transaction as the business change it describes — see OutboxMessage.</summary>
    void AddOutboxMessage(OutboxMessage message);

    /// <summary>The oldest <paramref name="batchSize"/> not-yet-published messages, for the dispatcher to attempt next.</summary>
    Task<IReadOnlyList<OutboxMessage>> GetPendingOutboxMessagesAsync(int batchSize, CancellationToken cancellationToken);

    Task<PagedResult<LedgerEntry>> GetStatementAsync(Guid walletId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Sum of TransferOut ledger entries for the wallet within [fromUtc, toUtc) — backs the WAT daily-limit check.</summary>
    Task<long> GetOutboundTotalAsync(Guid walletId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a database transaction wrapped in the
    /// provider's execution strategy (required because the DbContext is configured with
    /// EnableRetryOnFailure — a raw `BeginTransactionAsync` without this wrapper throws at
    /// runtime). Commits on success, rolls back on any exception. Keeps EF Core's
    /// transaction/retry machinery out of the Application layer entirely.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken);
}
