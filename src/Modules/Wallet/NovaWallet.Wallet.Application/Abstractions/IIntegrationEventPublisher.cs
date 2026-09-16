namespace NovaWallet.Wallet.Application.Abstractions;

/// <summary>
/// Where an outbox message actually goes once the dispatcher picks it up. Deliberately just a
/// (type, JSON payload) pair rather than a generic method — the outbox table itself only ever
/// stores that shape (see OutboxMessage), so the publisher never needs to know about specific
/// event types either. A real deployment implements this against a message broker (Kafka, SQS,
/// Azure Service Bus) or an outbound webhook to another module (e.g. NovaLend); see
/// LoggingIntegrationEventPublisher for what this take-home actually wires up instead.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken);
}
