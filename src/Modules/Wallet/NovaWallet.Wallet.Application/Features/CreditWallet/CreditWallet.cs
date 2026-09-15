using FluentValidation;
using NovaWallet.BuildingBlocks.Application.CQRS;
using NovaWallet.BuildingBlocks.Domain.Results;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Application.Features.CreditWallet;

/// <summary>
/// Simulates an inbound NIBSS NIP settlement landing in the wallet. In production this would
/// be invoked by a trusted server-to-server webhook (bank rails), never directly by a customer
/// — enforced here by requiring CallerRole.System (see CreditWalletCommandHandler / the
/// Carter endpoint's authorization policy in Wallet.API).
/// </summary>
public sealed record CreditWalletCommand(Guid WalletId, long AmountKobo, string Reference, CallerRole CallerRole) : ICommand<WalletResponse>;

public sealed class CreditWalletCommandValidator : AbstractValidator<CreditWalletCommand>
{
    public CreditWalletCommandValidator()
    {
        RuleFor(x => x.WalletId).NotEmpty();
        RuleFor(x => x.AmountKobo).GreaterThan(0);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreditWalletCommandHandler(IWalletRepository repository, IClock clock)
    : ICommandHandler<CreditWalletCommand, WalletResponse>
{
    public Task<Result<WalletResponse>> Handle(CreditWalletCommand request, CancellationToken cancellationToken) =>
        repository.ExecuteInTransactionAsync(async ct =>
        {
            if (request.CallerRole != CallerRole.System)
                return Result.Failure<WalletResponse>(WalletErrors.Forbidden);

            var locked = await repository.LockForUpdateAsync([request.WalletId], ct);
            if (!locked.TryGetValue(request.WalletId, out var wallet))
                return Result.Failure<WalletResponse>(WalletErrors.NotFound(request.WalletId));

            var utcNow = clock.UtcNow;
            var creditId = Guid.NewGuid();

            var balanceBefore = wallet.BalanceKobo;
            wallet.Credit(request.AmountKobo);

            repository.AddLedgerEntry(LedgerEntry.ForCredit(wallet.Id, request.AmountKobo, wallet.BalanceKobo, creditId, request.Reference, utcNow));
            repository.AddAuditEntry(AuditEntry.Create(wallet.Id, "WalletCredited", request.AmountKobo, balanceBefore, wallet.BalanceKobo, actorCustomerId: null, creditId, utcNow));

            await repository.SaveChangesAsync(ct);

            return Result.Success(WalletResponse.From(wallet));
        }, cancellationToken);
}
