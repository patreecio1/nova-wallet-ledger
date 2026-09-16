using Microsoft.Extensions.Logging;
using NovaWallet.Wallet.Application.Abstractions;

namespace NovaWallet.Wallet.Infrastructure.Outbox;

/// <summary>
/// Stands in for a real message broker or webhook call (e.g. to NovaLend) — this take-home has
/// no such external system to actually integrate with, so "publish" means "log it, structurally,
/// so the plumbing end-to-end is real and demonstrable." Swapping this for a Kafka/SQS/Azure
/// Service Bus producer, or an outbound HTTP call, is the only change a real deployment needs —
/// OutboxProcessor and the dispatcher loop stay exactly as they are.
/// </summary>
public sealed class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken)
    {
        logger.LogInformation("Publishing integration event {EventType}: {Payload}", eventType, payload);
        return Task.CompletedTask;
    }
}
