using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinancialDocumentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CounterpartyNameSnapshot",
                table: "Settlements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CashboxCodeSnapshot",
                table: "Operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CashboxNameSnapshot",
                table: "Operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryNameSnapshot",
                table: "Operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyNameSnapshot",
                table: "Operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationNameSnapshot",
                table: "Operations",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE "Operations" DISABLE TRIGGER posted_immutable;

                UPDATE "Operations" AS operation
                SET "OrganizationNameSnapshot" = (SELECT "Name" FROM "Organizations" ORDER BY "Id" LIMIT 1),
                    "CashboxNameSnapshot" = cashbox."Name",
                    "CashboxCodeSnapshot" = cashbox."Code",
                    "CategoryNameSnapshot" = category."Name",
                    "CounterpartyNameSnapshot" = (
                        SELECT "Name" FROM "Counterparties" AS counterparty
                        WHERE counterparty."Id" = operation."CounterpartyId")
                FROM "Cashboxes" AS cashbox,
                     "Categories" AS category
                WHERE cashbox."Id" = operation."CashboxId"
                  AND category."Id" = operation."CategoryId";

                UPDATE "Settlements" AS settlement
                SET "CounterpartyNameSnapshot" = counterparty."Name"
                FROM "Counterparties" AS counterparty
                WHERE counterparty."Id" = settlement."CounterpartyId";

                ALTER TABLE "Operations" ENABLE TRIGGER posted_immutable;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounterpartyNameSnapshot",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "CashboxCodeSnapshot",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "CashboxNameSnapshot",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "CategoryNameSnapshot",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "CounterpartyNameSnapshot",
                table: "Operations");

            migrationBuilder.DropColumn(
                name: "OrganizationNameSnapshot",
                table: "Operations");
        }
    }
}
