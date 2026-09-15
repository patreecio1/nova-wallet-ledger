using Microsoft.AspNetCore.Http;
using NovaWallet.BuildingBlocks.Domain.Results;

namespace NovaWallet.Wallet.API;

public static class ResultExtensions
{
    /// <summary>
    /// Maps a failed Result to an RFC 7807 ProblemDetails response with the right HTTP status
    /// for the error's category. Business-rule failures the app expects (Result.Failure) are
    /// mapped here; truly unexpected exceptions are handled separately by GlobalExceptionHandler
    /// in the host — the two are deliberately different channels (see ValidationBehaviour).
    /// </summary>
    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("ToProblem should only be called on a failed Result.");

        var statusCode = result.Error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        return Results.Problem(
            statusCode: statusCode,
            title: result.Error.Code,
            detail: result.Error.Description,
            extensions: new Dictionary<string, object?> { ["errorCode"] = result.Error.Code });
    }
}
