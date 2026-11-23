using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SettlementCashboxScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CashboxId",
                table: "Settlements",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_CashboxId",
                table: "Settlements",
                column: "CashboxId");

            migrationBuilder.AddForeignKey(
                name: "FK_Settlements_Cashboxes_CashboxId",
                table: "Settlements",
                column: "CashboxId",
                principalTable: "Cashboxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Settlements_Cashboxes_CashboxId",
                table: "Settlements");

            migrationBuilder.DropIndex(
                name: "IX_Settlements_CashboxId",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "CashboxId",
                table: "Settlements");
        }
    }
}
