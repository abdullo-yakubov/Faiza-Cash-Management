using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SystemTransferCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SystemType",
                table: "Categories",
                column: "Type",
                unique: true,
                filter: "\"IsSystem\"");

            // Системные категории межкассового перевода: если категория с таким именем уже есть — помечаем её,
            // иначе создаём. Переводы с этой миграции используют только эти две категории.
            migrationBuilder.Sql("""
                UPDATE "Categories" SET "IsSystem" = TRUE, "Active" = TRUE
                 WHERE ("Type", "Name") IN (('income', 'Межкассовый перевод (приход)'), ('expense', 'Межкассовый перевод (расход)'));
                INSERT INTO "Categories" ("Type", "ParentId", "Name", "Active", "IsSystem")
                SELECT v.type, NULL, v.name, TRUE, TRUE
                  FROM (VALUES ('income', 'Межкассовый перевод (приход)'), ('expense', 'Межкассовый перевод (расход)')) AS v(type, name)
                 WHERE NOT EXISTS (SELECT 1 FROM "Categories" c WHERE c."IsSystem" AND c."Type" = v.type);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_SystemType",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Categories");
        }
    }
}
