using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CashTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TransferId",
                table: "Operations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceCashboxId = table.Column<long>(type: "bigint", nullable: false),
                    TargetCashboxId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceOperationId = table.Column<long>(type: "bigint", nullable: false),
                    TargetOperationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.CheckConstraint("CK_CashTransfers_Values", "\"SourceCashboxId\" <> \"TargetCashboxId\" AND \"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_Transfers_Cashboxes_SourceCashboxId",
                        column: x => x.SourceCashboxId,
                        principalTable: "Cashboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Cashboxes_TargetCashboxId",
                        column: x => x.TargetCashboxId,
                        principalTable: "Cashboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Operations_SourceOperationId",
                        column: x => x.SourceOperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Operations_TargetOperationId",
                        column: x => x.TargetOperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Operations_TransferId",
                table: "Operations",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_RequestKey",
                table: "Transfers",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_SourceCashboxId",
                table: "Transfers",
                column: "SourceCashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_SourceOperationId",
                table: "Transfers",
                column: "SourceOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TargetCashboxId",
                table: "Transfers",
                column: "TargetCashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TargetOperationId",
                table: "Transfers",
                column: "TargetOperationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Operations_Transfers_TransferId",
                table: "Operations",
                column: "TransferId",
                principalTable: "Transfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Operations_Transfers_TransferId",
                table: "Operations");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Operations_TransferId",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "TransferId",
                table: "Operations");
        }
    }
}
