using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuminaVault.MetadataStorage.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineStepStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineStatuses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStatuses_MediaId",
                table: "PipelineStatuses",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStatuses_MediaId_StepName",
                table: "PipelineStatuses",
                columns: new[] { "MediaId", "StepName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineStatuses");
        }
    }
}
