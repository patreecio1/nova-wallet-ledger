using FluentAssertions;
using NovaWallet.Wallet.Application.Features.Transfer;

namespace NovaWallet.Wallet.UnitTests.Features;

public class TransferCommandValidatorTests
{
    private readonly TransferCommandValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        var command = new TransferCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100_00, "desc", "key");

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Same_source_and_destination_wallet_is_rejected()
    {
        var walletId = Guid.NewGuid();
        var command = new TransferCommand(Guid.NewGuid(), walletId, walletId, 100_00, null, "key");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_amount_is_rejected(long amount)
    {
        var command = new TransferCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, null, "key");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Missing_idempotency_key_is_rejected()
    {
        var command = new TransferCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100_00, null, string.Empty);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
