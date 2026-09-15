using Carter;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Application.Features.CreditWallet;
using NovaWallet.Wallet.Application.Features.GetBalance;
using NovaWallet.Wallet.Application.Features.GetStatement;
using NovaWallet.Wallet.Application.Features.Transfer;

namespace NovaWallet.Wallet.API;

public sealed class WalletModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Wallet").RequireAuthorization();

        group.MapPost("/wallets", CreateWallet);
        group.MapGet("/wallets/{walletId:guid}/balance", GetBalance);
        group.MapPost("/wallets/{walletId:guid}/credit", CreditWallet);
        group.MapGet("/wallets/{walletId:guid}/statement", GetStatement);
        group.MapPost("/transfers", Transfer).RequireRateLimiting("transfer");
    }

    private static async Task<IResult> CreateWallet(CreateWalletRequest request, ISender sender, ICurrentUser user, CancellationToken ct)
    {
        // A "system" caller may create a wallet on behalf of any customer (e.g. onboarding); a
        // "customer" caller may only ever create their own wallet.
        var customerId = user.Role == CallerRole.System && request.CustomerId is not null
            ? request.CustomerId.Value
            : user.CustomerId;

        var result = await sender.Send(new CreateWalletCommand(customerId), ct);
        return result.IsSuccess ? Results.Created($"/api/wallets/{result.Value.WalletId}", result.Value) : result.ToProblem();
    }

    private static async Task<IResult> GetBalance(Guid walletId, ISender sender, ICurrentUser user, CancellationToken ct)
    {
        var result = await sender.Send(new GetBalanceQuery(walletId, user.CustomerId, user.Role), ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    [Authorize(Policy = "System")]
    private static async Task<IResult> CreditWallet(Guid walletId, CreditWalletRequest request, ISender sender, ICurrentUser user, CancellationToken ct)
    {
        var result = await sender.Send(new CreditWalletCommand(walletId, request.AmountKobo, request.Reference, user.Role), ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> GetStatement(Guid walletId, int? page, int? pageSize, ISender sender, ICurrentUser user, CancellationToken ct)
    {
        var result = await sender.Send(new GetStatementQuery(walletId, user.CustomerId, user.Role, page, pageSize), ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> Transfer(TransferRequest request, HttpRequest httpRequest, ISender sender, ICurrentUser user, CancellationToken ct)
    {
        if (!httpRequest.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyHeader) || string.IsNullOrWhiteSpace(idempotencyKeyHeader))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Wallet.IdempotencyKeyRequired",
                detail: "The Idempotency-Key header is required for this operation.");
        }

        var command = new TransferCommand(
            user.CustomerId,
            request.SourceWalletId,
            request.DestinationWalletId,
            request.AmountKobo,
            request.Description,
            idempotencyKeyHeader.ToString());

        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }
}
