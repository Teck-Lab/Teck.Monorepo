using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Adds independent paid Barman backup and HA entitlement snapshots to purchase orders.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261031120000_AddPurchaseEntitlements")]
public partial class PostgreSQLAddPurchaseEntitlements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "HasScheduledBackups",
            table: "tenant_purchase_orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);
        migrationBuilder.AddColumn<bool>(
            name: "HasHighAvailability",
            table: "tenant_purchase_orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "HasHighAvailability", table: "tenant_purchase_orders");
        migrationBuilder.DropColumn(name: "HasScheduledBackups", table: "tenant_purchase_orders");
    }
}
