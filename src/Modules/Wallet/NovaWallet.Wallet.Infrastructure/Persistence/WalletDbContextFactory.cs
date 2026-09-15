using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NovaWallet.BuildingBlocks.Infrastructure.Persistence;

namespace NovaWallet.Wallet.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef migrations add` at design time — never by the running app (see WalletModuleExtensions for the real registration).</summary>
public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOVAWALLET_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=novawallet;Username=novawallet;Password=novawallet";

        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        optionsBuilder.UsePlatformPostgres(connectionString, "wallet");

        return new WalletDbContext(optionsBuilder.Options);
    }
}
