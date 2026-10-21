using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MediaPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "media",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "beep");

            // Uploads used to serve any purpose. Those set as an avatar or banner keep that use; an image also attached
            // to a beep stays a beep image, which still has every size it was stored in, so it shows wherever it is used.
            migrationBuilder.Sql("""
                UPDATE media m SET purpose = 'banner'
                FROM actors a WHERE a.banner_media_id = m.id
                  AND NOT EXISTS (SELECT 1 FROM beep_media bm WHERE bm.media_id = m.id);
                UPDATE media m SET purpose = 'avatar'
                FROM actors a WHERE a.avatar_media_id = m.id AND m.purpose = 'beep'
                  AND NOT EXISTS (SELECT 1 FROM beep_media bm WHERE bm.media_id = m.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purpose",
                table: "media");
        }
    }
}
