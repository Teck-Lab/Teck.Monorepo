using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Host.Database.Migrations;

/// <inheritdoc />
public partial class InitialCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ParentId = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("PK_Categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Products",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("PK_Products", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Suppliers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                ContactPhone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                table.PrimaryKey("PK_Suppliers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Variants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SellPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                SellPriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_Variants", x => x.Id);
                table.ForeignKey(
                    name: "FK_Variants_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VariantAttributes",
            columns: table => new
            {
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariantAttributes", x => new { x.VariantId, x.Name });
                table.ForeignKey(
                    name: "FK_VariantAttributes_Variants_VariantId",
                    column: x => x.VariantId,
                    principalTable: "Variants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VariantSuppliers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                CostPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CostPriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                SupplierSku = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                LeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                MinOrderQuantity = table.Column<int>(type: "integer", nullable: false),
                IsPreferred = table.Column<bool>(type: "boolean", nullable: false),
                VariantId = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_VariantSuppliers", x => x.Id);
                table.ForeignKey(
                    name: "FK_VariantSuppliers_Variants_VariantId",
                    column: x => x.VariantId,
                    principalTable: "Variants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SupplierPriceHistory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CostPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CostPriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                VariantSupplierId = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_SupplierPriceHistory", x => x.Id);
                table.ForeignKey(
                    name: "FK_SupplierPriceHistory_VariantSuppliers_VariantSupplierId",
                    column: x => x.VariantSupplierId,
                    principalTable: "VariantSuppliers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Categories_TenantId_Slug",
            table: "Categories",
            columns: new[] { "TenantId", "Slug" });

        migrationBuilder.CreateIndex(
            name: "IX_SupplierPriceHistory_VariantSupplierId",
            table: "SupplierPriceHistory",
            column: "VariantSupplierId");

        migrationBuilder.CreateIndex(
            name: "IX_Variants_ProductId",
            table: "Variants",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_VariantSuppliers_VariantId",
            table: "VariantSuppliers",
            column: "VariantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Categories");

        migrationBuilder.DropTable(
            name: "SupplierPriceHistory");

        migrationBuilder.DropTable(
            name: "Suppliers");

        migrationBuilder.DropTable(
            name: "VariantAttributes");

        migrationBuilder.DropTable(
            name: "VariantSuppliers");

        migrationBuilder.DropTable(
            name: "Variants");

        migrationBuilder.DropTable(
            name: "Products");
    }
}
