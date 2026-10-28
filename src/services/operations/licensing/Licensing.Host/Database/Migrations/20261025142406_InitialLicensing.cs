using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Host.Database.Migrations;

/// <inheritdoc />
public partial class InitialLicensing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "licenses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PlanVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LicenseDocument = table.Column<string>(type: "text", nullable: false),
                SigningKeyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Revoked = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_licenses", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_licenses_SubscriptionId",
            table: "licenses",
            column: "SubscriptionId",
            unique: true,
            filter: "NOT \"Revoked\"");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "licenses");
    }
}
