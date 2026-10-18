using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_codes",
                columns: table => new
                {
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_codes", x => new { x.purpose, x.normalized_email });
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
                name: "idempotency_keys",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    location = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    body = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_dead",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_dead", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_jobs",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_jobs", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "site_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "actors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    handle = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    normalized_handle = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bio = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    avatar_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    banner_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actors", x => x.id);
                    table.CheckConstraint("ck_actors_agent_owned", "kind <> 'agent' OR owner_id IS NOT NULL");
                    table.CheckConstraint("ck_actors_human_named", "kind <> 'human' OR (handle IS NOT NULL AND display_name IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_actors_actors_owner_id",
                        column: x => x.owner_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_audit_logs_actors_admin_id",
                        column: x => x.admin_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_keys_actors_agent_id",
                        column: x => x.agent_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    view_count = table.Column<long>(type: "bigint", nullable: false),
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
                name: "invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_by = table.Column<Guid>(type: "uuid", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_invites_actors_created_by",
                        column: x => x.created_by,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invites_actors_used_by",
                        column: x => x.used_by,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    pending_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    banned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    following_count = table.Column<int>(type: "integer", nullable: false),
                    followers_count = table.Column<int>(type: "integer", nullable: false),
                    hide_likes = table.Column<bool>(type: "boolean", nullable: false),
                    hide_follows = table.Column<bool>(type: "boolean", nullable: false),
                    hide_follow_counts = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_actors_id",
                        column: x => x.id,
                        principalTable: "actors",
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
                name: "moderation_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_jobs_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moderation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    categories = table.Column<List<string>>(type: "text[]", nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    tokens = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_results_beeps_beep_id",
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
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_reports_actors_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reports_actors_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reports_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
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

            migrationBuilder.CreateTable(
                name: "media_uploads",
                columns: table => new
                {
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    crop_x = table.Column<int>(type: "integer", nullable: true),
                    crop_y = table.Column<int>(type: "integer", nullable: true),
                    crop_size = table.Column<int>(type: "integer", nullable: true),
                    banner_crop_x = table.Column<int>(type: "integer", nullable: true),
                    banner_crop_y = table.Column<int>(type: "integer", nullable: true),
                    banner_crop_width = table.Column<int>(type: "integer", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "blocks",
                columns: table => new
                {
                    blocker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocks", x => new { x.blocker_id, x.blocked_id });
                    table.CheckConstraint("ck_blocks_not_self", "blocker_id <> blocked_id");
                    table.ForeignKey(
                        name: "fk_blocks_actors_blocked_id",
                        column: x => x.blocked_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_blocks_user_blocker_id",
                        column: x => x.blocker_id,
                        principalTable: "users",
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
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "viewed_beeps",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    view_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_viewed_beeps", x => new { x.user_id, x.beep_id });
                    table.ForeignKey(
                        name: "fk_viewed_beeps_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_viewed_beeps_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actors_avatar_media_id",
                table: "actors",
                column: "avatar_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_actors_banner_media_id",
                table: "actors",
                column: "banner_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_actors_owner_id",
                table: "actors",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ux_actors_normalized_handle",
                table: "actors",
                column: "normalized_handle",
                unique: true)
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_logs_admin_id",
                table: "admin_audit_logs",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_keys_agent_id",
                table: "agent_keys",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_keys_key_hash",
                table: "agent_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_beep_hashtags_hashtag_id_beep_id",
                table: "beep_hashtags",
                columns: new[] { "hashtag_id", "beep_id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_beep_media_media_id",
                table: "beep_media",
                column: "media_id");

            migrationBuilder.CreateIndex(
                name: "ix_beeps_author_id_id",
                table: "beeps",
                columns: new[] { "author_id", "id" },
                descending: new[] { false, true })
                .Annotation("Npgsql:IndexInclude", new[] { "reply_to_id", "moderation_status", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_beeps_public",
                table: "beeps",
                column: "id",
                descending: new bool[0],
                filter: "reply_to_id IS NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_beeps_reply_to_id",
                table: "beeps",
                column: "reply_to_id")
                .Annotation("Npgsql:IndexInclude", new[] { "deleted_at", "moderation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_beeps_review",
                table: "beeps",
                column: "id",
                filter: "moderation_status IN ('hidden', 'failed') AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_beeps_root_id_id",
                table: "beeps",
                columns: new[] { "root_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_blocks_blocked_id",
                table: "blocks",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_codes_expires_at",
                table: "email_codes",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_follows_followee_id_created_at_follower_id",
                table: "follows",
                columns: new[] { "followee_id", "created_at", "follower_id" });

            migrationBuilder.CreateIndex(
                name: "ix_follows_follower_id_created_at_followee_id",
                table: "follows",
                columns: new[] { "follower_id", "created_at", "followee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_hashtags_name",
                table: "hashtags",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_invites_code_hash",
                table: "invites",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invites_created_by",
                table: "invites",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_invites_used_by",
                table: "invites",
                column: "used_by");

            migrationBuilder.CreateIndex(
                name: "ix_likes_actor_id_created_at_beep_id",
                table: "likes",
                columns: new[] { "actor_id", "created_at", "beep_id" });

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

            migrationBuilder.CreateIndex(
                name: "ix_mentions_actor_id",
                table: "mentions",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_jobs_beep_id",
                table: "moderation_jobs",
                column: "beep_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_jobs_run_after",
                table: "moderation_jobs",
                column: "run_after");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_results_beep_id_id",
                table: "moderation_results",
                columns: new[] { "beep_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_beep",
                table: "notifications",
                columns: new[] { "recipient_id", "beep_id", "type", "id" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_beep_id",
                table: "notifications",
                column: "beep_id")
                .Annotation("Npgsql:IndexInclude", new[] { "recipient_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_from_actor_id",
                table: "notifications",
                column: "from_actor_id")
                .Annotation("Npgsql:IndexInclude", new[] { "recipient_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_heads",
                table: "notifications",
                columns: new[] { "recipient_id", "id" },
                descending: new[] { false, true },
                filter: "NOT superseded");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread",
                table: "notifications",
                columns: new[] { "recipient_id", "id" },
                descending: new[] { false, true },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread_heads",
                table: "notifications",
                columns: new[] { "recipient_id", "id" },
                descending: new[] { false, true },
                filter: "NOT superseded AND read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notifications_once",
                table: "notifications",
                columns: new[] { "recipient_id", "type", "from_actor_id", "beep_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_run_after",
                table: "outbox",
                column: "run_after");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_dead_failed_at",
                table: "outbox_dead",
                column: "failed_at");

            migrationBuilder.CreateIndex(
                name: "ix_reports_beep_id_id",
                table: "reports",
                columns: new[] { "beep_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_id",
                table: "reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_resolved_by",
                table: "reports",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ux_reports_open",
                table: "reports",
                columns: new[] { "beep_id", "reporter_id" },
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_expires_at",
                table: "user_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_revoked_at",
                table: "user_sessions",
                column: "revoked_at",
                filter: "revoked_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_token_hash",
                table: "user_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                table: "user_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_admins",
                table: "users",
                column: "id",
                filter: "is_admin");

            migrationBuilder.CreateIndex(
                name: "ix_users_banned",
                table: "users",
                column: "id",
                filter: "banned_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_unverified",
                table: "users",
                column: "created_at",
                filter: "email_verified_at IS NULL AND normalized_email NOT LIKE 'released:%'");

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true)
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_viewed_beeps_beep_id",
                table: "viewed_beeps",
                column: "beep_id");

            migrationBuilder.AddForeignKey(
                name: "fk_actors_media_item_avatar_media_id",
                table: "actors",
                column: "avatar_media_id",
                principalTable: "media",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_actors_media_item_banner_media_id",
                table: "actors",
                column: "banner_media_id",
                principalTable: "media",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Wakes the outbox workers (LISTEN beeps_outbox) and the review workers (LISTEN beeps_moderation).
            // Fires inside the inserting transaction, so the notification is delivered only once the rows are
            // committed and visible.
            migrationBuilder.Sql("""
                CREATE FUNCTION outbox_notify() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_notify('beeps_outbox', '');
                    RETURN NULL;
                END
                $$;

                CREATE TRIGGER outbox_notify AFTER INSERT ON outbox
                FOR EACH STATEMENT EXECUTE FUNCTION outbox_notify();

                CREATE FUNCTION moderation_jobs_notify() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_notify('beeps_moderation', '');
                    RETURN NULL;
                END
                $$;

                CREATE TRIGGER moderation_jobs_notify AFTER INSERT ON moderation_jobs
                FOR EACH STATEMENT EXECUTE FUNCTION moderation_jobs_notify();
                """);

            // @mention suggestions, shortest handle first, then in byte order (EfSearchStore.SuggestActorsAsync): "C" serves
            // both the prefix and the order, which the database's own collation cannot. An expression index, which the model
            // cannot describe, so it lives here only.
            migrationBuilder.Sql("""
                CREATE INDEX ix_actors_handle_length ON actors (length(normalized_handle), normalized_handle COLLATE "C")
                WHERE normalized_handle IS NOT NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS moderation_jobs_notify ON moderation_jobs;
                DROP FUNCTION IF EXISTS moderation_jobs_notify();
                DROP TRIGGER IF EXISTS outbox_notify ON outbox;
                DROP FUNCTION IF EXISTS outbox_notify();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_actors_media_item_avatar_media_id",
                table: "actors");

            migrationBuilder.DropForeignKey(
                name: "fk_actors_media_item_banner_media_id",
                table: "actors");

            migrationBuilder.DropTable(
                name: "admin_audit_logs");

            migrationBuilder.DropTable(
                name: "agent_keys");

            migrationBuilder.DropTable(
                name: "beep_hashtags");

            migrationBuilder.DropTable(
                name: "beep_media");

            migrationBuilder.DropTable(
                name: "blocks");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "email_codes");

            migrationBuilder.DropTable(
                name: "follows");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "invites");

            migrationBuilder.DropTable(
                name: "likes");

            migrationBuilder.DropTable(
                name: "media_uploads");

            migrationBuilder.DropTable(
                name: "mentions");

            migrationBuilder.DropTable(
                name: "moderation_jobs");

            migrationBuilder.DropTable(
                name: "moderation_results");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "outbox_dead");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "scheduled_jobs");

            migrationBuilder.DropTable(
                name: "site_settings");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "viewed_beeps");

            migrationBuilder.DropTable(
                name: "hashtags");

            migrationBuilder.DropTable(
                name: "beeps");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "media");

            migrationBuilder.DropTable(
                name: "actors");
        }
    }
}
