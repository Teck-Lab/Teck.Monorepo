using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Links each independently payable annual renewal to its preceding paid order.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261027012000_LinkPurchaseRenewalPredecessor")]
public sealed class LinkPurchaseRenewalPredecessor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "PreviousPurchaseOrderId", table: "tenant_purchase_orders",
            type: "uuid", nullable: false, defaultValue: Guid.Empty);
        migrationBuilder.CreateIndex(name: "IX_tenant_purchase_orders_PreviousPurchaseOrderId",
            table: "tenant_purchase_orders", column: "PreviousPurchaseOrderId", unique: true,
            filter: "\"PreviousPurchaseOrderId\" <> '00000000-0000-0000-0000-000000000000'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_tenant_purchase_orders_PreviousPurchaseOrderId", table: "tenant_purchase_orders");
        migrationBuilder.DropColumn(name: "PreviousPurchaseOrderId", table: "tenant_purchase_orders");
    }
}
