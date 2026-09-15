using NovaWallet.BuildingBlocks.Domain.Results;

namespace NovaWallet.Wallet.Domain;

public static class WalletErrors
{
    public static Error NotFound(Guid walletId) =>
        Error.NotFound("Wallet.NotFound", $"Wallet '{walletId}' was not found.");

    public static readonly Error InvalidAmount =
        Error.Validation("Wallet.InvalidAmount", "Amount must be a positive number of kobo.");

    public static readonly Error InsufficientFunds =
        Error.Conflict("Wallet.InsufficientFunds", "The wallet does not have enough balance for this transfer.");

    public static readonly Error DailyLimitExceeded =
        Error.Conflict("Wallet.DailyLimitExceeded", "This transfer would exceed the wallet's daily outbound transfer limit.");

    public static readonly Error SameWalletTransfer =
        Error.Validation("Wallet.SameWalletTransfer", "Source and destination wallet cannot be the same.");

    public static readonly Error ConcurrentUpdateConflict =
        Error.Conflict("Wallet.ConcurrentUpdateConflict", "The wallet was modified concurrently. Please retry.");

    public static readonly Error IdempotencyKeyRequired =
        Error.Validation("Wallet.IdempotencyKeyRequired", "The Idempotency-Key header is required for this operation.");

    public static readonly Error IdempotencyKeyReusedWithDifferentPayload =
        Error.Conflict("Wallet.IdempotencyKeyReusedWithDifferentPayload", "This Idempotency-Key was already used with a different request payload.");

    public static readonly Error IdempotentRequestInFlight =
        Error.Conflict("Wallet.IdempotentRequestInFlight", "A request with this Idempotency-Key is already being processed.");

    public static readonly Error Forbidden =
        Error.Forbidden("Wallet.Forbidden", "You do not have access to this wallet.");
}
