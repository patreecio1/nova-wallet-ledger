namespace NovaWallet.Wallet.Application.Abstractions;

/// <summary>Testable clock — handlers never call DateTime.UtcNow directly.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
