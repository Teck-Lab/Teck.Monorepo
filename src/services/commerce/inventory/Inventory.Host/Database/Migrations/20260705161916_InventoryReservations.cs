using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventories.Host.Database.Migrations;

/// <inheritdoc />
public partial class InventoryReservations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LocationPriorities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LocationIds = table.Column<string>(type: "text", nullable: false),
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
                table.PrimaryKey("PK_LocationPriorities", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Reservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceType = table.Column<int>(type: "integer", nullable: false),
                SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("PK_Reservations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ReservationLines",
            columns: table => new
            {
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestedQuantity = table.Column<int>(type: "integer", nullable: false),
                BackorderedQuantity = table.Column<int>(type: "integer", nullable: false),
                Allocations = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReservationLines", x => new { x.ReservationId, x.ProductId });
                table.ForeignKey(
                    name: "FK_ReservationLines_Reservations_ReservationId",
                    column: x => x.ReservationId,
                    principalTable: "Reservations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LocationPriorities_TenantId",
            table: "LocationPriorities",
            column: "TenantId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Reservations_Status_ExpiresAt",
            table: "Reservations",
            columns: new[] { "Status", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Reservations_TenantId_SourceType_SourceId",
            table: "Reservations",
            columns: new[] { "TenantId", "SourceType", "SourceId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "LocationPriorities");

        migrationBuilder.DropTable(
            name: "ReservationLines");

        migrationBuilder.DropTable(
            name: "Reservations");
    }
}
