using Microsoft.EntityFrameworkCore;

namespace NovaWallet.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Shared DbContext base. Owns one convention: every entity that opts in via
/// <see cref="ConfigureConcurrencyToken{TEntity}"/> uses Postgres's own `xmin` system column
/// as its EF concurrency token (a shadow `uint` property literally named "xmin", which Npgsql's
/// provider recognizes and maps to the system column instead of creating a real one). Postgres
/// bumps `xmin` on every row update for free — there is no manual "increment a version counter"
/// step to get right or forget, unlike the SQL Server `rowversion` pattern. EF puts the
/// last-read `xmin` value in the UPDATE's WHERE clause, so a write against a row someone else
/// already changed matches zero rows and EF raises <see cref="DbUpdateConcurrencyException"/> —
/// the repository layer decides whether to retry (see WalletRepository).
/// </summary>
public abstract class BaseDbContext(DbContextOptions options) : DbContext(options)
{
    protected abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }

    protected static void ConfigureConcurrencyToken<TEntity>(ModelBuilder modelBuilder) where TEntity : class =>
        modelBuilder.Entity<TEntity>().Property<uint>("xmin").IsRowVersion();
}
