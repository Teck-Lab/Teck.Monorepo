using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Customers.Host.Database.Migrations;

/// <inheritdoc />
public partial class InitialCustomer : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tenants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DatabaseStrategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DatabaseProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                HasReadReplicas = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                table.PrimaryKey("PK_tenants", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "tenants",
            columns: new[] { "Id", "CreatedAt", "CreatedBy", "DatabaseProvider", "DatabaseStrategy", "DeletedBy", "DeletedOn", "HasReadReplicas", "Identifier", "IsDeleted", "Status", "UpdatedBy", "UpdatedOn" },
            values: new object[] { new Guid("00000000-0000-0000-0000-0000000000a1"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "postgres", "shared", null, null, false, "dev", false, "active", null, null });

        migrationBuilder.CreateIndex(
            name: "IX_tenants_Identifier",
            table: "tenants",
            column: "Identifier",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tenants");
    }
}
