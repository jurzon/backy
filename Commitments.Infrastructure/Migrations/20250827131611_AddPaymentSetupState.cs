using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commitments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentSetupState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentSetupStates",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HasPaymentMethod = table.Column<bool>(type: "boolean", nullable: false),
                    LatestSetupIntentId = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentSetupStates", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntentLogs_CommitmentId_AttemptNumber",
                table: "PaymentIntentLogs",
                columns: new[] { "CommitmentId", "AttemptNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentSetupStates");

            migrationBuilder.DropIndex(
                name: "IX_PaymentIntentLogs_CommitmentId_AttemptNumber",
                table: "PaymentIntentLogs");
        }
    }
}
