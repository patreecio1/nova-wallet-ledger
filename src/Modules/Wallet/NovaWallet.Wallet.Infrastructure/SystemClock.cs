using NovaWallet.Wallet.Application.Abstractions;

namespace NovaWallet.Wallet.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
