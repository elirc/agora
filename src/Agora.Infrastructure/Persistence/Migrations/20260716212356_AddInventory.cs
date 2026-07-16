using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "reservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                CartId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                ReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "stock_levels",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                OnHand = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_levels", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_reservations_CartId",
            table: "reservations",
            column: "CartId");

        migrationBuilder.CreateIndex(
            name: "IX_reservations_ProductId",
            table: "reservations",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_reservations_ReleasedAtUtc",
            table: "reservations",
            column: "ReleasedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_stock_levels_ProductId",
            table: "stock_levels",
            column: "ProductId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "reservations");

        migrationBuilder.DropTable(
            name: "stock_levels");
    }
}
