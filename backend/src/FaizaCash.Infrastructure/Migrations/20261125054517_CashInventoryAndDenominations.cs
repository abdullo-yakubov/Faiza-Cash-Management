using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CashInventoryAndDenominations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Denominations",
                table: "Shifts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashInventories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CashboxId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CountedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpectedBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Denominations = table.Column<string>(type: "jsonb", nullable: false),
                    Commission = table.Column<string[]>(type: "text[]", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashInventories", x => x.Id);
                    table.CheckConstraint("CK_CashInventories_Values", "\"ExpectedBalance\" >= 0 AND \"ActualBalance\" >= 0 AND \"Difference\" = \"ActualBalance\" - \"ExpectedBalance\" AND cardinality(\"Commission\") BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_CashInventories_Cashboxes_CashboxId",
                        column: x => x.CashboxId,
                        principalTable: "Cashboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashInventories_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Denominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Denominations", x => x.Id);
                    table.CheckConstraint("CK_Denominations_Values", "\"Value\" > 0 AND \"Kind\" IN ('banknote','coin')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashInventories_CashboxId_CountedAt",
                table: "CashInventories",
                columns: new[] { "CashboxId", "CountedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashInventories_CreatedBy",
                table: "CashInventories",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Denominations_Currency_Value_Kind",
                table: "Denominations",
                columns: new[] { "Currency", "Value", "Kind" },
                unique: true);

            // Номиналы сомони и дирамов в обращении по данным Национального банка Таджикистана
            // (nbt.tj: «Банкноты», «Монеты», новость 571759): банкноты 1, 3, 5, 10, 20, 50, 100, 200, 500 сомони;
            // монеты 1, 3, 5 сомони и 5, 10, 20, 25, 50 дирамов. Администратор меняет набор через PUT /api/denominations.
            migrationBuilder.Sql("""
                INSERT INTO "Denominations" ("Currency", "Value", "Kind", "Active") VALUES
                  ('TJS', 500.00, 'banknote', TRUE), ('TJS', 200.00, 'banknote', TRUE), ('TJS', 100.00, 'banknote', TRUE),
                  ('TJS', 50.00, 'banknote', TRUE), ('TJS', 20.00, 'banknote', TRUE), ('TJS', 10.00, 'banknote', TRUE),
                  ('TJS', 5.00, 'banknote', TRUE), ('TJS', 3.00, 'banknote', TRUE), ('TJS', 1.00, 'banknote', TRUE),
                  ('TJS', 5.00, 'coin', TRUE), ('TJS', 3.00, 'coin', TRUE), ('TJS', 1.00, 'coin', TRUE),
                  ('TJS', 0.50, 'coin', TRUE), ('TJS', 0.25, 'coin', TRUE), ('TJS', 0.20, 'coin', TRUE),
                  ('TJS', 0.10, 'coin', TRUE), ('TJS', 0.05, 'coin', TRUE)
                ON CONFLICT DO NOTHING;
                CREATE FUNCTION faiza_inventory_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Cash inventory acts are append-only'; END $$;
                CREATE TRIGGER cash_inventory_immutable BEFORE UPDATE OR DELETE ON "CashInventories" FOR EACH ROW EXECUTE FUNCTION faiza_inventory_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS cash_inventory_immutable ON \"CashInventories\"; DROP FUNCTION IF EXISTS faiza_inventory_immutable();");
            migrationBuilder.DropTable(
                name: "CashInventories");

            migrationBuilder.DropTable(
                name: "Denominations");

            migrationBuilder.DropColumn(
                name: "Denominations",
                table: "Shifts");
        }
    }
}
