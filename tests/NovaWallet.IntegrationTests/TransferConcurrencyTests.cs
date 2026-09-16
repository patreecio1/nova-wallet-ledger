using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NovaWallet.Wallet.API;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Application.Features.Transfer;

namespace NovaWallet.IntegrationTests;

/// <summary>
/// The hard constraint this take-home cares about most: a wallet's balance must never go
/// negative and money must never be duplicated under concurrent load. These tests hit the real
/// host, over real HTTP, against a real Postgres instance — no mocking of the concurrency
/// control itself.
/// </summary>
public class TransferConcurrencyTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;

    public TransferConcurrencyTests(WalletApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Concurrent_transfers_that_would_overdraw_the_wallet_only_let_the_affordable_ones_through()
    {
        const int transferAmountKobo = 100_00; // NGN 100
        const int affordableTransferCount = 10;
        const int attemptedTransferCount = 20; // double what the wallet can actually afford
        var startingBalanceKobo = affordableTransferCount * transferAmountKobo;

        using var setupClient = _factory.CreateClient();
        var sourceCustomerId = Guid.NewGuid();
        var destinationCustomerId = Guid.NewGuid();

        var sourceToken = await _factory.IssueTokenAsync(setupClient, sourceCustomerId);
        var destinationToken = await _factory.IssueTokenAsync(setupClient, destinationCustomerId);
        var systemToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid(), role: "system");

        var sourceWallet = await _factory.CreateWalletAsync(sourceToken);
        var destinationWallet = await _factory.CreateWalletAsync(destinationToken);
        await _factory.CreditWalletAsync(systemToken, sourceWallet.WalletId, startingBalanceKobo);

        // Fire every transfer attempt at once, each with its own Idempotency-Key (these are
        // twenty independent transfer requests, not retries of one request) and each on its
        // own HttpClient/connection to maximize real concurrency against the same wallet row.
        var attempts = Enumerable.Range(0, attemptedTransferCount).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(sourceToken);
            var request = new TransferRequest(sourceWallet.WalletId, destinationWallet.WalletId, transferAmountKobo, $"attempt-{i}");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(request) };
            httpRequest.Headers.Add("Idempotency-Key", $"concurrency-test-{Guid.NewGuid()}");
            return await client.SendAsync(httpRequest);
        });

        var responses = await Task.WhenAll(attempts);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var insufficientFundsCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(affordableTransferCount, "only as many transfers as the starting balance covers should ever succeed");
        insufficientFundsCount.Should().Be(attemptedTransferCount - affordableTransferCount);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);

        var finalSource = await _factory.GetBalanceAsync(sourceToken, sourceWallet.WalletId);
        var finalDestination = await _factory.GetBalanceAsync(destinationToken, destinationWallet.WalletId);

        finalSource.BalanceKobo.Should().Be(0, "the source wallet must land at exactly zero, never negative");
        finalDestination.BalanceKobo.Should().Be(startingBalanceKobo, "every kobo debited from the source must show up on the destination — none lost, none duplicated");
    }

    [Fact]
    public async Task Concurrent_replays_of_the_same_idempotency_key_only_apply_the_transfer_once()
    {
        const int transferAmountKobo = 250_00;
        const int concurrentReplayCount = 10;

        using var setupClient = _factory.CreateClient();
        var sourceToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var destinationToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var systemToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid(), role: "system");

        var sourceWallet = await _factory.CreateWalletAsync(sourceToken);
        var destinationWallet = await _factory.CreateWalletAsync(destinationToken);
        await _factory.CreditWalletAsync(systemToken, sourceWallet.WalletId, transferAmountKobo * 5);

        var idempotencyKey = $"replay-test-{Guid.NewGuid()}";
        var request = new TransferRequest(sourceWallet.WalletId, destinationWallet.WalletId, transferAmountKobo, "rent");

        var attempts = Enumerable.Range(0, concurrentReplayCount).Select(async _ =>
        {
            using var client = _factory.CreateAuthenticatedClient(sourceToken);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(request) };
            httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);
            return await client.SendAsync(httpRequest);
        });

        var responses = await Task.WhenAll(attempts);

        // A request that raced the very first claim can legitimately see 409 "already
        // processing" — that is a client-retryable outcome, not data corruption. What must
        // never happen is more than one distinct successful transfer.
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);

        var successfulBodies = await Task.WhenAll(responses
            .Where(r => r.StatusCode == HttpStatusCode.OK)
            .Select(r => r.Content.ReadFromJsonAsync<TransferResponse>()));

        successfulBodies.Should().NotBeEmpty();
        successfulBodies.Select(b => b!.TransferId).Distinct().Should().ContainSingle("every successful reply must describe the same, single transfer");

        var finalSource = await _factory.GetBalanceAsync(sourceToken, sourceWallet.WalletId);
        finalSource.BalanceKobo.Should().Be(transferAmountKobo * 5 - transferAmountKobo, "the replayed transfer must debit the wallet exactly once");
    }
}
