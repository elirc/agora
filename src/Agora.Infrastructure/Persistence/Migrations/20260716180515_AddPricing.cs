using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPricing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sale_campaigns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DiscountKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_campaigns", x => x.Id);
                table.ForeignKey(
                    name: "FK_sale_campaigns_products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sale_campaigns_ProductId",
            table: "sale_campaigns",
            column: "ProductId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sale_campaigns");
    }
}
