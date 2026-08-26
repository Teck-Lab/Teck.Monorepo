using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Host.Database.Migrations.PostgreSQL;

/// <inheritdoc />
public partial class InitialNotificationPostgreSQL : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeycloakSubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
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
                    table.PrimaryKey("PK_customer_contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeycloakSubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceCorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContactRequestId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_notification_deliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_contacts_TenantId_CustomerId",
                table: "customer_contacts",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_contacts_TenantId_KeycloakSubjectId",
                table: "customer_contacts",
                columns: new[] { "TenantId", "KeycloakSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_TenantId_ContactRequestId",
                table: "notification_deliveries",
                columns: new[] { "TenantId", "ContactRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_TenantId_CustomerId",
                table: "notification_deliveries",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_TenantId_IdempotencyKey",
                table: "notification_deliveries",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_TenantId_KeycloakSubjectId",
                table: "notification_deliveries",
                columns: new[] { "TenantId", "KeycloakSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_TenantId_OrderId",
                table: "notification_deliveries",
                columns: new[] { "TenantId", "OrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_contacts");

            migrationBuilder.DropTable(
                name: "notification_deliveries");
        }
}
