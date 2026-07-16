using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCart : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "carts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: true),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_carts", x => x.Id);
                table.ForeignKey(
                    name: "FK_carts_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "cart_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CartId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                AddedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PriceAtAdditionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PriceAtAdditionCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cart_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_cart_lines_carts_CartId",
                    column: x => x.CartId,
                    principalTable: "carts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_cart_lines_products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_cart_lines_CartId_ProductId",
            table: "cart_lines",
            columns: new[] { "CartId", "ProductId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_cart_lines_ProductId",
            table: "cart_lines",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_carts_TokenHash",
            table: "carts",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_carts_UserId",
            table: "carts",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cart_lines");

        migrationBuilder.DropTable(
            name: "carts");
    }
}
