using Microsoft.EntityFrameworkCore;

namespace NovaWallet.BuildingBlocks.Infrastructure.Persistence;

public static class NpgsqlResilience
{
    /// <summary>
    /// One call site for every module's `UseNpgsql`: a dedicated migrations-history table per
    /// schema (so one physical database can host multiple independently-migratable modules)
    /// and automatic retry-on-transient-failure for the connection itself. This does NOT retry
    /// application-level conflicts (unique violations, concurrency exceptions) — those are
    /// business outcomes the repository layer handles explicitly, not transient infra failures.
    /// </summary>
    public static DbContextOptionsBuilder UsePlatformPostgres(this DbContextOptionsBuilder options, string? connectionString, string schema) =>
        options.UseNpgsql(connectionString, npgsql => npgsql
            .MigrationsHistoryTable("__ef_migrations_history", schema)
            .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null));
}
