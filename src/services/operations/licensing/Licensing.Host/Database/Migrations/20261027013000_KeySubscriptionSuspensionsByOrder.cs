using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Licensing.Host.Database.Migrations;

#pragma warning disable SA1649 // Timestamped EF migration files retain stable migration IDs.
/// <summary>Retains an out-of-order revocation tombstone per paid annual order without blocking later captured periods.</summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261027013000_KeySubscriptionSuspensionsByOrder")]
public sealed class KeySubscriptionSuspensionsByOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_subscription_suspensions", table: "subscription_suspensions");
        migrationBuilder.AddPrimaryKey(name: "PK_subscription_suspensions", table: "subscription_suspensions", column: "SourceOrderId");
        migrationBuilder.CreateIndex(name: "IX_subscription_suspensions_SubscriptionId",
            table: "subscription_suspensions", column: "SubscriptionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_subscription_suspensions_SubscriptionId", table: "subscription_suspensions");
        migrationBuilder.DropPrimaryKey(name: "PK_subscription_suspensions", table: "subscription_suspensions");
        migrationBuilder.AddPrimaryKey(name: "PK_subscription_suspensions", table: "subscription_suspensions", column: "SubscriptionId");
    }
}
#pragma warning restore SA1649
