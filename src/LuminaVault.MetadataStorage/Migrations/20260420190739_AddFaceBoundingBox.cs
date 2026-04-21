using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuminaVault.MetadataStorage.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceBoundingBox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BboxHeight",
                table: "Faces",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BboxWidth",
                table: "Faces",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BboxX",
                table: "Faces",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BboxY",
                table: "Faces",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BboxHeight",
                table: "Faces");

            migrationBuilder.DropColumn(
                name: "BboxWidth",
                table: "Faces");

            migrationBuilder.DropColumn(
                name: "BboxX",
                table: "Faces");

            migrationBuilder.DropColumn(
                name: "BboxY",
                table: "Faces");
        }
    }
}
