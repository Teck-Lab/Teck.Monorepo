using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Persists annual entitlement start independently of capture time for prepaid renewals.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261027010000_AddPurchasePaidPeriodStart")]
public sealed class AddPurchasePaidPeriodStart : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "ValidFromUtc", table: "tenant_purchase_orders",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.Sql("UPDATE tenant_purchase_orders SET \"ValidFromUtc\" = \"ConfirmedAtUtc\" WHERE \"ConfirmedAtUtc\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ValidFromUtc", table: "tenant_purchase_orders");
    }
}
