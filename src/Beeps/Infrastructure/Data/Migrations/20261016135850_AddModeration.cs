using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "ix_beeps_review",
                table: "beeps",
                column: "id",
                filter: "moderation_status IN ('hidden', 'failed') AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_blocks_blocked_id",
                table: "blocks",
                column: "blocked_id");

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

            // Wakes the review workers (LISTEN beeps_moderation). Fires inside the inserting transaction, so the
            // notification is delivered only once the reviews are committed and visible.
            migrationBuilder.Sql("""
                CREATE FUNCTION moderation_jobs_notify() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_notify('beeps_moderation', '');
                    RETURN NULL;
                END
                $$;

                CREATE TRIGGER moderation_jobs_notify AFTER INSERT ON moderation_jobs
                FOR EACH STATEMENT EXECUTE FUNCTION moderation_jobs_notify();
                """);

            // Beeps posted before the review queue existed were left pending: review the ones with text, and
            // allow the ones without (images alone are not reviewed).
            migrationBuilder.Sql("""
                INSERT INTO moderation_jobs (id, beep_id, attempts, run_after, created_at)
                SELECT uuidv7(), id, 0, now(), now() FROM beeps
                WHERE moderation_status = 'pending' AND deleted_at IS NULL AND text <> '';

                UPDATE beeps SET moderation_status = 'allowed' WHERE moderation_status = 'pending' AND text = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS moderation_jobs_notify ON moderation_jobs;
                DROP FUNCTION IF EXISTS moderation_jobs_notify();
                """);

            migrationBuilder.DropTable(
                name: "blocks");

            migrationBuilder.DropTable(
                name: "moderation_jobs");

            migrationBuilder.DropTable(
                name: "moderation_results");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropIndex(
                name: "ix_beeps_review",
                table: "beeps");
        }
    }
}
