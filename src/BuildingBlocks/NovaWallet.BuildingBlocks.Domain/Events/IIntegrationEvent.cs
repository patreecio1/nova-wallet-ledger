namespace NovaWallet.BuildingBlocks.Domain.Events;

/// <summary>
/// Cross-boundary event — something another module, or a system outside this service entirely,
/// would want to know about (e.g. NovaLend's credit scoring reacting to a completed transfer).
/// Unlike <see cref="IDomainEvent"/>, this is never handled in-process/same-transaction; it is
/// written to the outbox alongside the state change that caused it and published later, at least
/// once, by a separate dispatcher — see BuildingBlocks.Domain.Outbox.OutboxMessage.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }

    DateTime OccurredOnUtc { get; }
}
