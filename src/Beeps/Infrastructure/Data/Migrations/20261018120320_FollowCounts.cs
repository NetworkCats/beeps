using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FollowCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "followers_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "following_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The follows made so far, counted once as EfProfileStore.RecountFollowsAsync counts them (only shown humans);
            // from here on follows, unfollows, bans and deletions keep the counts.
            migrationBuilder.Sql("""
                UPDATE users AS u SET
                    following_count = (
                        SELECT count(*) FROM follows AS f
                        WHERE f.follower_id = u.id AND EXISTS (
                            SELECT 1 FROM actors AS a JOIN users AS v ON v.id = a.id
                            WHERE a.id = f.followee_id AND a.deleted_at IS NULL AND v.banned_at IS NULL)),
                    followers_count = (
                        SELECT count(*) FROM follows AS f
                        WHERE f.followee_id = u.id AND EXISTS (
                            SELECT 1 FROM actors AS a JOIN users AS v ON v.id = a.id
                            WHERE a.id = f.follower_id AND a.deleted_at IS NULL AND v.banned_at IS NULL))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "followers_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "following_count",
                table: "users");
        }
    }
}
