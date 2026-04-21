using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuminaVault.MetadataStorage.Migrations
{
    /// <inheritdoc />
    public partial class AddGpsLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GpsLocation",
                table: "MediaMetadata",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GpsLocation",
                table: "MediaMetadata");
        }
    }
}
