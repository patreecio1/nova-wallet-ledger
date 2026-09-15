using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace NovaWallet.Api.Infrastructure;

/// <summary>
/// Safety net for exceptions the app did NOT expect and could not turn into a Result.Failure —
/// malformed requests (FluentValidation, thrown by ValidationBehaviour), EF write conflicts that
/// slipped past the repository's own handling, and everything else. Expected business failures
/// never reach here; they come back as Result.Failure and are mapped by ResultExtensions.ToProblem
/// at the endpoint instead.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validationException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = string.Join(" ", validationException.Errors.Select(e => e.ErrorMessage)),
            },
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request could not be understood",
                Detail = badRequest.Message,
            },
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The wallet was modified concurrently",
                Detail = "Please retry the request.",
            },
            DbUpdateException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The change conflicts with existing data",
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
            },
        };

        if (problem.Status >= 500)
            logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);
        else
            logger.LogWarning(exception, "Handled exception on {Path}: {Title}", httpContext.Request.Path, problem.Title);

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
