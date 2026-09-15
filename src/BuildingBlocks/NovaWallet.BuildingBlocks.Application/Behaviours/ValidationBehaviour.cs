using FluentValidation;
using MediatR;

namespace NovaWallet.BuildingBlocks.Application.Behaviours;

/// <summary>
/// Runs all registered FluentValidation validators for the request before the handler executes.
/// Deliberately throws (rather than returning a Result.Failure) so that "this request is
/// malformed" (400, caught by GlobalExceptionHandler) stays distinct from "this request is
/// well-formed but the business rejected it" (a Result.Failure the handler returns itself,
/// mapped to 422 at the endpoint). Mixing the two into one channel makes API responses
/// harder to reason about client-side.
/// </summary>
public sealed class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors.Where(f => f is not null));
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
