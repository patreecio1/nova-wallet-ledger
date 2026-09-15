using NovaWallet.BuildingBlocks.Domain.Primitives;

namespace NovaWallet.Wallet.Domain;

/// <summary>
/// Append-only, immutable record of every balance mutation — separate from
/// <see cref="LedgerEntry"/> so it can be retained/queried independently of the customer-facing
/// statement and can never be edited by application code (no Update/Delete is exposed anywhere
/// in the repository or DbContext for this table).
/// </summary>
public sealed class AuditEntry : Entity
{
    private AuditEntry() { }

    private AuditEntry(
        Guid id,
        Guid walletId,
        string eventType,
        long amountKobo,
        long balanceBeforeKobo,
        long balanceAfterKobo,
        Guid? actorCustomerId,
        Guid correlationId,
        DateTime occurredAtUtc) : base(id)
    {
        WalletId = walletId;
        EventType = eventType;
        AmountKobo = amountKobo;
        BalanceBeforeKobo = balanceBeforeKobo;
        BalanceAfterKobo = balanceAfterKobo;
        ActorCustomerId = actorCustomerId;
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid WalletId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public long AmountKobo { get; private set; }

    public long BalanceBeforeKobo { get; private set; }

    public long BalanceAfterKobo { get; private set; }

    public Guid? ActorCustomerId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static AuditEntry Create(
        Guid walletId,
        string eventType,
        long amountKobo,
        long balanceBeforeKobo,
        long balanceAfterKobo,
        Guid? actorCustomerId,
        Guid correlationId,
        DateTime utcNow) =>
        new(Guid.NewGuid(), walletId, eventType, amountKobo, balanceBeforeKobo, balanceAfterKobo, actorCustomerId, correlationId, utcNow);
}
