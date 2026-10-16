using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBeeps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "beeps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    reply_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moderation_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    like_count = table.Column<int>(type: "integer", nullable: false),
                    reply_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beeps", x => x.id);
                    table.ForeignKey(
                        name: "fk_beeps_actors_author_id",
                        column: x => x.author_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_beeps_beeps_reply_to_id",
                        column: x => x.reply_to_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_beeps_beeps_root_id",
                        column: x => x.root_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "follows",
                columns: table => new
                {
                    follower_id = table.Column<Guid>(type: "uuid", nullable: false),
                    followee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follows", x => new { x.follower_id, x.followee_id });
                    table.CheckConstraint("ck_follows_not_self", "follower_id <> followee_id");
                    table.ForeignKey(
                        name: "fk_follows_user_followee_id",
                        column: x => x.followee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_follows_user_follower_id",
                        column: x => x.follower_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hashtags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hashtags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "likes",
                columns: table => new
                {
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_likes", x => new { x.beep_id, x.actor_id });
                    table.ForeignKey(
                        name: "fk_likes_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_likes_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mentions",
                columns: table => new
                {
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentions", x => new { x.beep_id, x.actor_id });
                    table.ForeignKey(
                        name: "fk_mentions_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentions_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    from_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_actors_from_actor_id",
                        column: x => x.from_actor_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_actors_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beep_hashtags",
                columns: table => new
                {
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hashtag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beep_hashtags", x => new { x.beep_id, x.hashtag_id });
                    table.ForeignKey(
                        name: "fk_beep_hashtags_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_beep_hashtags_hashtag_hashtag_id",
                        column: x => x.hashtag_id,
                        principalTable: "hashtags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_beep_hashtags_hashtag_id_beep_id",
                table: "beep_hashtags",
                columns: new[] { "hashtag_id", "beep_id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_beeps_author_id_id",
                table: "beeps",
                columns: new[] { "author_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_beeps_public",
                table: "beeps",
                column: "id",
                descending: new bool[0],
                filter: "reply_to_id IS NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_beeps_reply_to_id",
                table: "beeps",
                column: "reply_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_beeps_root_id_id",
                table: "beeps",
                columns: new[] { "root_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_follows_followee_id",
                table: "follows",
                column: "followee_id");

            migrationBuilder.CreateIndex(
                name: "ux_hashtags_name",
                table: "hashtags",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_likes_actor_id",
                table: "likes",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentions_actor_id",
                table: "mentions",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_beep_id",
                table: "notifications",
                column: "beep_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_from_actor_id",
                table: "notifications",
                column: "from_actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_inbox",
                table: "notifications",
                columns: new[] { "recipient_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread",
                table: "notifications",
                columns: new[] { "recipient_id", "id" },
                descending: new[] { false, true },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notifications_once",
                table: "notifications",
                columns: new[] { "recipient_id", "type", "from_actor_id", "beep_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beep_hashtags");

            migrationBuilder.DropTable(
                name: "follows");

            migrationBuilder.DropTable(
                name: "likes");

            migrationBuilder.DropTable(
                name: "mentions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "hashtags");

            migrationBuilder.DropTable(
                name: "beeps");
        }
    }
}
