using FluentAssertions;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Domain;
using NSubstitute;

namespace NovaWallet.Wallet.UnitTests.Features;

public class CreateWalletCommandHandlerTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly CreateWalletCommandHandler _handler;

    public CreateWalletCommandHandlerTests()
    {
        _clock.UtcNow.Returns(UtcNow);
        _handler = new CreateWalletCommandHandler(_repository, _clock);
    }

    [Fact]
    public async Task Creates_a_wallet_with_zero_balance_for_the_given_customer()
    {
        var customerId = Guid.NewGuid();
        var command = new CreateWalletCommand(customerId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerId.Should().Be(customerId);
        result.Value.BalanceKobo.Should().Be(0);
        result.Value.Currency.Should().Be(WalletAccount.Currency);
        result.Value.DailyOutboundLimitKobo.Should().Be(WalletAccount.DefaultDailyOutboundLimitKobo);
    }

    [Fact]
    public async Task Adds_the_new_wallet_to_the_repository_and_saves_it()
    {
        var command = new CreateWalletCommand(Guid.NewGuid());

        await _handler.Handle(command, CancellationToken.None);

        _repository.Received(1).Add(Arg.Is<WalletAccount>(w => w.CustomerId == command.CustomerId));
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
