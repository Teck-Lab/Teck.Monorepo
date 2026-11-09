using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Retains Stripe creation uncertainty across process failure before the intent ID is bound.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261030220000_AddStripeIntentAttempt")]
public partial class PostgreSQLAddStripeIntentAttempt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "StripeIntentAttempted",
            table: "tenant_purchase_orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "StripeIntentAttemptedAtUtc",
            table: "tenant_purchase_orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StripeIntentAttemptedAtUtc", table: "tenant_purchase_orders");
        migrationBuilder.DropColumn(name: "StripeIntentAttempted", table: "tenant_purchase_orders");
    }
}
