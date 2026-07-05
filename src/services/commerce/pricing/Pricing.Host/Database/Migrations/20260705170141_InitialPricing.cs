using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pricing.Host.Database.Migrations;

/// <inheritdoc />
public partial class InitialPricing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExchangeRates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FromCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ToCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Rate = table.Column<decimal>(type: "numeric", nullable: false),
                ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                table.PrimaryKey("PK_ExchangeRates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PriceLists",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                CustomerGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("PK_PriceLists", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Prices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                table.PrimaryKey("PK_Prices", x => x.Id);
                table.ForeignKey(
                    name: "FK_Prices_PriceLists_PriceListId",
                    column: x => x.PriceListId,
                    principalTable: "PriceLists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PriceTiers",
            columns: table => new
            {
                MinQuantity = table.Column<int>(type: "integer", nullable: false),
                PriceId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PriceTiers", x => new { x.PriceId, x.MinQuantity });
                table.ForeignKey(
                    name: "FK_PriceTiers_Prices_PriceId",
                    column: x => x.PriceId,
                    principalTable: "Prices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ExchangeRates_TenantId_FromCurrency_ToCurrency",
            table: "ExchangeRates",
            columns: new[] { "TenantId", "FromCurrency", "ToCurrency" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PriceLists_TenantId_Status",
            table: "PriceLists",
            columns: new[] { "TenantId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Prices_PriceListId",
            table: "Prices",
            column: "PriceListId");

        migrationBuilder.CreateIndex(
            name: "IX_Prices_TenantId_ProductId",
            table: "Prices",
            columns: new[] { "TenantId", "ProductId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ExchangeRates");

        migrationBuilder.DropTable(
            name: "PriceTiers");

        migrationBuilder.DropTable(
            name: "Prices");

        migrationBuilder.DropTable(
            name: "PriceLists");
    }
}
