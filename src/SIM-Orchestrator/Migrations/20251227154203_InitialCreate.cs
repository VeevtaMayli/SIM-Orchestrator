using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIMOrchestrator.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmsMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Sender = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentToTelegram = table.Column<bool>(type: "INTEGER", nullable: false),
                    SentToTelegramAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_ReceivedAt",
                table: "SmsMessages",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_SentToTelegram",
                table: "SmsMessages",
                column: "SentToTelegram");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsMessages");
        }
    }
}
