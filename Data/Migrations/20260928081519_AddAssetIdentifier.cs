using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioManager.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetIdentifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Identifier",
                table: "Assets",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // existing rows would all share "" and break the unique index, so seed them from Symbol (already unique)
            migrationBuilder.Sql("UPDATE Assets SET Identifier = Symbol;");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Identifier",
                table: "Assets",
                column: "Identifier",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_Identifier",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Identifier",
                table: "Assets");
        }
    }
}
