using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "avatar_media_id",
                table: "actors",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_actors_owner_actor_id",
                        column: x => x.owner_actor_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beep_media",
                columns: table => new
                {
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beep_media", x => new { x.beep_id, x.media_id });
                    table.ForeignKey(
                        name: "fk_beep_media_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_beep_media_media_item_media_id",
                        column: x => x.media_id,
                        principalTable: "media",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actors_avatar_media_id",
                table: "actors",
                column: "avatar_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_beep_media_media_id",
                table: "beep_media",
                column: "media_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_created_at",
                table: "media",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_media_owner_actor_id",
                table: "media",
                column: "owner_actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_storage_key",
                table: "media",
                column: "storage_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_actors_media_item_avatar_media_id",
                table: "actors",
                column: "avatar_media_id",
                principalTable: "media",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_actors_media_item_avatar_media_id",
                table: "actors");

            migrationBuilder.DropTable(
                name: "beep_media");

            migrationBuilder.DropTable(
                name: "media");

            migrationBuilder.DropIndex(
                name: "ix_actors_avatar_media_id",
                table: "actors");

            migrationBuilder.DropColumn(
                name: "avatar_media_id",
                table: "actors");
        }
    }
}
