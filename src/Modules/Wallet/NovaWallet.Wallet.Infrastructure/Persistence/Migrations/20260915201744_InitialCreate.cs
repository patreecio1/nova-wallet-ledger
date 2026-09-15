using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wallet");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    BalanceBeforeKobo = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfterKobo = table.Column<long>(type: "bigint", nullable: false),
                    ActorCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "wallet",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResponsePayload = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfterKobo = table.Column<long>(type: "bigint", nullable: false),
                    CounterpartyWalletId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BalanceKobo = table.Column<long>(type: "bigint", nullable: false),
                    DailyOutboundLimitKobo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_CorrelationId",
                schema: "wallet",
                table: "audit_entries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_WalletId_OccurredAtUtc",
                schema: "wallet",
                table: "audit_entries",
                columns: new[] { "WalletId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_TransferGroupId",
                schema: "wallet",
                table: "ledger_entries",
                column: "TransferGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_WalletId_CreatedAtUtc",
                schema: "wallet",
                table: "ledger_entries",
                columns: new[] { "WalletId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_wallets_CustomerId",
                schema: "wallet",
                table: "wallets",
                column: "CustomerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "wallet");
        }
    }
}
