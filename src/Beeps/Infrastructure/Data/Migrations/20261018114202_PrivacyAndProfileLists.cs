using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyAndProfileLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_likes_actor_id",
                table: "likes");

            migrationBuilder.DropIndex(
                name: "ix_follows_followee_id",
                table: "follows");

            migrationBuilder.AddColumn<bool>(
                name: "hide_follow_counts",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "hide_follows",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "hide_likes",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_likes_actor_id_created_at_beep_id",
                table: "likes",
                columns: new[] { "actor_id", "created_at", "beep_id" });

            migrationBuilder.CreateIndex(
                name: "ix_follows_followee_id_created_at_follower_id",
                table: "follows",
                columns: new[] { "followee_id", "created_at", "follower_id" });

            migrationBuilder.CreateIndex(
                name: "ix_follows_follower_id_created_at_followee_id",
                table: "follows",
                columns: new[] { "follower_id", "created_at", "followee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_likes_actor_id_created_at_beep_id",
                table: "likes");

            migrationBuilder.DropIndex(
                name: "ix_follows_followee_id_created_at_follower_id",
                table: "follows");

            migrationBuilder.DropIndex(
                name: "ix_follows_follower_id_created_at_followee_id",
                table: "follows");

            migrationBuilder.DropColumn(
                name: "hide_follow_counts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "hide_follows",
                table: "users");

            migrationBuilder.DropColumn(
                name: "hide_likes",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "ix_likes_actor_id",
                table: "likes",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_follows_followee_id",
                table: "follows",
                column: "followee_id");
        }
    }
}
