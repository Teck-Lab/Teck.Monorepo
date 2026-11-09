using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teck.TenantDatabase.Host.Database.Migrations;

/// <inheritdoc />
public partial class SuspensionReason : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "suspension_reason",
            table: "tenant_database_requests",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "suspension_reason",
            table: "tenant_database_requests");
    }
}
