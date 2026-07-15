using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventories.Host.Database.Migrations;

/// <inheritdoc />
public partial class InitialInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StockItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                QuantityOnHand = table.Column<int>(type: "integer", nullable: false),
                QuantityReserved = table.Column<int>(type: "integer", nullable: false),
                AllowBackorder = table.Column<bool>(type: "boolean", nullable: false),
                ReorderThreshold = table.Column<int>(type: "integer", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                table.PrimaryKey("PK_StockItems", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StockItems_TenantId_ProductId_LocationId",
            table: "StockItems",
            columns: new[] { "TenantId", "ProductId", "LocationId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "StockItems");
    }
}
