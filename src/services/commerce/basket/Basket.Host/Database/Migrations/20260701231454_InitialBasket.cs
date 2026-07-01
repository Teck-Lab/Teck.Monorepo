using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baskets.Host.Database.Migrations;

/// <inheritdoc />
public partial class InitialBasket : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Baskets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                AnonymousToken = table.Column<Guid>(type: "uuid", nullable: true),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Subtotal = table.Column<decimal>(type: "numeric", nullable: false),
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
                table.PrimaryKey("PK_Baskets", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "BasketItems",
            columns: table => new
            {
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                BasketId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BasketItems", x => new { x.BasketId, x.ProductId });
                table.ForeignKey(
                    name: "FK_BasketItems_Baskets_BasketId",
                    column: x => x.BasketId,
                    principalTable: "Baskets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Baskets_TenantId_AnonymousToken_Status",
            table: "Baskets",
            columns: new[] { "TenantId", "AnonymousToken", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Baskets_TenantId_CustomerId_Status",
            table: "Baskets",
            columns: new[] { "TenantId", "CustomerId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BasketItems");

        migrationBuilder.DropTable(
            name: "Baskets");
    }
}
