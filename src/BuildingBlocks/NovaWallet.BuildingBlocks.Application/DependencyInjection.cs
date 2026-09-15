using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.BuildingBlocks.Application.Behaviours;

namespace NovaWallet.BuildingBlocks.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the shared MediatR pipeline behaviours. Order matters: logging wraps
    /// everything, validation runs before the handler ever sees a malformed request.
    /// Call once from the host; each module still registers MediatR handlers from its
    /// own Application assembly separately (see &lt;Module&gt;ModuleExtensions.AddXModule).
    /// </summary>
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        return services;
    }
}
