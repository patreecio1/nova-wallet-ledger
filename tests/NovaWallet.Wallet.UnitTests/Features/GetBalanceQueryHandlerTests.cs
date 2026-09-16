using FluentAssertions;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.GetBalance;
using NovaWallet.Wallet.Domain;
using NSubstitute;

namespace NovaWallet.Wallet.UnitTests.Features;

public class GetBalanceQueryHandlerTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly GetBalanceQueryHandler _handler;

    public GetBalanceQueryHandlerTests() => _handler = new GetBalanceQueryHandler(_repository);

    [Fact]
    public async Task Owner_can_read_their_own_balance()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        wallet.Credit(750_00);
        _repository.GetByIdAsync(wallet.Id, Arg.Any<CancellationToken>()).Returns(wallet);

        var query = new GetBalanceQuery(wallet.Id, wallet.CustomerId, CallerRole.Customer);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BalanceKobo.Should().Be(750_00);
    }

    [Fact]
    public async Task A_different_customer_cannot_read_someone_elses_balance()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        _repository.GetByIdAsync(wallet.Id, Arg.Any<CancellationToken>()).Returns(wallet);

        var query = new GetBalanceQuery(wallet.Id, Guid.NewGuid(), CallerRole.Customer);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WalletErrors.Forbidden);
    }

    [Fact]
    public async Task System_role_can_read_any_wallets_balance()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        _repository.GetByIdAsync(wallet.Id, Arg.Any<CancellationToken>()).Returns(wallet);

        var query = new GetBalanceQuery(wallet.Id, Guid.NewGuid(), CallerRole.System);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_when_the_wallet_does_not_exist()
    {
        var missingWalletId = Guid.NewGuid();
        _repository.GetByIdAsync(missingWalletId, Arg.Any<CancellationToken>()).Returns((WalletAccount?)null);

        var query = new GetBalanceQuery(missingWalletId, Guid.NewGuid(), CallerRole.Customer);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WalletErrors.NotFound(missingWalletId).Code);
    }
}
