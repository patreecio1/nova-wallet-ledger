using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaWallet.Wallet.Application.Outbox;

namespace NovaWallet.Wallet.Infrastructure.Outbox;

/// <summary>
/// Polls for unpublished outbox messages every few seconds and hands them to
/// <see cref="OutboxProcessor"/>. A hosted service is a singleton, but <see cref="OutboxProcessor"/>
/// and the DbContext underneath it are scoped, so a fresh scope is created for every poll rather
/// than resolving them once at startup.
/// </summary>
public sealed class OutboxDispatcherHostedService(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcherHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                await processor.ProcessPendingAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A bad poll (e.g. a transient DB blip) must not kill the loop permanently —
                // the next poll tries again.
                logger.LogError(ex, "Outbox dispatcher iteration failed; will retry on the next poll");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }
}
