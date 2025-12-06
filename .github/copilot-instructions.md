# ImageDB — Guidance for AI code assistants

This file explains project-specific patterns, workflows and important files so an AI coding agent can be productive quickly.

1. Project purpose and big picture
- Purpose: a CLI tool that scans photo library folders, extracts metadata via ExifTool and updates a SQLite database (see `database/ImageDB.sqlite.sql`).
- Main binary: the console app under `console/` (entry point: `console/Program.cs`). The app uses EF Core (`console/Models/CDatabaseImageDBsqliteContext.cs`) to persist to SQLite.

2. How the app is invoked / developer workflows
- Build: from repo root run `dotnet restore` then `dotnet build` (or `dotnet build ./console` for only the CLI). Built binaries are in `console/bin/Debug/net8.0/` or `console/bin/Release/net8.0/`.
- Run: produced executable (`ImageDB.exe`) accepts flags: `--mode [normal|date|quick|reload]` and optional `--folder "C:\path\to\library"`. Examples in `README.md`.
- Important runtime dependency: `exiftool.exe` must be installed and in `PATH`. The app checks version using `ExifToolHelper.CheckExiftool()`.

3. Key files and responsibilities (quick reference)
- `console/Program.cs` — full scanning logic, mode handling, batch recording, DB updates and retry logic for SQLite locks.
- `console/ExifToolHelper.cs` — long-running ExifTool process (`-stay_open True`) and the single place where exiftool commands are assembled. Use this for extraction behavior and JSON vs MWG structural modes.
- `console/JsonConverter.cs` — after exiftool output, numbers & booleans are converted to strings to keep DB values consistent.
- `console/Models/CDatabaseImageDBsqliteContext.cs` — EF Core context; it reads connection string from `console/appsettings.json` via `OnConfiguring`.
- `console/PeopleTagService.cs`, `console/DescriptiveTagService.cs`, `console/StructService.cs` — small services that add/remove related entities (tags, people, regions) using EF Core. Follow their patterns for DB updates (delete existing relations then add new ones).
- `database/ImageDB.sqlite.sql` — canonical DB schema and useful to inspect table/column names and indexes.

4. Project-specific conventions and gotchas
- Date strings: the code uses `yyyy-MM-dd HH:mm:ss` widely; generated `RecordAdded/RecordModified` strings follow that exact format — preserve it.
- Normalization: file paths are normalized via `GetNormalizedFolderPath`/`NormalizePathCase`. When matching `PhotoLibrary.Folder` values, use the same normalization logic.
- SHA1: file integrity comparisons use SHA1 via `getFileSHA1`. New files are detected by missing `Image.Filepath` records.
- ExifTool usage: the helper uses `-G1 -n -json` for common metadata and `-struct` + specific XMP tags when `mwg` mode is requested. Respect `ExifToolHelper.GetExiftoolMetadata(filepath, mode)` when calling extraction.
- Concurrency: SQLite lock handling is done with retry loops (catch SqliteErrorCode == 5). When modifying DB code, be careful to preserve these retries.
- Tag services: follow the pattern of checking for existing tag/person, create if missing, then add `relation*` rows. This code relies on `PeopleTag.PersonName` and `Tag.TagName` uniqueness (indexes exist in the EF model).

5. Configuration and common edits
- `console/appsettings.json` contains `ConnectionStrings:ImageDBConnectionString` and `IgnoreFolders` array (used by scanning code). Updates to config will be loaded at runtime via `ConfigurationBuilder` in `CDatabaseImageDBsqliteContext` and in `Program.cs`.
- To add a new PhotoLibrary, insert rows into `PhotoLibrary` table (via SQLite editor) — the app enumerates `db.PhotoLibraries` to choose scan folders.

6. Tests, debugging and iteration tips
- No unit tests present. To validate changes: build and run the console app against a small test photo folder and a disposable SQLite DB (use `database/ImageDB.sqlite.sql` to create schema).
- Debugging: place temporary `Console.WriteLine` in `Program.cs` near extraction/update functions to track JSON payloads. Use the `--mode reload` to re-process DB metadata without touching files.

7. When editing code, follow established patterns
- Keep date formatting and string-based metadata conversions intact.
- When adding fields to DB, update `database/ImageDB.sqlite.sql` and the EF model where appropriate (EF model was scaffolded; prefer small, backwards-compatible changes).
- Preserve ExifTool invocation style (stay_open process and `-execute` lines) — changing to spawn-per-file will degrade performance.

8. Merge guidance for `.github/copilot-instructions.md`
- If a previous instructions file exists, preserve any project-specific commands and examples. Replace general AI advice with this file's precise, codebase-specific points (entry points, config, exiftool, DB schema references).

9. Quick pointers (example snippets)
- Build CLI: `dotnet restore && dotnet build ./console`
- Run scan (normal): `ImageDB.exe --mode normal --folder "C:\Photos\Year2023"`
- Re-process DB metadata without reading files: `ImageDB.exe --mode reload`

If any section above is unclear or you want more details (e.g., a code map for specific services, sample appsettings.json values, or a small runnable test harness), tell me which area to expand and I will update this file.
