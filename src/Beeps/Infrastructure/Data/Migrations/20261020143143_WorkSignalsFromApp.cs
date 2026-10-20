using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkSignalsFromApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The app wakes the workers after the commit instead (Outbox.WorkSignals): a NOTIFY inside the writing
            // transaction made PostgreSQL commit those transactions, nearly every write, one at a time.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS moderation_jobs_notify ON moderation_jobs;
                DROP FUNCTION IF EXISTS moderation_jobs_notify();
                DROP TRIGGER IF EXISTS outbox_notify ON outbox;
                DROP FUNCTION IF EXISTS outbox_notify();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
