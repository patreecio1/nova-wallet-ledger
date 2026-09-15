using NovaWallet.BuildingBlocks.Domain.Primitives;

namespace NovaWallet.Wallet.Domain;

public enum LedgerEntryType
{
    Credit = 1,
    TransferOut = 2,
    TransferIn = 3,
}

/// <summary>
/// One line of a wallet's statement (GET /wallets/{id}/statement). This is the queryable,
/// customer-facing transaction history — distinct from <see cref="AuditEntry"/>, which is
/// the internal, immutable, every-mutation-including-failed-attempts trail.
/// </summary>
public sealed class LedgerEntry : Entity
{
    private LedgerEntry() { }

    private LedgerEntry(
        Guid id,
        Guid walletId,
        LedgerEntryType type,
        long amountKobo,
        long balanceAfterKobo,
        Guid? counterpartyWalletId,
        Guid transferGroupId,
        string description,
        DateTime createdAtUtc) : base(id)
    {
        WalletId = walletId;
        Type = type;
        AmountKobo = amountKobo;
        BalanceAfterKobo = balanceAfterKobo;
        CounterpartyWalletId = counterpartyWalletId;
        TransferGroupId = transferGroupId;
        Description = description;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WalletId { get; private set; }

    public LedgerEntryType Type { get; private set; }

    public long AmountKobo { get; private set; }

    public long BalanceAfterKobo { get; private set; }

    public Guid? CounterpartyWalletId { get; private set; }

    /// <summary>Correlates the two rows (debit + credit) that make up one transfer, or a credit's own id.</summary>
    public Guid TransferGroupId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public static LedgerEntry ForCredit(Guid walletId, long amountKobo, long balanceAfterKobo, Guid creditId, string description, DateTime utcNow) =>
        new(Guid.NewGuid(), walletId, LedgerEntryType.Credit, amountKobo, balanceAfterKobo, counterpartyWalletId: null, creditId, description, utcNow);

    public static LedgerEntry ForTransferOut(Guid walletId, Guid counterpartyWalletId, long amountKobo, long balanceAfterKobo, Guid transferId, string description, DateTime utcNow) =>
        new(Guid.NewGuid(), walletId, LedgerEntryType.TransferOut, amountKobo, balanceAfterKobo, counterpartyWalletId, transferId, description, utcNow);

    public static LedgerEntry ForTransferIn(Guid walletId, Guid counterpartyWalletId, long amountKobo, long balanceAfterKobo, Guid transferId, string description, DateTime utcNow) =>
        new(Guid.NewGuid(), walletId, LedgerEntryType.TransferIn, amountKobo, balanceAfterKobo, counterpartyWalletId, transferId, description, utcNow);
}
