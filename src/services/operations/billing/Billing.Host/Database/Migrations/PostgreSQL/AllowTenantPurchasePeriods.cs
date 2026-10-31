using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Allows one independently paid order per annual period and at most one pending order per tenant.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261027011000_AllowTenantPurchasePeriods")]
public sealed class AllowTenantPurchasePeriods : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_SubscriptionId", table: "tenant_purchase_orders");
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_TenantId", table: "tenant_purchase_orders");
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_TenantIdentifier", table: "tenant_purchase_orders");
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_SubscriptionId", table: "tenant_purchase_orders",
            column: "SubscriptionId", filter: "\"SubscriptionId\" <> '00000000-0000-0000-0000-000000000000'");
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_TenantId", table: "tenant_purchase_orders", column: "TenantId");
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_TenantIdentifier", table: "tenant_purchase_orders", column: "TenantIdentifier");
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_one_pending_per_tenant", table: "tenant_purchase_orders",
            column: "TenantId", unique: true, filter: "\"Status\" = 'pending'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_one_pending_per_tenant", table: "tenant_purchase_orders");
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_SubscriptionId", table: "tenant_purchase_orders");
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_TenantId", table: "tenant_purchase_orders");
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_TenantIdentifier", table: "tenant_purchase_orders");
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_SubscriptionId", table: "tenant_purchase_orders",
            column: "SubscriptionId", unique: true, filter: "\"SubscriptionId\" <> '00000000-0000-0000-0000-000000000000'");
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_TenantId", table: "tenant_purchase_orders",
            column: "TenantId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_TenantIdentifier", table: "tenant_purchase_orders",
            column: "TenantIdentifier", unique: true);
    }
}
