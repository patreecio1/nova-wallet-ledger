using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.BuildingBlocks.Infrastructure.Persistence;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Infrastructure.Idempotency;
using NovaWallet.Wallet.Infrastructure.Persistence;

namespace NovaWallet.Wallet.Infrastructure;

public static class WalletModuleExtensions
{
    /// <summary>
    /// Composition root for the Wallet module: its DbContext, repository, MediatR handlers and
    /// FluentValidation validators (scanned only from this module's own Application assembly —
    /// each module owns its own MediatR registration rather than one global reflection scan).
    /// </summary>
    public static IServiceCollection AddWalletModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WalletDbContext>(options =>
            options.UsePlatformPostgres(configuration.GetConnectionString("Default"), "wallet"));

        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateWalletCommand).Assembly));
        services.AddValidatorsFromAssembly(typeof(CreateWalletCommand).Assembly);

        return services;
    }
}
