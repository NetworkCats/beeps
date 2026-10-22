using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "untranslated_languages",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.CreateTable(
                name: "beep_translations",
                columns: table => new
                {
                    beep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    english = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    chinese = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    tokens = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beep_translations", x => x.beep_id);
                    table.ForeignKey(
                        name: "fk_beep_translations_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "translation_jobs",
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
                    table.PrimaryKey("pk_translation_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_jobs_beeps_beep_id",
                        column: x => x.beep_id,
                        principalTable: "beeps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_translation_jobs_beep_id",
                table: "translation_jobs",
                column: "beep_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_jobs_run_after",
                table: "translation_jobs",
                column: "run_after");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beep_translations");

            migrationBuilder.DropTable(
                name: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "untranslated_languages",
                table: "users");
        }
    }
}
