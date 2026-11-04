using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIMOrchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "SmsMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "SmsMessages",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "SmsMessages",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "SmsMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "SmsMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SmsMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing rows: derive the delivery state from the legacy flag and give
            // each row a unique key (SQLite cannot hash in SQL, so use the id) so the
            // unique index below can be created on a populated table.
            migrationBuilder.Sql(
                """
                UPDATE "SmsMessages"
                SET "Status" = CASE WHEN "SentToTelegram" THEN 2 ELSE 0 END,
                    "Attempts" = CASE WHEN "SentToTelegram" THEN 1 ELSE 0 END,
                    "DedupKey" = 'legacy:' || "Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_DedupKey",
                table: "SmsMessages",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_Status_NextAttemptAt",
                table: "SmsMessages",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SmsMessages_DedupKey",
                table: "SmsMessages");

            migrationBuilder.DropIndex(
                name: "IX_SmsMessages_Status_NextAttemptAt",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SmsMessages");
        }
    }
}
