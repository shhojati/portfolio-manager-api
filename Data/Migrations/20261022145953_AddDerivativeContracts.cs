using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioManager.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivativeContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DerivativeContracts",
                columns: table => new
                {
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    StrikePrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ContractSize = table.Column<int>(type: "INTEGER", nullable: false),
                    UnderlyingAssetId = table.Column<int>(type: "INTEGER", nullable: true),
                    UnderlyingSymbol = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivativeContracts", x => x.AssetId);
                    table.ForeignKey(
                        name: "FK_DerivativeContracts_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DerivativeContracts_Assets_UnderlyingAssetId",
                        column: x => x.UnderlyingAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prices_Date",
                table: "Prices",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_DerivativeContracts_UnderlyingAssetId",
                table: "DerivativeContracts",
                column: "UnderlyingAssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DerivativeContracts");

            migrationBuilder.DropIndex(
                name: "IX_Prices_Date",
                table: "Prices");
        }
    }
}
