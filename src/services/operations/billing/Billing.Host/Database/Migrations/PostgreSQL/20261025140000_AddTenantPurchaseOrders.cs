using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <inheritdoc />
public partial class AddTenantPurchaseOrders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tenant_purchase_orders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: true),
                DeletedBy = table.Column<string>(type: "text", nullable: true),
                DeletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                UpdatedBy = table.Column<string>(type: "text", nullable: true),
                UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreatedBySubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                BeneficiarySubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PlanVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                DatabaseStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DatabaseProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                HasReadReplicas = table.Column<bool>(type: "boolean", nullable: false),
                ReplicaCount = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                PaymentMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExternalInvoiceReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                InvoiceVerifiedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_tenant_purchase_orders", order => order.Id));

        migrationBuilder.CreateIndex("IX_tenant_purchase_orders_Status", "tenant_purchase_orders", "Status");
        migrationBuilder.CreateIndex("IX_tenant_purchase_orders_SubscriptionId", "tenant_purchase_orders", "SubscriptionId", unique: true, filter: "\"SubscriptionId\" <> '00000000-0000-0000-0000-000000000000'");
        migrationBuilder.CreateIndex("IX_tenant_purchase_orders_TenantId", "tenant_purchase_orders", "TenantId", unique: true);
        migrationBuilder.CreateIndex("IX_tenant_purchase_orders_TenantIdentifier", "tenant_purchase_orders", "TenantIdentifier", unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("tenant_purchase_orders");
}
