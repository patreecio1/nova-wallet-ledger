namespace NovaWallet.Wallet.API;

public sealed record CreateWalletRequest(Guid? CustomerId);

public sealed record CreditWalletRequest(long AmountKobo, string Reference);

public sealed record TransferRequest(Guid SourceWalletId, Guid DestinationWalletId, long AmountKobo, string? Description);
