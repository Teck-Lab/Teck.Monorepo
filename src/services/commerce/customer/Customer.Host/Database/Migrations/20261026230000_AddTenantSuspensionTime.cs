using Customers.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Customers.Host.Database.Migrations;

/// <summary>Records the immutable Billing-authorized time that tenant access was suspended.</summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261026230000_AddTenantSuspensionTime")]
#pragma warning disable SA1649
public sealed class AddTenantSuspensionTime : Migration
{
#pragma warning restore SA1649
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "SuspendedAtUtc", table: "tenants",
            type: "timestamp with time zone", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SuspendedAtUtc", table: "tenants");
    }
}
