using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billings.Host.Database.Migrations.PostgreSQL;

    /// <inheritdoc />
    public partial class PostgreSQLPaymentAttemptsAndOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AuthorizedAmount",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizedCurrency",
                table: "payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CancellationRequestId",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeclineCategory",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclineMappingAuditHash",
                table: "payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeclineMappingAuditedAt",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodToken",
                table: "payments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceCorrelationId",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE payments SET \"AuthorizedAmount\" = \"Amount\", \"AuthorizedCurrency\" = \"Currency\", \"PaymentMethodToken\" = 'legacy-token', \"RequestId\" = 'legacy-' || \"Id\"::text WHERE \"RequestId\" = '';");

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeclineCategory = table.Column<int>(type: "integer", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_attempts_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_TenantId_RequestId",
                table: "payments",
                columns: new[] { "TenantId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_PaymentId",
                table: "payment_attempts",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_TenantId_RequestId",
                table: "payment_attempts",
                columns: new[] { "TenantId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_attempts");

            migrationBuilder.DropIndex(
                name: "IX_payments_TenantId_RequestId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "AuthorizedAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "AuthorizedCurrency",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancellationRequestId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "DeclineCategory",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "DeclineMappingAuditHash",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "DeclineMappingAuditedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PaymentMethodToken",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "SourceCorrelationId",
                table: "payments");
        }
    }
