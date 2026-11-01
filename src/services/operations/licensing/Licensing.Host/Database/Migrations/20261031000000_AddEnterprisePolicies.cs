#pragma warning disable IDE0005
using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Licensing.Host.Database.Migrations;

#pragma warning disable SA1649, SA1600, SA1503
/// <summary>Stores immutable signed enterprise policies and issuance audit events.</summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261031000000_AddEnterprisePolicies")]
public sealed class AddEnterprisePolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "enterprise_policies", columns: table => new
        {
            PolicyId = table.Column<string>(type: "text", nullable: false),
            PolicyVersion = table.Column<string>(type: "text", nullable: false),
            Document = table.Column<string>(type: "text", nullable: false),
            IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
            SourceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
            RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            RevokedBySubjectId = table.Column<string>(type: "text", nullable: true),
        }, constraints: table => table.PrimaryKey("PK_enterprise_policies", x => new { x.PolicyId, x.PolicyVersion }));
        migrationBuilder.CreateTable(name: "enterprise_policy_audit", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            PolicyId = table.Column<string>(type: "text", nullable: false),
            PolicyVersion = table.Column<string>(type: "text", nullable: false),
            ActorSubjectId = table.Column<string>(type: "text", nullable: false),
            Action = table.Column<string>(type: "text", nullable: false),
            AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_enterprise_policy_audit", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_enterprise_policies_PolicyId_TenantId_SubscriptionId_SourceOrderId_IssuedAtUtc", table: "enterprise_policies", columns: new[] { "PolicyId", "TenantId", "SubscriptionId", "SourceOrderId", "IssuedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_enterprise_policy_audit_PolicyId_PolicyVersion", table: "enterprise_policy_audit", columns: new[] { "PolicyId", "PolicyVersion" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "enterprise_policy_audit");
        migrationBuilder.DropTable(name: "enterprise_policies");
    }
}
#pragma warning restore SA1649
