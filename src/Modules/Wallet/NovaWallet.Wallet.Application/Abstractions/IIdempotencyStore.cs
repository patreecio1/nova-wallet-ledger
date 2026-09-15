namespace NovaWallet.Wallet.Application.Abstractions;

public enum IdempotencyOutcome
{
    /// <summary>Fresh key — the caller should proceed and later call CompleteAsync.</summary>
    Claimed,

    /// <summary>Same key, different request payload — must be rejected, never processed.</summary>
    ReusedWithDifferentPayload,

    /// <summary>Same key, same payload, a previous attempt is still processing — do not reprocess.</summary>
    InFlight,

    /// <summary>Same key, same payload, already finished — replay the cached response verbatim.</summary>
    Completed,
}

public sealed record IdempotencyClaim(IdempotencyOutcome Outcome, string? CachedResponsePayload = null);

/// <summary>
/// Backs the Idempotency-Key header contract on the transfer endpoint. All methods must be
/// called from inside the same transaction as the business operation they guard, so that
/// "claim the key" and "apply the transfer" succeed or fail together. Only ever stores/returns
/// an opaque JSON payload — HTTP status-code mapping stays entirely in the API layer.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(string key, string requestHash, CancellationToken cancellationToken);

    void Complete(string key, string responsePayload);
}
