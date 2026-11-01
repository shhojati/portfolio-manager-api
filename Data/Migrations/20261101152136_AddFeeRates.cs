using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioManager.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeeRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Class = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BuyPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    SellPercent = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeeRates_Class_EffectiveFrom",
                table: "FeeRates",
                columns: new[] { "Class", "EffectiveFrom" },
                unique: true);

            // The rates the mobile app had built in until now: the SEO's for online trades as brokers published them in
            // 1405 (learning.charisma.ir, rade.ir), an equity fund's for ETFs and a gold coin certificate's for
            // certificates. From 2000 so every trade has one; admins add later rates from the day they change.
            migrationBuilder.InsertData(
                table: "FeeRates",
                columns: ["Class", "EffectiveFrom", "BuyPercent", "SellPercent"],
                values: new object[,]
                {
                    { "stockTse", new DateOnly(2000, 1, 1), 0.3712m, 0.88m },
                    { "stockIfb", new DateOnly(2000, 1, 1), 0.3632m, 0.88m },
                    { "etf", new DateOnly(2000, 1, 1), 0.2245m, 0.23m },
                    { "option", new DateOnly(2000, 1, 1), 0.103m, 0.103m },
                    { "certificate", new DateOnly(2000, 1, 1), 0.125m, 0.125m },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeeRates");
        }
    }
}
