using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FaizaCash.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentsAndAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Advances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationId = table.Column<long>(type: "bigint", nullable: false),
                    CashboxId = table.Column<long>(type: "bigint", nullable: false),
                    CounterpartyId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Spent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SettlementOperationId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReportedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advances", x => x.Id);
                    table.CheckConstraint("CK_Advances_Amounts", "\"Amount\" > 0 AND (\"Spent\" IS NULL OR \"Spent\" >= 0)");
                    table.CheckConstraint("CK_Advances_Purpose", "char_length(btrim(\"Purpose\")) BETWEEN 1 AND 500");
                    table.CheckConstraint("CK_Advances_Status", "\"Status\" IN ('issued','reported','closed')");
                    table.ForeignKey(
                        name: "FK_Advances_Cashboxes_CashboxId",
                        column: x => x.CashboxId,
                        principalTable: "Cashboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Advances_Counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Advances_Operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Advances_Operations_SettlementOperationId",
                        column: x => x.SettlementOperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Advances_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    CashboxId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.CheckConstraint("CK_Attachments_File", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 5242880 AND \"ContentType\" IN ('image/jpeg','image/png','image/webp','application/pdf') AND char_length(\"Sha256\") = 64");
                    table.CheckConstraint("CK_Attachments_Owner", "\"OwnerType\" IN ('operation','receipt','advance') AND \"OwnerId\" > 0");
                    table.ForeignKey(
                        name: "FK_Attachments_Cashboxes_CashboxId",
                        column: x => x.CashboxId,
                        principalTable: "Cashboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attachments_Users_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvanceReportLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvanceId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceReportLines", x => x.Id);
                    table.CheckConstraint("CK_AdvanceReportLines_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_AdvanceReportLines_Advances_AdvanceId",
                        column: x => x.AdvanceId,
                        principalTable: "Advances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceReportLines_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceReportLines_AdvanceId",
                table: "AdvanceReportLines",
                column: "AdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceReportLines_CategoryId",
                table: "AdvanceReportLines",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_CashboxId",
                table: "Advances",
                column: "CashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_CounterpartyId",
                table: "Advances",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_CreatedBy",
                table: "Advances",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_OperationId",
                table: "Advances",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Advances_SettlementOperationId",
                table: "Advances",
                column: "SettlementOperationId",
                unique: true,
                filter: "\"SettlementOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_Status_DueDate",
                table: "Advances",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_CashboxId",
                table: "Attachments",
                column: "CashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OwnerType_OwnerId",
                table: "Attachments",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UploadedBy",
                table: "Attachments",
                column: "UploadedBy");

            // Удаление вложения проведённого документа, принятой/отменённой квитанции или закрытого аванса
            // запрещено на уровне БД (дублирует проверку в коде, как ImmutableLedger для журнала).
            migrationBuilder.Sql("""
                CREATE FUNCTION faiza_attachment_delete_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."OwnerType" = 'operation' AND EXISTS (SELECT 1 FROM "Operations" o WHERE o."Id" = OLD."OwnerId" AND o."Status" = 'posted'))
                     OR (OLD."OwnerType" = 'receipt' AND EXISTS (SELECT 1 FROM "RepresentativeReceipts" r WHERE r."Id" = OLD."OwnerId" AND r."Status" IN ('accepted','cancelled')))
                     OR (OLD."OwnerType" = 'advance' AND EXISTS (SELECT 1 FROM "Advances" a WHERE a."Id" = OLD."OwnerId" AND a."Status" = 'closed')) THEN
                    RAISE EXCEPTION 'Attachments of posted or accepted documents cannot be deleted';
                  END IF;
                  RETURN OLD;
                END $$;
                CREATE TRIGGER attachment_delete_guard BEFORE DELETE ON "Attachments" FOR EACH ROW EXECUTE FUNCTION faiza_attachment_delete_guard();
                CREATE FUNCTION faiza_attachment_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Attachment metadata is immutable'; END $$;
                CREATE TRIGGER attachment_immutable BEFORE UPDATE ON "Attachments" FOR EACH ROW EXECUTE FUNCTION faiza_attachment_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS attachment_immutable ON \"Attachments\"; DROP FUNCTION IF EXISTS faiza_attachment_immutable(); DROP TRIGGER IF EXISTS attachment_delete_guard ON \"Attachments\"; DROP FUNCTION IF EXISTS faiza_attachment_delete_guard();");
            migrationBuilder.DropTable(
                name: "AdvanceReportLines");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Advances");
        }
    }
}
