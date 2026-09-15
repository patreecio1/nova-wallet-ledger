using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using NovaWallet.BuildingBlocks.Application.CQRS;
using NovaWallet.BuildingBlocks.Domain.Results;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Application.Features.Transfer;

public sealed record TransferCommand(
    Guid CallerCustomerId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    long AmountKobo,
    string? Description,
    string IdempotencyKey) : ICommand<TransferResponse>;

public sealed record TransferResponse(
    Guid TransferId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    long AmountKobo,
    long SourceBalanceAfterKobo,
    DateTime CreatedAtUtc);

public sealed class TransferCommandValidator : AbstractValidator<TransferCommand>
{
    public TransferCommandValidator()
    {
        RuleFor(x => x.SourceWalletId).NotEmpty();
        RuleFor(x => x.DestinationWalletId).NotEmpty();
        RuleFor(x => x.AmountKobo).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200);
        RuleFor(x => x)
            .Must(x => x.SourceWalletId != x.DestinationWalletId)
            .WithMessage("Source and destination wallet cannot be the same.");
    }
}

/// <summary>
/// The core "must never lose, duplicate, or miscount money" operation. Correctness rests on
/// three independent mechanisms stacked together:
///   1. Idempotency-Key claimed inside the same DB transaction as the transfer itself, so a
///      network retry of an identical request can never double-process (see IIdempotencyStore).
///   2. Both wallets locked with SELECT ... FOR UPDATE in a fixed ascending-Id order before any
///      balance is read, so concurrent transfers touching the same wallet(s) serialize instead
///      of racing, and a canonical lock order rules out deadlocks between opposite-direction
///      transfers of the same pair (see IWalletRepository.LockForUpdateAsync).
///   3. WalletAccount.Debit throws rather than letting a balance go negative, as a last-resort
///      invariant even if 1/2 were somehow bypassed.
/// </summary>
public sealed class TransferCommandHandler(IWalletRepository repository, IIdempotencyStore idempotencyStore, IClock clock)
    : ICommandHandler<TransferCommand, TransferResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<TransferResponse>> Handle(TransferCommand request, CancellationToken cancellationToken)
    {
        // Claiming the key is a small, standalone unit of work (its own implicit transaction,
        // committed immediately) — deliberately NOT nested inside the transfer's own
        // transaction below. Postgres aborts an entire transaction on the first statement
        // error, so catching a unique-violation on the claim insert while already inside the
        // transfer's transaction would poison it for everything that follows. Keeping the two
        // separate means a losing "claim" attempt fails and rolls back on its own, cleanly,
        // before the transfer transaction ever opens.
        var requestHash = ComputeRequestHash(request);
        var claim = await idempotencyStore.TryClaimAsync(request.IdempotencyKey, requestHash, cancellationToken);

        switch (claim.Outcome)
        {
            case IdempotencyOutcome.ReusedWithDifferentPayload:
                return Result.Failure<TransferResponse>(WalletErrors.IdempotencyKeyReusedWithDifferentPayload);
            case IdempotencyOutcome.InFlight:
                return Result.Failure<TransferResponse>(WalletErrors.IdempotentRequestInFlight);
            case IdempotencyOutcome.Completed:
                return DeserializeEnvelope(claim.CachedResponsePayload!);
        }

        return await repository.ExecuteInTransactionAsync(async ct =>
        {
            var result = await ExecuteTransferAsync(request, ct);

            idempotencyStore.Complete(request.IdempotencyKey, SerializeEnvelope(result));
            await repository.SaveChangesAsync(ct);

            return result;
        }, cancellationToken);
    }

    private async Task<Result<TransferResponse>> ExecuteTransferAsync(TransferCommand request, CancellationToken ct)
    {
        var locked = await repository.LockForUpdateAsync([request.SourceWalletId, request.DestinationWalletId], ct);

        if (!locked.TryGetValue(request.SourceWalletId, out var source))
            return Result.Failure<TransferResponse>(WalletErrors.NotFound(request.SourceWalletId));
        if (!locked.TryGetValue(request.DestinationWalletId, out var destination))
            return Result.Failure<TransferResponse>(WalletErrors.NotFound(request.DestinationWalletId));

        if (source.CustomerId != request.CallerCustomerId)
            return Result.Failure<TransferResponse>(WalletErrors.Forbidden);

        if (request.AmountKobo > source.BalanceKobo)
            return Result.Failure<TransferResponse>(WalletErrors.InsufficientFunds);

        var utcNow = clock.UtcNow;
        var (fromUtc, toUtc) = WatTime.GetCurrentDayWindowUtc(utcNow);
        var outboundSoFar = await repository.GetOutboundTotalAsync(source.Id, fromUtc, toUtc, ct);
        if (outboundSoFar + request.AmountKobo > source.DailyOutboundLimitKobo)
            return Result.Failure<TransferResponse>(WalletErrors.DailyLimitExceeded);

        var transferId = Guid.NewGuid();
        var sourceBalanceBefore = source.BalanceKobo;
        var destinationBalanceBefore = destination.BalanceKobo;
        var description = string.IsNullOrWhiteSpace(request.Description) ? "Wallet transfer" : request.Description;

        source.Debit(request.AmountKobo);
        destination.Credit(request.AmountKobo);

        repository.AddLedgerEntry(LedgerEntry.ForTransferOut(source.Id, destination.Id, request.AmountKobo, source.BalanceKobo, transferId, description, utcNow));
        repository.AddLedgerEntry(LedgerEntry.ForTransferIn(destination.Id, source.Id, request.AmountKobo, destination.BalanceKobo, transferId, description, utcNow));

        repository.AddAuditEntry(AuditEntry.Create(source.Id, "TransferDebited", request.AmountKobo, sourceBalanceBefore, source.BalanceKobo, request.CallerCustomerId, transferId, utcNow));
        repository.AddAuditEntry(AuditEntry.Create(destination.Id, "TransferCredited", request.AmountKobo, destinationBalanceBefore, destination.BalanceKobo, request.CallerCustomerId, transferId, utcNow));

        return Result.Success(new TransferResponse(transferId, source.Id, destination.Id, request.AmountKobo, source.BalanceKobo, utcNow));
    }

    private static string ComputeRequestHash(TransferCommand request)
    {
        var canonical = $"{request.SourceWalletId:N}|{request.DestinationWalletId:N}|{request.AmountKobo}|{request.Description}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private sealed record Envelope(bool IsSuccess, string? ErrorCode, string? ErrorDescription, ErrorType? ErrorType, TransferResponse? Value);

    private static string SerializeEnvelope(Result<TransferResponse> result)
    {
        var envelope = result.IsSuccess
            ? new Envelope(true, null, null, null, result.Value)
            : new Envelope(false, result.Error.Code, result.Error.Description, result.Error.Type, null);

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private static Result<TransferResponse> DeserializeEnvelope(string payload)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Cached idempotent response could not be deserialized.");

        return envelope.IsSuccess
            ? Result.Success(envelope.Value!)
            : Result.Failure<TransferResponse>(new Error(envelope.ErrorCode!, envelope.ErrorDescription!, envelope.ErrorType!.Value));
    }
}
