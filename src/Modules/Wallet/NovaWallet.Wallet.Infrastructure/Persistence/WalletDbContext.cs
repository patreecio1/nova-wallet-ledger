using Microsoft.EntityFrameworkCore;
using NovaWallet.BuildingBlocks.Domain.Outbox;
using NovaWallet.BuildingBlocks.Infrastructure.Persistence;
using NovaWallet.Wallet.Domain;

namespace NovaWallet.Wallet.Infrastructure.Persistence;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : BaseDbContext(options)
{
    public const string CustomerIdUniqueIndexName = "UX_wallets_CustomerId";
    public const string IdempotencyKeyPrimaryKeyName = "PK_idempotency_keys";

    protected override string Schema => "wallet";

    public DbSet<WalletAccount> Wallets => Set<WalletAccount>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletAccount>(builder =>
        {
            builder.ToTable("wallets");
            builder.HasKey(w => w.Id);
            builder.Property(w => w.CustomerId).IsRequired();
            builder.Property(w => w.BalanceKobo).IsRequired();
            builder.Property(w => w.DailyOutboundLimitKobo).IsRequired();
            builder.Property(w => w.CreatedAtUtc).IsRequired();
            builder.HasIndex(w => w.CustomerId).IsUnique().HasDatabaseName(CustomerIdUniqueIndexName);
        });
        ConfigureConcurrencyToken<WalletAccount>(modelBuilder);

        modelBuilder.Entity<LedgerEntry>(builder =>
        {
            builder.ToTable("ledger_entries");
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
            builder.Property(l => l.Description).HasMaxLength(500);
            builder.HasIndex(l => new { l.WalletId, l.CreatedAtUtc });
            builder.HasIndex(l => l.TransferGroupId);
        });

        modelBuilder.Entity<AuditEntry>(builder =>
        {
            builder.ToTable("audit_entries");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.EventType).HasMaxLength(50);
            builder.HasIndex(a => new { a.WalletId, a.OccurredAtUtc });
            builder.HasIndex(a => a.CorrelationId);
        });

        modelBuilder.Entity<IdempotencyRecord>(builder =>
        {
            builder.ToTable("idempotency_keys");
            builder.HasKey(i => i.Key).HasName(IdempotencyKeyPrimaryKeyName);
            builder.Property(i => i.Key).HasMaxLength(200);
            builder.Property(i => i.RequestHash).HasMaxLength(128).IsRequired();
            builder.Property(i => i.ResponsePayload).HasColumnType("jsonb");
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Type).HasMaxLength(200).IsRequired();
            builder.Property(m => m.Content).HasColumnType("jsonb").IsRequired();
            builder.Property(m => m.Error).HasMaxLength(2000);
            // The dispatcher's only query is "oldest unprocessed rows first" — a partial index
            // keeps that fast without bloating as processed messages pile up.
            builder.HasIndex(m => m.OccurredOnUtc).HasFilter("\"ProcessedOnUtc\" IS NULL");
        });

        base.OnModelCreating(modelBuilder);
    }
}
