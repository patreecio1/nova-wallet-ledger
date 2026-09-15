using System.IdentityModel.Tokens.Jwt;
using NovaWallet.Wallet.Application.Abstractions;

namespace NovaWallet.Api.Auth;

public static class ClaimNames
{
    public const string Role = "role";
}

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal User =>
        httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HttpContext is available to resolve the current user.");

    public Guid CustomerId
    {
        get
        {
            var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? throw new InvalidOperationException("The JWT is missing a 'sub' claim.");
            return Guid.Parse(sub);
        }
    }

    public CallerRole Role => User.FindFirst(ClaimNames.Role)?.Value switch
    {
        "system" => CallerRole.System,
        _ => CallerRole.Customer,
    };
}
