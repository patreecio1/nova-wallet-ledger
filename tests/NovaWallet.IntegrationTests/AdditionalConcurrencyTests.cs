using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NovaWallet.Wallet.API;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.IntegrationTests;

/// <summary>
/// Covers the concurrency scenarios <see cref="TransferConcurrencyTests"/> does not: concurrent
/// credits to a single wallet (not just transfers), opposite-direction transfers between the
/// same pair of wallets (the specific scenario the ascending-Id lock order exists to prevent
/// from deadlocking), and the WAT daily outbound limit under concurrent load.
/// </summary>
public class AdditionalConcurrencyTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;

    public AdditionalConcurrencyTests(WalletApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Concurrent_credits_to_the_same_wallet_all_land_without_losing_any()
    {
        const int creditAmountKobo = 40_00;
        const int concurrentCreditCount = 25;

        using var setupClient = _factory.CreateClient();
        var ownerToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var systemToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid(), role: "system");
        var wallet = await _factory.CreateWalletAsync(ownerToken);

        // Every credit is its own independent inbound settlement (own reference), all racing
        // to lock and update the exact same wallet row at once.
        var attempts = Enumerable.Range(0, concurrentCreditCount).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(systemToken);
            return await client.PostAsJsonAsync($"/api/wallets/{wallet.WalletId}/credit",
                new { amountKobo = creditAmountKobo, reference = $"inbound-nip-{i}" });
        });

        var responses = await Task.WhenAll(attempts);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK, "no legitimate credit should ever be rejected");

        var finalBalance = await _factory.GetBalanceAsync(ownerToken, wallet.WalletId);
        finalBalance.BalanceKobo.Should().Be(concurrentCreditCount * (long)creditAmountKobo, "every one of the 25 concurrent credits must be reflected — none lost to a lost update");
    }

    [Fact]
    public async Task Opposite_direction_transfers_between_the_same_pair_never_deadlock_and_balances_reconcile()
    {
        const int transferAmountKobo = 50_00;
        const int transfersPerDirection = 15;

        using var setupClient = _factory.CreateClient();
        var tokenA = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var tokenB = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var systemToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid(), role: "system");

        var walletA = await _factory.CreateWalletAsync(tokenA);
        var walletB = await _factory.CreateWalletAsync(tokenB);

        // Each wallet is funded for exactly its own outgoing load, independent of how fast the
        // opposite direction's credits arrive -- so this proves the locking/ordering mechanism
        // itself, not just that there happened to be enough float to cover any interleaving.
        var startingBalanceKobo = transfersPerDirection * (long)transferAmountKobo;
        await _factory.CreditWalletAsync(systemToken, walletA.WalletId, startingBalanceKobo);
        await _factory.CreditWalletAsync(systemToken, walletB.WalletId, startingBalanceKobo);

        var aToB = Enumerable.Range(0, transfersPerDirection).Select(i => SendTransferAsync(tokenA, walletA.WalletId, walletB.WalletId, transferAmountKobo, $"a-to-b-{i}"));
        var bToA = Enumerable.Range(0, transfersPerDirection).Select(i => SendTransferAsync(tokenB, walletB.WalletId, walletA.WalletId, transferAmountKobo, $"b-to-a-{i}"));

        // Interleave both directions in one Task.WhenAll so they race against Postgres's
        // FOR UPDATE locks at the same time, in both directions, at once.
        var responses = await Task.WhenAll(aToB.Concat(bToA));

        // If the ascending-Id lock order were wrong (e.g. "always lock the caller's own wallet
        // first"), this specific back-and-forth pattern is exactly what would deadlock: A's
        // request locks A then wants B while B's request locks B and wants A. A real deadlock
        // here would surface as a 500 (an unhandled PostgresException), not a clean 200/409.
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK, "correct lock ordering means every one of these (fully funded) transfers succeeds -- none should time out, deadlock, or 500");

        var finalA = await _factory.GetBalanceAsync(tokenA, walletA.WalletId);
        var finalB = await _factory.GetBalanceAsync(tokenB, walletB.WalletId);

        finalA.BalanceKobo.Should().Be(startingBalanceKobo, "equal amounts flowed out and back in, in both directions");
        finalB.BalanceKobo.Should().Be(startingBalanceKobo);
    }

    [Fact]
    public async Task Concurrent_transfers_that_would_exceed_the_daily_outbound_limit_only_let_the_limit_through()
    {
        const long transferAmountKobo = 50_000_00; // NGN 50,000
        var affordableCount = (int)(WalletAccount.DefaultDailyOutboundLimitKobo / transferAmountKobo); // 10 at the default NGN 500,000 limit
        var attemptedCount = affordableCount * 2;

        using var setupClient = _factory.CreateClient();
        var sourceToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var destinationToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid());
        var systemToken = await _factory.IssueTokenAsync(setupClient, Guid.NewGuid(), role: "system");

        var sourceWallet = await _factory.CreateWalletAsync(sourceToken);
        var destinationWallet = await _factory.CreateWalletAsync(destinationToken);

        // Fund the wallet far above the daily limit so InsufficientFunds can never be the
        // reason a request fails here -- only the WAT daily-limit check can be.
        await _factory.CreditWalletAsync(systemToken, sourceWallet.WalletId, WalletAccount.DefaultDailyOutboundLimitKobo * 10);

        var attempts = Enumerable.Range(0, attemptedCount)
            .Select(i => SendTransferAsync(sourceToken, sourceWallet.WalletId, destinationWallet.WalletId, transferAmountKobo, $"limit-test-{i}"));

        var responses = await Task.WhenAll(attempts);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        successCount.Should().Be(affordableCount, "exactly enough transfers to reach the daily cap should succeed, no more");

        var failedBodies = await Task.WhenAll(responses
            .Where(r => r.StatusCode != HttpStatusCode.OK)
            .Select(r => r.Content.ReadFromJsonAsync<ProblemDetailsBody>()));

        failedBodies.Should().OnlyContain(b => b!.Title == "Wallet.DailyLimitExceeded", "with balance never the constraint, every rejection here must specifically be the daily-limit check, not insufficient funds");

        var finalSource = await _factory.GetBalanceAsync(sourceToken, sourceWallet.WalletId);
        finalSource.BalanceKobo.Should().Be(WalletAccount.DefaultDailyOutboundLimitKobo * 10 - WalletAccount.DefaultDailyOutboundLimitKobo, "exactly one day's worth of limit was spent, not more");
    }

    private async Task<HttpResponseMessage> SendTransferAsync(string callerToken, Guid sourceWalletId, Guid destinationWalletId, long amountKobo, string idempotencyKeySuffix)
    {
        using var client = _factory.CreateAuthenticatedClient(callerToken);
        var request = new TransferRequest(sourceWalletId, destinationWalletId, amountKobo, idempotencyKeySuffix);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(request) };
        httpRequest.Headers.Add("Idempotency-Key", $"{idempotencyKeySuffix}-{Guid.NewGuid()}");
        return await client.SendAsync(httpRequest);
    }

    private sealed record ProblemDetailsBody(string? Title, string? Detail);
}
