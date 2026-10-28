using Customers.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Customers.Host.Database.Migrations;

/// <summary>Records the exclusive end of the Billing-attested paid period for immediate access denial.</summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261027000000_AddTenantPaidPeriodEnd")]
#pragma warning disable SA1649
public sealed class AddTenantPaidPeriodEnd : Migration
{
#pragma warning restore SA1649
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "ValidUntilUtc", table: "tenants",
            type: "timestamp with time zone", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ValidUntilUtc", table: "tenants");
    }
}
