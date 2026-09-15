using NovaWallet.BuildingBlocks.Application.CQRS;
using NovaWallet.BuildingBlocks.Domain.Results;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Application.Features.GetBalance;

public sealed record GetBalanceQuery(Guid WalletId, Guid CallerCustomerId, CallerRole CallerRole) : IQuery<WalletResponse>;

public sealed class GetBalanceQueryHandler(IWalletRepository repository)
    : IQueryHandler<GetBalanceQuery, WalletResponse>
{
    public async Task<Result<WalletResponse>> Handle(GetBalanceQuery request, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(request.WalletId, cancellationToken);
        if (wallet is null)
            return Result.Failure<WalletResponse>(WalletErrors.NotFound(request.WalletId));

        if (request.CallerRole != CallerRole.System && wallet.CustomerId != request.CallerCustomerId)
            return Result.Failure<WalletResponse>(WalletErrors.Forbidden);

        return Result.Success(WalletResponse.From(wallet));
    }
}
