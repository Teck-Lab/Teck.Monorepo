using Customers.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Customers.Host.Database.Migrations;

/// <summary>Persists immutable independent Keycloak operator decisions for one paid tenant period.</summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261029100000_AddTenantPurgeApprovals")]
#pragma warning disable SA1649
public sealed class AddTenantPurgeApprovals : Migration
{
#pragma warning restore SA1649
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "tenant_purge_approvals", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            SourceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
            SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
            ApproverSubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_tenant_purge_approvals", row => row.Id));
        migrationBuilder.CreateIndex(name: "IX_tenant_purge_approvals_TenantId_SourceOrderId_ApproverSubjectId",
            table: "tenant_purge_approvals", columns: ["TenantId", "SourceOrderId", "ApproverSubjectId"], unique: true);
        migrationBuilder.CreateIndex(name: "IX_tenant_purge_approvals_TenantId_SourceOrderId",
            table: "tenant_purge_approvals", columns: ["TenantId", "SourceOrderId"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("tenant_purge_approvals");
    }
}
