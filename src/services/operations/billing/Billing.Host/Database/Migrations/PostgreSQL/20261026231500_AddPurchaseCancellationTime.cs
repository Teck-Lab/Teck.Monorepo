using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Records the Billing-authorized start of subscription access suspension.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261026231500_AddPurchaseCancellationTime")]
public partial class AddPurchaseCancellationTime : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "CancelledAtUtc", table: "tenant_purchase_orders",
            type: "timestamp with time zone", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "tenant_purchase_orders");
    }
}
