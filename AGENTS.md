# AGENTS.md

## The site is live: database migrations must keep the data

beeps runs in production at https://beeps.sh with real users and their data. Every migration has to work on that
database without losing or corrupting anything in it.

- Never edit, fold or delete a migration that has been released (`src/Beeps/Infrastructure/Data/Migrations`).
  Change the schema with a new one: `make migration NAME=AddSomething`.
- Read the generated migration before committing it. EF Core turns a rename into a drop and an add: write it as
  a rename, and move data with SQL in the migration when a column or table changes shape.
- Do not drop a column or table that still holds data, and do not add a `NOT NULL` column without a default or
  a backfill for the existing rows.
- A release's migrations run while the previous release still takes traffic, and a rollback does not undo them.
  Keep each change compatible with the version before it: add first, remove a release later.
