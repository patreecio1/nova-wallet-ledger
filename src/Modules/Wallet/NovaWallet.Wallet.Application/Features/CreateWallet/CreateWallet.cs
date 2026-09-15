using FluentValidation;
using NovaWallet.BuildingBlocks.Application.CQRS;
using NovaWallet.BuildingBlocks.Domain.Results;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Application.Features.CreateWallet;

/// <summary>CustomerId is the caller's own id for role=customer; role=system may create a wallet on behalf of any customer (e.g. onboarding).</summary>
public sealed record CreateWalletCommand(Guid CustomerId) : ICommand<WalletResponse>;

public sealed record WalletResponse(Guid WalletId, Guid CustomerId, long BalanceKobo, string Currency, long DailyOutboundLimitKobo, DateTime CreatedAtUtc)
{
    public static WalletResponse From(WalletAccount wallet) =>
        new(wallet.Id, wallet.CustomerId, wallet.BalanceKobo, WalletAccount.Currency, wallet.DailyOutboundLimitKobo, wallet.CreatedAtUtc);
}

public sealed class CreateWalletCommandValidator : AbstractValidator<CreateWalletCommand>
{
    public CreateWalletCommandValidator() => RuleFor(x => x.CustomerId).NotEmpty();
}

public sealed class CreateWalletCommandHandler(IWalletRepository repository, IClock clock)
    : ICommandHandler<CreateWalletCommand, WalletResponse>
{
    public async Task<Result<WalletResponse>> Handle(CreateWalletCommand request, CancellationToken cancellationToken)
    {
        var wallet = WalletAccount.Create(request.CustomerId, clock.UtcNow);
        repository.Add(wallet);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(WalletResponse.From(wallet));
    }
}
