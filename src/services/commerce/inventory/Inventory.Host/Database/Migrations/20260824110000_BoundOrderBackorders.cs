using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventories.Host.Database.Migrations;

/// <inheritdoc />
public partial class BoundOrderBackorders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "BackorderExpiresAt", table: "Reservations", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "BackorderExpiredOutcomeKey", table: "Reservations", type: "character varying(160)", maxLength: 160, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BackorderReadyOutcomeKey", table: "Reservations", type: "character varying(160)", maxLength: 160, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "BasketId", table: "Reservations", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "SourceCorrelationId", table: "Reservations", type: "character varying(128)", maxLength: 128, nullable: false, defaultValue: string.Empty);
        migrationBuilder.CreateIndex(name: "IX_Reservations_Status_BackorderExpiresAt", table: "Reservations", columns: new[] { "Status", "BackorderExpiresAt" });
        migrationBuilder.CreateIndex(name: "IX_Reservations_TenantId_SourceCorrelationId", table: "Reservations", columns: new[] { "TenantId", "SourceCorrelationId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Reservations_Status_BackorderExpiresAt", table: "Reservations");
        migrationBuilder.DropIndex(name: "IX_Reservations_TenantId_SourceCorrelationId", table: "Reservations");
        migrationBuilder.DropColumn(name: "BackorderExpiresAt", table: "Reservations");
        migrationBuilder.DropColumn(name: "BackorderExpiredOutcomeKey", table: "Reservations");
        migrationBuilder.DropColumn(name: "BackorderReadyOutcomeKey", table: "Reservations");
        migrationBuilder.DropColumn(name: "BasketId", table: "Reservations");
        migrationBuilder.DropColumn(name: "SourceCorrelationId", table: "Reservations");
    }
}
