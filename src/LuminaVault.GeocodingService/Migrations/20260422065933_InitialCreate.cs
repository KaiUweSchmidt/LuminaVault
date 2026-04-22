using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuminaVault.GeocodingService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeocodingCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LatitudeRounded = table.Column<double>(type: "double precision", nullable: false),
                    LongitudeRounded = table.Column<double>(type: "double precision", nullable: false),
                    LocationName = table.Column<string>(type: "text", nullable: true),
                    CachedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeocodingCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeocodingCache_LatitudeRounded_LongitudeRounded",
                table: "GeocodingCache",
                columns: new[] { "LatitudeRounded", "LongitudeRounded" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeocodingCache");
        }
    }
}
