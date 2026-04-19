using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuminaVault.AiTagging.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaggingResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    AnalyzedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaggingResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaggingResults_MediaId",
                table: "TaggingResults",
                column: "MediaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaggingResults");
        }
    }
}
