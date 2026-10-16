using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProcessMediaInOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Media uploaded before this were processed in the upload request: all of them are ready.
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "media",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "ready");
            // The app always writes the status; the default was only for the existing rows.
            migrationBuilder.Sql("ALTER TABLE media ALTER COLUMN status DROP DEFAULT");

            migrationBuilder.CreateTable(
                name: "media_uploads",
                columns: table => new
                {
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    crop_x = table.Column<int>(type: "integer", nullable: true),
                    crop_y = table.Column<int>(type: "integer", nullable: true),
                    crop_size = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_uploads", x => x.media_id);
                    table.ForeignKey(
                        name: "fk_media_uploads_media_media_id",
                        column: x => x.media_id,
                        principalTable: "media",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_uploads");

            migrationBuilder.DropColumn(
                name: "status",
                table: "media");
        }
    }
}
