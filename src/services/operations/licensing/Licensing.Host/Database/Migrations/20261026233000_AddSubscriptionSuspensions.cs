using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Licensing.Host.Database.Migrations;

/// <summary>Persists verified Billing suspensions before or after signed license issuance.</summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261026233000_AddSubscriptionSuspensions")]
public partial class AddSubscriptionSuspensions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subscription_suspensions",
            columns: table => new
            {
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                SuspendedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_subscription_suspensions", x => x.SubscriptionId));
        migrationBuilder.CreateIndex(name: "IX_subscription_suspensions_TenantId", table: "subscription_suspensions", column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "subscription_suspensions");
    }
}
