using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SettlementsAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CounterpartyId = table.Column<long>(type: "bigint", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    BasisType = table.Column<string>(type: "text", nullable: false),
                    BasisReference = table.Column<string>(type: "text", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settlements", x => x.Id);
                    table.CheckConstraint("CK_Settlements_Amounts", "\"TotalAmount\" > 0 AND \"PaidAmount\" >= 0 AND \"PaidAmount\" <= \"TotalAmount\"");
                    table.CheckConstraint("CK_Settlements_Basis", "\"BasisType\" IN ('order','delivery','return','manual') AND char_length(btrim(\"BasisReference\")) BETWEEN 1 AND 120");
                    table.CheckConstraint("CK_Settlements_Direction", "\"Direction\" IN ('receivable','payable')");
                    table.ForeignKey(
                        name: "FK_Settlements_Counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SettlementId = table.Column<long>(type: "bigint", nullable: false),
                    OperationId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Settlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "Settlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_CreatedBy",
                table: "PaymentAllocations",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_OperationId",
                table: "PaymentAllocations",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_SettlementId_OperationId",
                table: "PaymentAllocations",
                columns: new[] { "SettlementId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_CounterpartyId_Status",
                table: "Settlements",
                columns: new[] { "CounterpartyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAllocations");

            migrationBuilder.DropTable(
                name: "Settlements");
        }
    }
}
