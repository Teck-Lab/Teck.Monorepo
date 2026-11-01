using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Adds durable serialized Billing authorization for external tenant purge work.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261030210000_AddTenantPurgeGrants")]
public partial class PostgreSQLAddTenantPurgeGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tenant_purge_grants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovalId = table.Column<Guid>(type: "uuid", nullable: false),
                AcquiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_tenant_purge_grants", row => row.Id));

        migrationBuilder.CreateIndex("IX_tenant_purge_grants_ApprovalId", "tenant_purge_grants", "ApprovalId", unique: true);
        migrationBuilder.CreateIndex("IX_tenant_purge_grants_SourceOrderId", "tenant_purge_grants", "SourceOrderId");
        migrationBuilder.CreateIndex("IX_tenant_purge_grants_TenantId_Active", "tenant_purge_grants", "TenantId", unique: true,
            filter: "\"CompletedAtUtc\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("tenant_purge_grants");
}
