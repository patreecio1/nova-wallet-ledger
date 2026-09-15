using NovaWallet.BuildingBlocks.Domain.Primitives;

namespace NovaWallet.Wallet.Domain;

/// <summary>
/// A customer's NGN wallet. This is the aggregate boundary: the only way a balance
/// changes is through <see cref="Credit"/> / <see cref="Debit"/>, both of which enforce
/// "never negative" as an invariant of the aggregate itself, not just of the caller's checks.
/// Business rules that need information outside the aggregate (available balance vs. daily
/// limit already spent) are decided by the application handler *before* calling these methods,
/// under a row lock — see TransferWallet handler / IWalletRepository.LockForUpdateAsync.
/// </summary>
public sealed class WalletAccount : AggregateRoot
{
    public const string Currency = "NGN";
    public const long DefaultDailyOutboundLimitKobo = 500_000_00; // NGN 500,000.00

    private WalletAccount() { }

    private WalletAccount(Guid id, Guid customerId, long dailyOutboundLimitKobo, DateTime createdAtUtc) : base(id)
    {
        CustomerId = customerId;
        BalanceKobo = 0;
        DailyOutboundLimitKobo = dailyOutboundLimitKobo;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid CustomerId { get; private set; }

    public long BalanceKobo { get; private set; }

    public long DailyOutboundLimitKobo { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static WalletAccount Create(Guid customerId, DateTime utcNow, long dailyOutboundLimitKobo = DefaultDailyOutboundLimitKobo)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (dailyOutboundLimitKobo <= 0)
            throw new ArgumentOutOfRangeException(nameof(dailyOutboundLimitKobo), "Daily outbound limit must be positive.");

        return new WalletAccount(Guid.NewGuid(), customerId, dailyOutboundLimitKobo, utcNow);
    }

    /// <summary>Increases the balance. Used both for inbound NIP credits and for the receiving side of a transfer.</summary>
    public void Credit(long amountKobo)
    {
        if (amountKobo <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountKobo), "Credit amount must be positive.");

        BalanceKobo = checked(BalanceKobo + amountKobo);
    }

    /// <summary>
    /// Decreases the balance. Throws if this would make the balance negative — this is the
    /// last line of defense for the "never negative" hard constraint, even if a caller's
    /// pre-check (e.g. after a stale read) somehow got it wrong.
    /// </summary>
    public void Debit(long amountKobo)
    {
        if (amountKobo <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountKobo), "Debit amount must be positive.");
        if (amountKobo > BalanceKobo)
            throw new InvalidOperationException("Debit would make the wallet balance negative.");

        BalanceKobo -= amountKobo;
    }
}
