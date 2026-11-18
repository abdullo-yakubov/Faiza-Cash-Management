using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Branches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.CheckConstraint("CK_Branches_Code", "char_length(btrim(\"Code\")) BETWEEN 2 AND 24");
                });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Code", "Name", "City", "Active" },
                values: new object[,]
                {
                    { 1L, "DUSHANBE", "Филиал Душанбе", "Душанбе", true },
                    { 2L, "KHUJAND", "Филиал Худжанд", "Худжанд", true },
                    { 3L, "BOKHTAR", "Филиал Бохтар", "Бохтар", true }
                });

            migrationBuilder.AddColumn<long>(
                name: "BranchId",
                table: "Cashboxes",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateIndex(
                name: "IX_Cashboxes_BranchId",
                table: "Cashboxes",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Cashboxes_Branches_BranchId",
                table: "Cashboxes",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cashboxes_Branches_BranchId",
                table: "Cashboxes");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Cashboxes_BranchId",
                table: "Cashboxes");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Cashboxes");
        }
    }
}
