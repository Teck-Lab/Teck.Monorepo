using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pricing.Host.Database.Migrations;

/// <inheritdoc />
public partial class AddCatalogFallbackReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "CatalogPrices", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), ProductId = table.Column<Guid>(type: "uuid", nullable: false), VariantId = table.Column<Guid>(type: "uuid", nullable: false), Amount = table.Column<decimal>(type: "numeric", nullable: false), Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false), ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), CreatedBy = table.Column<string>(type: "text", nullable: true), UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), UpdatedBy = table.Column<string>(type: "text", nullable: true), DeletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), DeletedBy = table.Column<string>(type: "text", nullable: true), IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_CatalogPrices", x => x.Id));
        migrationBuilder.CreateTable(name: "PendingPriceResolutions", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), ProductId = table.Column<Guid>(type: "uuid", nullable: false), BasketId = table.Column<Guid>(type: "uuid", nullable: false), AuthorizedAmount = table.Column<decimal>(type: "numeric", nullable: false), Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false), RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false), SourceCorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false), LinesJson = table.Column<string>(type: "text", nullable: false), IsResolved = table.Column<bool>(type: "boolean", nullable: false), TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), CreatedBy = table.Column<string>(type: "text", nullable: true), UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), UpdatedBy = table.Column<string>(type: "text", nullable: true), DeletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), DeletedBy = table.Column<string>(type: "text", nullable: true), IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_PendingPriceResolutions", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_CatalogPrices_TenantId_ProductId", table: "CatalogPrices", columns: new[] { "TenantId", "ProductId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_PendingPriceResolutions_TenantId_RequestId", table: "PendingPriceResolutions", columns: new[] { "TenantId", "RequestId" }, unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PendingPriceResolutions");
        migrationBuilder.DropTable(name: "CatalogPrices");
    }
}
