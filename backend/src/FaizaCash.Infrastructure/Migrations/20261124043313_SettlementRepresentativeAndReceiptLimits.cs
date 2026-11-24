using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <summary>
    /// V7.1: optional responsible representative on a debt (nullable FK, RESTRICT) and a bounded
    /// basis reference for representative receipts (D-05). Existing rows are untouched; receipts with
    /// a reference longer than 120 characters must be corrected before applying (checked below).
    /// </summary>
    public partial class SettlementRepresentativeAndReceiptLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RepresentativeId",
                table: "Settlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_RepresentativeId",
                table: "Settlements",
                column: "RepresentativeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Settlements_Users_RepresentativeId",
                table: "Settlements",
                column: "RepresentativeId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                DO $preflight$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "RepresentativeReceipts" WHERE char_length(btrim("BasisReference")) NOT BETWEEN 1 AND 120) THEN
                        RAISE EXCEPTION 'RepresentativeReceipts.BasisReference must be 1..120 characters before applying this migration';
                    END IF;
                END $preflight$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RepresentativeReceipts_Reference",
                table: "RepresentativeReceipts",
                sql: "char_length(btrim(\"BasisReference\")) BETWEEN 1 AND 120");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RepresentativeReceipts_Reference",
                table: "RepresentativeReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_Settlements_Users_RepresentativeId",
                table: "Settlements");

            migrationBuilder.DropIndex(
                name: "IX_Settlements_RepresentativeId",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "RepresentativeId",
                table: "Settlements");
        }
    }
}
