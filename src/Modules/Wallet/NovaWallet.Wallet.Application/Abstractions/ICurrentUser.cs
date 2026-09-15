namespace NovaWallet.Wallet.Application.Abstractions;

public enum CallerRole
{
    Customer = 1,
    System = 2,
}

/// <summary>The authenticated caller, resolved from JWT claims by the host (see CurrentUserService in NovaWallet.Api).</summary>
public interface ICurrentUser
{
    Guid CustomerId { get; }

    CallerRole Role { get; }
}
