using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfileBanners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "banner_crop_width",
                table: "media_uploads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "banner_crop_x",
                table: "media_uploads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "banner_crop_y",
                table: "media_uploads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "banner_media_id",
                table: "actors",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_actors_banner_media_id",
                table: "actors",
                column: "banner_media_id");

            migrationBuilder.AddForeignKey(
                name: "fk_actors_media_item_banner_media_id",
                table: "actors",
                column: "banner_media_id",
                principalTable: "media",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_actors_media_item_banner_media_id",
                table: "actors");

            migrationBuilder.DropIndex(
                name: "ix_actors_banner_media_id",
                table: "actors");

            migrationBuilder.DropColumn(
                name: "banner_crop_width",
                table: "media_uploads");

            migrationBuilder.DropColumn(
                name: "banner_crop_x",
                table: "media_uploads");

            migrationBuilder.DropColumn(
                name: "banner_crop_y",
                table: "media_uploads");

            migrationBuilder.DropColumn(
                name: "banner_media_id",
                table: "actors");
        }
    }
}
