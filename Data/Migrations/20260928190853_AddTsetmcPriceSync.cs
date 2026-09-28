using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioManager.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTsetmcPriceSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Nav",
                table: "Prices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TsetmcInsCode",
                table: "Assets",
                type: "TEXT",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nav",
                table: "Prices");

            migrationBuilder.DropColumn(
                name: "TsetmcInsCode",
                table: "Assets");
        }
    }
}
