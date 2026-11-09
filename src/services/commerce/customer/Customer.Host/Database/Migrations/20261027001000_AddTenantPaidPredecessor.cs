using Customers.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Customers.Host.Database.Migrations;

/// <summary>Remembers the prior Billing order of the current paid period to reject changed-order replays.</summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261027001000_AddTenantPaidPredecessor")]
#pragma warning disable SA1649
public sealed class AddTenantPaidPredecessor : Migration
{
#pragma warning restore SA1649
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "PreviousPaidOrderId", table: "tenants",
            type: "uuid", nullable: false, defaultValue: Guid.Empty);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PreviousPaidOrderId", table: "tenants");
    }
}
