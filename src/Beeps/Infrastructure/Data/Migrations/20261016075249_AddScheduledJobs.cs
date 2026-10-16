using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "ix_email_codes_expires_at",
                table: "email_codes",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_jobs");

            migrationBuilder.DropIndex(
                name: "ix_user_sessions_expires_at",
                table: "user_sessions");

            migrationBuilder.DropIndex(
                name: "ix_user_sessions_revoked_at",
                table: "user_sessions");

            migrationBuilder.DropIndex(
                name: "ix_email_codes_expires_at",
                table: "email_codes");
        }
    }
}
