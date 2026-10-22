using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGravatarCheckedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "gravatar_checked_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_gravatar_unchecked",
                table: "users",
                column: "created_at",
                filter: "gravatar_checked_at IS NULL AND email_verified_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_gravatar_unchecked",
                table: "users");

            migrationBuilder.DropColumn(
                name: "gravatar_checked_at",
                table: "users");
        }
    }
}
