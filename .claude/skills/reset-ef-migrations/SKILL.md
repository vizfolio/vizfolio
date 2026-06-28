---
name: reset-ef-migrations
description: Reset Vizfolio's EF Core migrations to a single fresh Init migration. Use whenever the user asks to reset, squash, collapse, recreate, regenerate, flatten, or nuke EF migrations, or asks for a clean database init, in this codebase. Captures the project's non-obvious paths and the gotchas that previously caused trial-and-error (wrong output-dir, stale dev DB, test factory config-override bug).
---

# Reset EF Migrations (Vizfolio)

Use this when the user asks to reset / squash / regenerate / "clean init" EF Core migrations. The repo is small enough that we always collapse to a single `Init` migration rather than chain another one.

## Project-specific facts

- **Migrations project**: `src/Vizfolio.Infrastructure`
- **Startup project**: `src/Vizfolio.Api` (this is where `Program.cs` and config live)
- **Migrations folder**: `src/Vizfolio.Infrastructure/Persistence/Migrations/` — **not** the EF default of `Migrations/` at the project root
- **DbContext**: `Vizfolio.Infrastructure.Persistence.AppDbContext`
- **EF tool**: `dotnet ef` 10.0.0, pinned via `dotnet-tools.json` (no install needed)
- **Default provider**: SQLite. Postgres + SQL Server are also wired up — keep migrations provider-agnostic (see Gotcha 4).
- **Local dev DB**: `src/Vizfolio.Api/vizfolio.dev.db` (+ `-shm`, `-wal`). This is the file `dotnet run` writes to in Development.

## Steps

1. Delete every migration file and the model snapshot:
   ```
   rm src/Vizfolio.Infrastructure/Persistence/Migrations/*.cs
   ```
   (If you don't trust the glob, list them first — they're named `<timestamp>_Name.cs`, `<timestamp>_Name.Designer.cs`, and `AppDbContextModelSnapshot.cs`.)

2. Delete the local dev DB so the next `dotnet run` rebuilds it clean. This is **required** — see Gotcha 2.
   ```
   rm -f src/Vizfolio.Api/vizfolio.dev.db src/Vizfolio.Api/vizfolio.dev.db-shm src/Vizfolio.Api/vizfolio.dev.db-wal
   ```

3. Generate the new Init migration. **Always pass `--output-dir Persistence/Migrations`** — see Gotcha 1:
   ```
   dotnet ef migrations add Init \
     --project src/Vizfolio.Infrastructure \
     --startup-project src/Vizfolio.Api \
     --output-dir Persistence/Migrations
   ```

4. Verify the migration applies cleanly against a fresh SQLite file (catches data/seed issues before tests run):
   ```
   TMP=/tmp/vizfolio-init-check.db && rm -f $TMP && \
     Database__Provider=Sqlite ConnectionStrings__Default="Data Source=$TMP" \
     dotnet ef database update \
     --project src/Vizfolio.Infrastructure --startup-project src/Vizfolio.Api && \
   rm -f $TMP
   ```

5. Run the full test suite — endpoint tests boot the API and call `Migrate()`, which exercises the new migration end-to-end:
   ```
   dotnet test
   ```

## Gotchas (these are why this skill exists)

### 1. `--output-dir` is mandatory or migrations land in the wrong place

When the `Persistence/Migrations/` folder is empty, `dotnet ef migrations add` falls back to the EF default of `<project>/Migrations/` at the project root. The next `dotnet ef` command then picks the wrong folder up as its source. If this happens: run `dotnet ef migrations remove` (it cleans up the misplaced files), then re-add with `--output-dir Persistence/Migrations`.

### 2. The dev DB holds the *old* migration history

`src/Vizfolio.Api/vizfolio.dev.db` accumulates rows in `__EFMigrationsHistory` for whatever migrations existed when it was last written. After a reset, the new `Init` migration won't be in that history, so `Migrate()` tries to apply it — and the schema already has all the tables, producing `SqliteException: table "X" already exists`. Delete the file (step 2 above). It regenerates on the next `dotnet run`.

### 3. The test factory used to silently use the dev DB — verify the fix is still in place

`VizfolioApiFactory` is supposed to point each test at a fresh `/tmp/vizfolio-test-<guid>.db`. Historically its `ConfigureAppConfiguration` in-memory override did **not** win over `appsettings.Development.json` (which sets `ConnectionStrings:Default=Data Source=vizfolio.dev.db`). The connection-string config silently lost; tests used the dev DB and only worked because the schema happened to match.

The current fix is in `tests/Vizfolio.Api.Tests/VizfolioApiFactory.cs`: `ConfigureServices` removes the `DbContextOptions<AppDbContext>` registration and re-adds it with the tmp path. Before debugging a "table already exists" failure during tests, confirm that block is still there. If someone refactors the factory back to config-only override, the bug returns.

A quick way to diagnose this if it ever recurs: temporarily print `db.Database.GetDbConnection().ConnectionString` inside the `Database:AutoMigrate` block in `Program.cs`. If it says `vizfolio.dev.db` during a test run, the factory override is broken.

### 4. Don't use provider-specific syntax in migrations

The codebase supports SQLite, Postgres, and SQL Server (see `DatabaseProvider.cs`). Don't reach for `HasFilter("[col] IS NOT NULL")` (SQL Server bracket quoting) or any other provider-specific SQL in entity configurations — it makes the migration unportable. If you need uniqueness conditional on a column being non-null, enforce it in application code (the existing `InstrumentResolver` is an example: it find-or-creates with an in-memory cache instead of relying on a filtered unique index).

## After the reset

The user said this is a "clean init for first release" project, so be matter-of-fact about it being breaking. Don't try to preserve data in `vizfolio.dev.db`. If they want sample data, regenerate via the import flows (`POST /admin/imports/...`) after `dotnet run` recreates the DB.
