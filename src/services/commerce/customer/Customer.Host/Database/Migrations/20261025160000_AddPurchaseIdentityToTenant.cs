using Customers.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Customers.Host.Database.Migrations;

/// <summary>Adds reservation owner and confirmed-purchase correlation columns.</summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261025160000_AddPurchaseIdentityToTenant")]
#pragma warning disable SA1649
public sealed class AddPurchaseIdentityToTenant : Migration
{
    #pragma warning restore SA1649
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "OwnerSubjectId", table: "tenants", type: "character varying(128)", maxLength: 128, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "PlanId", table: "tenants", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "PlanVersion", table: "tenants", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "PurchaseOrderId", table: "tenants", type: "uuid", nullable: false, defaultValue: Guid.Empty);
        migrationBuilder.AddColumn<Guid>(name: "SubscriptionId", table: "tenants", type: "uuid", nullable: false, defaultValue: Guid.Empty);
        migrationBuilder.AddColumn<Guid>(name: "KeycloakOrganizationId", table: "tenants", type: "uuid", nullable: true);
        migrationBuilder.Sql("UPDATE tenants SET \"KeycloakOrganizationId\" = \"Id\" WHERE \"Status\" = 'active'");
        migrationBuilder.CreateIndex(name: "IX_tenants_KeycloakOrganizationId", table: "tenants", column: "KeycloakOrganizationId", unique: true);
        migrationBuilder.AddColumn<bool>(name: "LicenseReady", table: "tenants", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "DatabaseReady", table: "tenants", type: "boolean", nullable: false, defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_tenants_KeycloakOrganizationId", table: "tenants");
        migrationBuilder.DropColumn(name: "KeycloakOrganizationId", table: "tenants");
        migrationBuilder.DropColumn(name: "SubscriptionId", table: "tenants");
        migrationBuilder.DropColumn(name: "LicenseReady", table: "tenants");
        migrationBuilder.DropColumn(name: "DatabaseReady", table: "tenants");
        migrationBuilder.DropColumn(name: "OwnerSubjectId", table: "tenants");
        migrationBuilder.DropColumn(name: "PlanId", table: "tenants");
        migrationBuilder.DropColumn(name: "PlanVersion", table: "tenants");
        migrationBuilder.DropColumn(name: "PurchaseOrderId", table: "tenants");
    }
}
