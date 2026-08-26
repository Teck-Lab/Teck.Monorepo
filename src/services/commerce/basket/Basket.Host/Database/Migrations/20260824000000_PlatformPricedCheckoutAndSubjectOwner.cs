using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baskets.Host.Database.Migrations;

/// <inheritdoc />
public partial class PlatformPricedCheckoutAndSubjectOwner : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Subject", table: "Baskets", type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "AuthorizedAmount", table: "Baskets", type: "numeric", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>(name: "Currency", table: "Baskets", type: "character varying(3)", maxLength: 3, nullable: true);
        migrationBuilder.AddColumn<string>(name: "PaymentReference", table: "Baskets", type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>(name: "CheckoutRequestId", table: "Baskets", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "CheckoutFailure", table: "Baskets", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Baskets_TenantId_Subject_Status", table: "Baskets", columns: new[] { "TenantId", "Subject", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Baskets_TenantId_Subject_Status", table: "Baskets");
        migrationBuilder.DropColumn(name: "Subject", table: "Baskets");
        migrationBuilder.DropColumn(name: "AuthorizedAmount", table: "Baskets");
        migrationBuilder.DropColumn(name: "Currency", table: "Baskets");
        migrationBuilder.DropColumn(name: "PaymentReference", table: "Baskets");
        migrationBuilder.DropColumn(name: "CheckoutRequestId", table: "Baskets");
        migrationBuilder.DropColumn(name: "CheckoutFailure", table: "Baskets");
    }
}
