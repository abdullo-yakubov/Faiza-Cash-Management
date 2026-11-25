using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpenseApprovalThreshold",
                table: "Organizations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalComment",
                table: "Operations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Operations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "Operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Organizations_ApprovalThreshold",
                table: "Organizations",
                sql: "\"ExpenseApprovalThreshold\" IS NULL OR \"ExpenseApprovalThreshold\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_ApprovalStatus_CashboxId",
                table: "Operations",
                columns: new[] { "ApprovalStatus", "CashboxId" },
                filter: "\"ApprovalStatus\" <> 'none'");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_ApprovedBy",
                table: "Operations",
                column: "ApprovedBy");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Operations_ApprovalStatus",
                table: "Operations",
                sql: "\"ApprovalStatus\" IN ('none','pending','approved','rejected')");

            migrationBuilder.AddForeignKey(
                name: "FK_Operations_Users_ApprovedBy",
                table: "Operations",
                column: "ApprovedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Operations_Users_ApprovedBy",
                table: "Operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Organizations_ApprovalThreshold",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Operations_ApprovalStatus_CashboxId",
                table: "Operations");

            migrationBuilder.DropIndex(
                name: "IX_Operations_ApprovedBy",
                table: "Operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Operations_ApprovalStatus",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "ExpenseApprovalThreshold",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ApprovalComment",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "Operations");
        }
    }
}
