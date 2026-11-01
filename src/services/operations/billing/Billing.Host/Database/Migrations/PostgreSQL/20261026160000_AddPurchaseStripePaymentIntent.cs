using Billings.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billings.Host.Database.Migrations.PostgreSQL;

/// <summary>Adds the provider correlation required before a purchase can be marked paid.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261026160000_AddPurchaseStripePaymentIntent")]
public partial class AddPurchaseStripePaymentIntent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StripePaymentIntentId",
            table: "tenant_purchase_orders",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_tenant_purchase_orders_StripePaymentIntentId",
            table: "tenant_purchase_orders",
            column: "StripePaymentIntentId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tenant_purchase_orders_StripePaymentIntentId",
            table: "tenant_purchase_orders");
        migrationBuilder.DropColumn(
            name: "StripePaymentIntentId",
            table: "tenant_purchase_orders");
    }
}
