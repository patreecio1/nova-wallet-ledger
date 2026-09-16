using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Wallet.API;
using NovaWallet.Wallet.Application.Features.Transfer;
using NovaWallet.Wallet.Infrastructure.Persistence;

namespace NovaWallet.IntegrationTests;

/// <summary>
/// Proves the transactional outbox end-to-end against the real host: a successful transfer
/// writes a TransferCompleted message in the same transaction, and the real
/// OutboxDispatcherHostedService (running inside this WebApplicationFactory instance, not
/// mocked or invoked manually) picks it up and publishes it shortly after.
/// </summary>
public class OutboxTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;

    public OutboxTests(WalletApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_successful_transfer_writes_an_outbox_message_that_the_background_dispatcher_publishes()
    {
        using var setupClient = _factory.CreateClient();
        var sourceToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var destinationToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var systemToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid(), role: "system");

        var sourceWallet = await _factory.CreateWalletAsync(sourceToken);
        var destinationWallet = await _factory.CreateWalletAsync(destinationToken);
        await _factory.CreditWalletAsync(systemToken, sourceWallet.WalletId, 1_000_00);

        using var client = _factory.CreateAuthenticatedClient(sourceToken);
        var request = new TransferRequest(sourceWallet.WalletId, destinationWallet.WalletId, 250_00, "outbox proof");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(request) };
        httpRequest.Headers.Add("Idempotency-Key", $"outbox-test-{Guid.NewGuid()}");
        var response = await client.SendAsync(httpRequest);
        await WalletApiFactory.EnsureSuccessAsync(response);
        var transfer = (await response.Content.ReadFromJsonAsync<TransferResponse>())!;

        // The message must exist immediately, in the same transaction as the transfer -- no
        // waiting needed to observe this part.
        var messageRightAfterTransfer = await GetOutboxMessageForTransferAsync(transfer.TransferId);
        messageRightAfterTransfer.Should().NotBeNull();
        messageRightAfterTransfer!.Type.Should().Be("TransferCompletedIntegrationEvent");
        messageRightAfterTransfer.Content.Should().Contain(transfer.TransferId.ToString());

        // The real background dispatcher polls every 5 seconds -- give it a window to pick this
        // up rather than asserting instantly or sleeping the full worst case blindly.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        OutboxMessageSnapshot? processed = null;
        while (DateTime.UtcNow < deadline)
        {
            var current = await GetOutboxMessageForTransferAsync(transfer.TransferId);
            if (current?.ProcessedOnUtc is not null)
            {
                processed = current;
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        processed.Should().NotBeNull("the real OutboxDispatcherHostedService running in this host should have published and marked it processed within 15 seconds");
    }

    private sealed record OutboxMessageSnapshot(string Type, string Content, DateTime? ProcessedOnUtc);

    private async Task<OutboxMessageSnapshot?> GetOutboxMessageForTransferAsync(Guid transferId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // Filtering on Content (a jsonb column) client-side rather than via a SQL LIKE, which
        // Postgres doesn't support directly against jsonb without an explicit text cast --
        // fine here since a test only ever has a handful of these rows to scan.
        var candidates = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == "TransferCompletedIntegrationEvent")
            .ToListAsync();

        var message = candidates.SingleOrDefault(m => m.Content.Contains(transferId.ToString()));

        return message is null ? null : new OutboxMessageSnapshot(message.Type, message.Content, message.ProcessedOnUtc);
    }
}
