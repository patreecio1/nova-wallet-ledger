namespace NovaWallet.BuildingBlocks.Domain.Outbox;

/// <summary>
/// A row in the transactional outbox: written in the exact same database transaction as the
/// business change that produced it (see TransferCommandHandler), so "the transfer happened"
/// and "an event describing it exists to be published" either both commit or both roll back —
/// there is no window where one happens without the other. A separate dispatcher (polling this
/// table, not driven by the request that created the row) publishes it later, at least once.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    private OutboxMessage(Guid id, string type, string content, DateTime occurredOnUtc)
    {
        Id = id;
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>A stable, human-readable event-type discriminator (not necessarily a .NET type name — just enough for a subscriber to know how to interpret Content).</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>The event, already serialized to JSON — the outbox itself doesn't need to know the shape.</summary>
    public string Content { get; private set; } = string.Empty;

    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>Null until a dispatcher successfully publishes this message.</summary>
    public DateTime? ProcessedOnUtc { get; private set; }

    /// <summary>The most recent publish failure, if any — cleared on success. Retained (not cleared to null) alongside a successful ProcessedOnUtc so a past failure stays visible for diagnostics.</summary>
    public string? Error { get; private set; }

    public static OutboxMessage Create(string type, string content, DateTime occurredOnUtc) =>
        new(Guid.NewGuid(), type, content, occurredOnUtc);

    public void MarkProcessed(DateTime utcNow) => ProcessedOnUtc = utcNow;

    public void MarkFailed(string error) => Error = error;
}
