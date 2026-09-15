using NovaWallet.BuildingBlocks.Application.CQRS;
using NovaWallet.BuildingBlocks.Application.Paging;
using NovaWallet.BuildingBlocks.Domain.Results;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Application.Features.GetStatement;

public sealed record GetStatementQuery(Guid WalletId, Guid CallerCustomerId, CallerRole CallerRole, int? Page, int? PageSize)
    : IQuery<PagedResult<StatementLineResponse>>;

public sealed record StatementLineResponse(
    Guid Id,
    string Type,
    long AmountKobo,
    long BalanceAfterKobo,
    Guid? CounterpartyWalletId,
    Guid TransferGroupId,
    string Description,
    DateTime CreatedAtUtc)
{
    public static StatementLineResponse From(LedgerEntry entry) => new(
        entry.Id,
        entry.Type.ToString(),
        entry.AmountKobo,
        entry.BalanceAfterKobo,
        entry.CounterpartyWalletId,
        entry.TransferGroupId,
        entry.Description,
        entry.CreatedAtUtc);
}

public sealed class GetStatementQueryHandler(IWalletRepository repository)
    : IQueryHandler<GetStatementQuery, PagedResult<StatementLineResponse>>
{
    public async Task<Result<PagedResult<StatementLineResponse>>> Handle(GetStatementQuery request, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(request.WalletId, cancellationToken);
        if (wallet is null)
            return Result.Failure<PagedResult<StatementLineResponse>>(WalletErrors.NotFound(request.WalletId));

        if (request.CallerRole != CallerRole.System && wallet.CustomerId != request.CallerCustomerId)
            return Result.Failure<PagedResult<StatementLineResponse>>(WalletErrors.Forbidden);

        var page = PageRequest.From(request.Page, request.PageSize);
        var statement = await repository.GetStatementAsync(request.WalletId, page, cancellationToken);

        var mapped = new PagedResult<StatementLineResponse>(
            statement.Items.Select(StatementLineResponse.From).ToList(),
            statement.Page,
            statement.PageSize,
            statement.TotalCount);

        return Result.Success(mapped);
    }
}
