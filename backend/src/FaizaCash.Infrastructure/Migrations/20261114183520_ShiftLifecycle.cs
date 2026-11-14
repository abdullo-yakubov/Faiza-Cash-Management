using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShiftLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "OpeningBalance",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "ClosingBalance",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Shifts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedBy",
                table: "Shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingDifference",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedAt",
                table: "Shifts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "OpenedBy",
                table: "Shifts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE \"Shifts\" SET \"OpenedBy\"=(SELECT \"Id\" FROM \"Users\" ORDER BY \"Id\" LIMIT 1) WHERE \"OpenedBy\"='00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_ClosedBy",
                table: "Shifts",
                column: "ClosedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_OpenedBy",
                table: "Shifts",
                column: "OpenedBy");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashShifts_Status",
                table: "Shifts",
                sql: "\"Status\" IN ('open','closed')");

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Users_ClosedBy",
                table: "Shifts",
                column: "ClosedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Users_OpenedBy",
                table: "Shifts",
                column: "OpenedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Users_ClosedBy",
                table: "Shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Users_OpenedBy",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_ClosedBy",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_OpenedBy",
                table: "Shifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashShifts_Status",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ClosingDifference",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "OpenedBy",
                table: "Shifts");

            migrationBuilder.AlterColumn<decimal>(
                name: "OpeningBalance",
                table: "Shifts",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "ClosingBalance",
                table: "Shifts",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
