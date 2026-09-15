using FluentAssertions;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.UnitTests.Domain;

public class WalletAccountTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_starts_with_zero_balance()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);

        wallet.BalanceKobo.Should().Be(0);
        wallet.DailyOutboundLimitKobo.Should().Be(WalletAccount.DefaultDailyOutboundLimitKobo);
    }

    [Fact]
    public void Create_rejects_empty_customer_id()
    {
        var act = () => WalletAccount.Create(Guid.Empty, UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Credit_increases_balance()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);

        wallet.Credit(10_000_00);

        wallet.BalanceKobo.Should().Be(10_000_00);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Credit_rejects_non_positive_amounts(long amount)
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);

        var act = () => wallet.Credit(amount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Debit_decreases_balance_when_sufficient_funds()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        wallet.Credit(10_000_00);

        wallet.Debit(4_000_00);

        wallet.BalanceKobo.Should().Be(6_000_00);
    }

    [Fact]
    public void Debit_never_allows_balance_to_go_negative()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        wallet.Credit(1_000_00);

        var act = () => wallet.Debit(1_000_01);

        act.Should().Throw<InvalidOperationException>();
        wallet.BalanceKobo.Should().Be(1_000_00, "a failed debit must not partially apply");
    }

    [Fact]
    public void Debit_of_exactly_the_full_balance_is_allowed()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        wallet.Credit(500_00);

        wallet.Debit(500_00);

        wallet.BalanceKobo.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Debit_rejects_non_positive_amounts(long amount)
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        wallet.Credit(1_000_00);

        var act = () => wallet.Debit(amount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
