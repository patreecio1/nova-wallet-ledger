using FluentAssertions;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.Transfer;
using NovaWallet.Wallet.Domain;
using NSubstitute;

namespace NovaWallet.Wallet.UnitTests.Features;

public class TransferCommandHandlerTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly IIdempotencyStore _idempotencyStore = Substitute.For<IIdempotencyStore>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly TransferCommandHandler _handler;

    public TransferCommandHandlerTests()
    {
        _clock.UtcNow.Returns(UtcNow);

        // The handler wraps its business logic in ExecuteInTransactionAsync; for these unit
        // tests we just invoke the delegate directly rather than faking a real transaction.
        _repository
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<NovaWallet.BuildingBlocks.Domain.Results.Result<TransferResponse>>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, Task<NovaWallet.BuildingBlocks.Domain.Results.Result<TransferResponse>>>>()(CancellationToken.None));

        _idempotencyStore
            .TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyClaim(IdempotencyOutcome.Claimed));

        _handler = new TransferCommandHandler(_repository, _idempotencyStore, _clock);
    }

    private (WalletAccount Source, WalletAccount Destination) SeedWallets(long sourceBalanceKobo)
    {
        var source = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        if (sourceBalanceKobo > 0)
            source.Credit(sourceBalanceKobo);
        var destination = WalletAccount.Create(Guid.NewGuid(), UtcNow);

        _repository
            .LockForUpdateAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(source.Id) && ids.Contains(destination.Id)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, WalletAccount> { [source.Id] = source, [destination.Id] = destination });

        return (source, destination);
    }

    [Fact]
    public async Task Fails_with_insufficient_funds_when_amount_exceeds_balance()
    {
        var (source, destination) = SeedWallets(sourceBalanceKobo: 1_000_00);
        var command = new TransferCommand(source.CustomerId, source.Id, destination.Id, 2_000_00, null, "key-1");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WalletErrors.InsufficientFunds);
        source.BalanceKobo.Should().Be(1_000_00, "a rejected transfer must not touch the balance");
    }

    [Fact]
    public async Task Succeeds_and_moves_funds_between_wallets()
    {
        var (source, destination) = SeedWallets(sourceBalanceKobo: 5_000_00);
        var command = new TransferCommand(source.CustomerId, source.Id, destination.Id, 2_000_00, "rent", "key-2");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        source.BalanceKobo.Should().Be(3_000_00);
        destination.BalanceKobo.Should().Be(2_000_00);
        result.Value.AmountKobo.Should().Be(2_000_00);
    }

    [Fact]
    public async Task Fails_with_forbidden_when_caller_does_not_own_source_wallet()
    {
        var (source, destination) = SeedWallets(sourceBalanceKobo: 5_000_00);
        var someoneElse = Guid.NewGuid();
        var command = new TransferCommand(someoneElse, source.Id, destination.Id, 1_000_00, null, "key-3");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WalletErrors.Forbidden);
    }

    [Fact]
    public async Task Fails_when_source_wallet_is_not_found()
    {
        var destination = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        var missingSourceId = Guid.NewGuid();

        _repository
            .LockForUpdateAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, WalletAccount> { [destination.Id] = destination });

        var command = new TransferCommand(Guid.NewGuid(), missingSourceId, destination.Id, 1_000_00, null, "key-4");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WalletErrors.NotFound(missingSourceId).Code);
    }

    [Fact]
    public async Task Fails_with_daily_limit_exceeded_when_todays_outbound_total_would_be_breached()
    {
        var (source, destination) = SeedWallets(sourceBalanceKobo: 1_000_000_00);
        _repository
            .GetOutboundTotalAsync(source.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(WalletAccount.DefaultDailyOutboundLimitKobo - 100);

        var command = new TransferCommand(source.CustomerId, source.Id, destination.Id, 200, null, "key-5");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WalletErrors.DailyLimitExceeded);
    }

    [Fact]
    public async Task Replaying_the_same_idempotency_key_and_payload_returns_the_cached_result_without_touching_balances()
    {
        var (source, destination) = SeedWallets(sourceBalanceKobo: 5_000_00);
        var command = new TransferCommand(source.CustomerId, source.Id, destination.Id, 1_000_00, null, "replay-key");

        var first = await _handler.Handle(command, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Simulate the store now holding a completed record for that key/payload.
        var cachedPayload = GetLastCompletedPayload();
        _idempotencyStore
            .TryClaimAsync("replay-key", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyClaim(IdempotencyOutcome.Completed, cachedPayload));

        var replay = await _handler.Handle(command, CancellationToken.None);

        replay.IsSuccess.Should().BeTrue();
        replay.Value.Should().BeEquivalentTo(first.Value);
        source.BalanceKobo.Should().Be(4_000_00, "the replay must not debit the wallet a second time");
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_payload_is_rejected()
    {
        var (source, destination) = SeedWallets(sourceBalanceKobo: 5_000_00);
        _idempotencyStore
            .TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyClaim(IdempotencyOutcome.ReusedWithDifferentPayload));

        var command = new TransferCommand(source.CustomerId, source.Id, destination.Id, 1_000_00, null, "reused-key");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WalletErrors.IdempotencyKeyReusedWithDifferentPayload);
        source.BalanceKobo.Should().Be(5_000_00);
    }

    private string GetLastCompletedPayload()
    {
        var call = _idempotencyStore.ReceivedCalls()
            .Last(c => c.GetMethodInfo().Name == nameof(IIdempotencyStore.Complete));
        return (string)call.GetArguments()[1]!;
    }
}
