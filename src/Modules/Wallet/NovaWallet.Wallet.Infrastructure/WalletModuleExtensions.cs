using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.BuildingBlocks.Infrastructure.Persistence;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.CreateWallet;
using NovaWallet.Wallet.Application.Outbox;
using NovaWallet.Wallet.Infrastructure.Idempotency;
using NovaWallet.Wallet.Infrastructure.Outbox;
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

        // Transactional outbox: TransferCommandHandler writes an OutboxMessage in the same
        // transaction as the transfer; this dispatcher polls for unpublished ones and hands them
        // to IIntegrationEventPublisher (a logger for this take-home — see
        // LoggingIntegrationEventPublisher for what a real deployment would swap in instead).
        services.AddScoped<IIntegrationEventPublisher, LoggingIntegrationEventPublisher>();
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcherHostedService>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateWalletCommand).Assembly));
        services.AddValidatorsFromAssembly(typeof(CreateWalletCommand).Assembly);

        return services;
    }
}
