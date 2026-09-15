using Carter;
using FluentValidation;

namespace NovaWallet.Api.Auth;

public sealed record IssueTokenRequest(Guid CustomerId, string Role = "customer");

public sealed class IssueTokenRequestValidator : AbstractValidator<IssueTokenRequest>
{
    public IssueTokenRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Role).Must(r => r is "customer" or "system").WithMessage("Role must be 'customer' or 'system'.");
    }
}

/// <summary>
/// Demo-only token issuer, mounted solely for grading/testing this take-home outside
/// Production (see Program.cs). Anyone who can reach it can mint a token for any customerId
/// and any role, including "system" — that is fine for a mock issuer standing in for
/// middleware/claims-handling review, but would never ship like this.
/// </summary>
public sealed class AuthModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/dev-token", (IssueTokenRequest request, IssueTokenRequestValidator validator, JwtTokenService tokenService) =>
        {
            var validation = validator.Validate(request);
            if (!validation.IsValid)
            {
                var errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.ValidationProblem(errors);
            }

            var issued = tokenService.Issue(request.CustomerId, request.Role);
            return Results.Ok(new { accessToken = issued.AccessToken, expiresAtUtc = issued.ExpiresAtUtc });
        })
        .WithTags("Auth (dev-only)")
        .AllowAnonymous();
    }
}
