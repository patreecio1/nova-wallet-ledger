namespace NovaWallet.Wallet.Domain;

/// <summary>
/// Backs the Idempotency-Key contract on the transfer endpoint. Keyed by the header value
/// itself (globally unique per client), guarded by a DB-level unique constraint so that two
/// concurrent requests carrying the same fresh key can't both "win" the insert — see
/// IIdempotencyStore.TryClaimAsync and its Postgres unique-violation handling in
/// WalletRepository.
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    private IdempotencyRecord(string key, string requestHash, DateTime createdAtUtc)
    {
        Key = key;
        RequestHash = requestHash;
        CreatedAtUtc = createdAtUtc;
    }

    public string Key { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    /// <summary>Null while the original request is still in flight (claimed but not yet completed).</summary>
    public string? ResponsePayload { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public static IdempotencyRecord Claim(string key, string requestHash, DateTime utcNow) => new(key, requestHash, utcNow);

    public void Complete(string responsePayload, DateTime utcNow)
    {
        ResponsePayload = responsePayload;
        CompletedAtUtc = utcNow;
    }
}
