using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billings.Host.Database.Migrations.PostgreSQL;
    /// <inheritdoc />
    public partial class AddEnterprisePolicyTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EffectiveBackupRetentionDays",
                table: "tenant_purchase_orders",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "EffectiveGracePeriodDays",
                table: "tenant_purchase_orders",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<string>(
                name: "EnterprisePolicyId",
                table: "tenant_purchase_orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnterprisePolicyVersion",
                table: "tenant_purchase_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveBackupRetentionDays",
                table: "tenant_purchase_orders");

            migrationBuilder.DropColumn(
                name: "EffectiveGracePeriodDays",
                table: "tenant_purchase_orders");

            migrationBuilder.DropColumn(
                name: "EnterprisePolicyId",
                table: "tenant_purchase_orders");

            migrationBuilder.DropColumn(
                name: "EnterprisePolicyVersion",
                table: "tenant_purchase_orders");
        }
    }
