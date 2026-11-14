using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepresentativeReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepresentativeReceipts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RepresentativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<long>(type: "bigint", nullable: false),
                    BasisType = table.Column<string>(type: "text", nullable: false),
                    BasisReference = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashboxId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HandedOverAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepresentativeReceipts", x => x.Id);
                    table.CheckConstraint("CK_RepresentativeReceipts_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_RepresentativeReceipts_Basis", "\"BasisType\" IN ('order','delivery','return','manual')");
                    table.CheckConstraint("CK_RepresentativeReceipts_Status", "\"Status\" IN ('collected','handed_over','accepted','cancelled')");
                    table.ForeignKey(
                        name: "FK_RepresentativeReceipts_Counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepresentativeReceipts_CounterpartyId",
                table: "RepresentativeReceipts",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentativeReceipts_RequestKey",
                table: "RepresentativeReceipts",
                column: "RequestKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepresentativeReceipts");
        }
    }
}
