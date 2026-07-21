using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Customers.Host.Database.Migrations;

/// <inheritdoc />
public partial class AddCustomerProfiles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                KeycloakSubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: true),
                UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<string>(type: "text", nullable: true),
                DeletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeletedBy = table.Column<string>(type: "text", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "addresses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Line1 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Line2 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PostalCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Country = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: true),
                UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<string>(type: "text", nullable: true),
                DeletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeletedBy = table.Column<string>(type: "text", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_addresses", x => x.Id);
                table.ForeignKey(
                    name: "FK_addresses_customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_addresses_CustomerId",
            table: "addresses",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_customers_KeycloakSubjectId",
            table: "customers",
            column: "KeycloakSubjectId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "addresses");

        migrationBuilder.DropTable(
            name: "customers");
    }
}
