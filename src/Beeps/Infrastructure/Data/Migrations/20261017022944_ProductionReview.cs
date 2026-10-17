using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductionReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_beeps_author_id_id",
                table: "beeps");

            migrationBuilder.CreateIndex(
                name: "ix_users_banned",
                table: "users",
                column: "id",
                filter: "banned_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_unverified",
                table: "users",
                column: "created_at",
                filter: "email_verified_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_beeps_author_id_id",
                table: "beeps",
                columns: new[] { "author_id", "id" },
                descending: new[] { false, true })
                .Annotation("Npgsql:IndexInclude", new[] { "reply_to_id", "moderation_status", "deleted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_banned",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_unverified",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_beeps_author_id_id",
                table: "beeps");

            migrationBuilder.CreateIndex(
                name: "ix_beeps_author_id_id",
                table: "beeps",
                columns: new[] { "author_id", "id" },
                descending: new[] { false, true });
        }
    }
}
