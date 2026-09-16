using Microsoft.Extensions.Logging;
using NovaWallet.Wallet.Application.Abstractions;

namespace NovaWallet.Wallet.Application.Outbox;

/// <summary>
/// Publishes pending outbox messages one batch at a time. Deliberately separated from whatever
/// drives it on a schedule (see OutboxDispatcherHostedService in Infrastructure) so the actual
/// "read pending, publish, mark processed or record the failure" logic is unit-testable without
/// a real background timer.
/// </summary>
public sealed class OutboxProcessor(IWalletRepository repository, IIntegrationEventPublisher publisher, IClock clock, ILogger<OutboxProcessor> logger)
{
    public async Task<int> ProcessPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await repository.GetPendingOutboxMessagesAsync(batchSize, cancellationToken);
        if (pending.Count == 0)
            return 0;

        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message.Type, message.Content, cancellationToken);
                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception ex)
            {
                // One message failing to publish must never stop the rest of the batch, and
                // must never throw out of this loop — it stays unprocessed and is retried on
                // the next poll, same as any other message that hasn't been picked up yet.
                message.MarkFailed(ex.Message);
                logger.LogWarning(ex, "Failed to publish outbox message {MessageId} ({Type}); will retry on the next poll", message.Id, message.Type);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}
