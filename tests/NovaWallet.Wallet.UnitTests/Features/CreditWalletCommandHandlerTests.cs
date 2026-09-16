using FluentAssertions;
using NovaWallet.BuildingBlocks.Domain.Results;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Application.Features.CreditWallet;
using NovaWallet.Wallet.Domain;
using NSubstitute;

namespace NovaWallet.Wallet.UnitTests.Features;

public class CreditWalletCommandHandlerTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly CreditWalletCommandHandler _handler;

    public CreditWalletCommandHandlerTests()
    {
        _clock.UtcNow.Returns(UtcNow);

        // Same pattern as TransferCommandHandlerTests: run the transaction delegate directly
        // rather than faking a real database transaction.
        _repository
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Result<WalletResponse>>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, Task<Result<WalletResponse>>>>()(CancellationToken.None));

        _handler = new CreditWalletCommandHandler(_repository, _clock);
    }

    private WalletAccount SeedWallet(long startingBalanceKobo = 0)
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        if (startingBalanceKobo > 0)
            wallet.Credit(startingBalanceKobo);

        _repository
            .LockForUpdateAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(wallet.Id)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, WalletAccount> { [wallet.Id] = wallet });

        return wallet;
    }

    [Fact]
    public async Task System_role_can_credit_a_wallet()
    {
        var wallet = SeedWallet(startingBalanceKobo: 1_000_00);
        var command = new CreditWalletCommand(wallet.Id, 500_00, "seed", CallerRole.System);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BalanceKobo.Should().Be(1_500_00);
        wallet.BalanceKobo.Should().Be(1_500_00);
    }

    [Fact]
    public async Task Customer_role_cannot_credit_a_wallet()
    {
        var wallet = SeedWallet();
        var command = new CreditWalletCommand(wallet.Id, 500_00, "seed", CallerRole.Customer);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WalletErrors.Forbidden);
        wallet.BalanceKobo.Should().Be(0, "a forbidden request must never touch the balance");
    }

    [Fact]
    public async Task Fails_when_the_wallet_does_not_exist()
    {
        var missingWalletId = Guid.NewGuid();
        _repository
            .LockForUpdateAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, WalletAccount>());

        var command = new CreditWalletCommand(missingWalletId, 500_00, "seed", CallerRole.System);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WalletErrors.NotFound(missingWalletId).Code);
    }

    [Fact]
    public async Task Records_a_ledger_entry_and_an_audit_entry_for_the_credit()
    {
        var wallet = SeedWallet();
        var command = new CreditWalletCommand(wallet.Id, 250_00, "inbound NIP", CallerRole.System);

        await _handler.Handle(command, CancellationToken.None);

        _repository.Received(1).AddLedgerEntry(Arg.Is<LedgerEntry>(e =>
            e.WalletId == wallet.Id && e.Type == LedgerEntryType.Credit && e.AmountKobo == 250_00));
        _repository.Received(1).AddAuditEntry(Arg.Is<AuditEntry>(e =>
            e.WalletId == wallet.Id && e.EventType == "WalletCredited" && e.AmountKobo == 250_00));
    }
}
