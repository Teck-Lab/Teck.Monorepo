using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orders.Host.Database.Migrations;

/// <inheritdoc />
public partial class CheckoutLifecycleStateAndSubjectOwner : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ActionText",
            table: "Orders",
            type: "character varying(512)",
            maxLength: 512,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "AuthorizedAmount",
            table: "Orders",
            type: "numeric",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<Guid>(
            name: "BasketId",
            table: "Orders",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<decimal>(
            name: "CapturedAmount",
            table: "Orders",
            type: "numeric",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "CheckoutCorrelationId",
            table: "Orders",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Currency",
            table: "Orders",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "XXX");

        migrationBuilder.AddColumn<int>(
            name: "FailureReason",
            table: "Orders",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "KeycloakSubjectId",
            table: "Orders",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<Guid>(
            name: "PaymentId",
            table: "Orders",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PaymentState",
            table: "Orders",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "ProcessedTransitionKeys",
            table: "Orders",
            type: "character varying(8192)",
            maxLength: 8192,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<bool>(
            name: "RequiresHumanDecision",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "RetryRequestId",
            table: "Orders",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "StockState",
            table: "Orders",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "AuthorizedAmount" = "Total",
                "CheckoutCorrelationId" = CONCAT('legacy:', REPLACE("Id"::text, '-', '')),
                "KeycloakSubjectId" = CONCAT('legacy-unowned:', "Id"::text)
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_TenantId_CheckoutCorrelationId",
            table: "Orders",
            columns: new[] { "TenantId", "CheckoutCorrelationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_TenantId_RetryRequestId",
            table: "Orders",
            columns: new[] { "TenantId", "RetryRequestId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ActionText",
            table: "Orders");

        migrationBuilder.DropIndex(
            name: "IX_Orders_TenantId_CheckoutCorrelationId",
            table: "Orders");

        migrationBuilder.DropIndex(
            name: "IX_Orders_TenantId_RetryRequestId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "AuthorizedAmount",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "BasketId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "CapturedAmount",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "CheckoutCorrelationId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "Currency",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "FailureReason",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "KeycloakSubjectId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "PaymentId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "PaymentState",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "ProcessedTransitionKeys",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "RequiresHumanDecision",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "RetryRequestId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "StockState",
            table: "Orders");
    }
}
