using Customers.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Customers.Host.Database.Migrations;

/// <summary>Persists the currently attached, signed enterprise policy for a paid tenant.</summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261101140000_AddTenantEnterprisePolicy")]
public partial class AddTenantEnterprisePolicy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "EnterprisePolicyId", table: "tenants", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "EnterprisePolicyVersion", table: "tenants", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<int>(name: "EnterprisePolicyGracePeriodDays", table: "tenants", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(name: "EnterprisePolicyBackupRetentionDays", table: "tenants", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "EnterprisePolicySigningKeyId", table: "tenants", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "EnterprisePolicySignature", table: "tenants", type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "EnterprisePolicyValidFromUtc", table: "tenants", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "EnterprisePolicyValidUntilUtc", table: "tenants", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "EnterprisePolicyTransitionProof", table: "tenants", type: "text", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "EnterprisePolicySourceOrderId", table: "tenants", type: "uuid", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EnterprisePolicyId", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicyVersion", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicyGracePeriodDays", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicyBackupRetentionDays", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicySigningKeyId", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicySignature", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicyValidFromUtc", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicyValidUntilUtc", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicyTransitionProof", table: "tenants");
        migrationBuilder.DropColumn(name: "EnterprisePolicySourceOrderId", table: "tenants");
    }
}
