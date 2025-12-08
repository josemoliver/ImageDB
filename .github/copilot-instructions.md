# ImageDB — Guidance for AI code assistants

This file provides project-specific patterns, workflows and architectural context to help AI coding agents be immediately productive.

## 1. Project Purpose & Architecture

**What it does**: CLI tool that scans photo library folders, extracts metadata via ExifTool, and updates a SQLite database for metadata analysis.

**Core components**:
- Entry point: `console/Program.cs` — main scanning loop, mode handling, batch tracking, and orchestration
- Metadata extraction: `console/ExifToolHelper.cs` — manages long-running ExifTool process (`-stay_open True`)
- Data persistence: `console/Models/CDatabaseImageDBsqliteContext.cs` — EF Core 9.0 context with SQLite
- Schema definition: `database/ImageDB.sqlite.sql` — canonical SQL for tables, views, and indexes
- Tag services: `PeopleTagService`, `DescriptiveTagService`, `StructService` — CRUD for relationships (tags, people, regions, collections)

**Data flow**:
1. Scan photo folders → enumerate files → compare against DB (SHA1 or modified date)
2. For new/changed files: invoke ExifTool → parse JSON → normalize values → update DB
3. Extract structured data (MWG regions/collections) with separate ExifTool call using `-struct`
4. Store raw JSON in `Image.Metadata` field; store derived fields (Title, DateTimeTaken, Device, etc.) in dedicated columns

## 2. Developer Workflows

**Build & run**:
```powershell
# From repo root
dotnet restore
dotnet build ./console

# Output: console/bin/Debug/net8.0/ImageDB.exe
```

**Execution modes** (via `--mode` flag):
- `normal` — SHA1-based integrity scan (slowest, most reliable)
- `date` — file modified date comparison (faster)
- `quick` — stops at first unchanged file when sorted by modified date (fastest)
- `reload` — reprocess existing DB metadata without reading files (for schema/logic updates)

**Example commands**:
```powershell
ImageDB.exe --mode normal --folder "C:\Photos\2023"
ImageDB.exe --mode quick                              # Scans all libraries
ImageDB.exe --mode reload                             # Re-extract derived fields
```

**Critical dependency**: `exiftool.exe` must be in PATH. App validates with `ExifToolHelper.CheckExiftool()` on startup.

## 3. Key Files & Responsibilities

| File | Purpose |
|------|---------|
| `console/Program.cs` | Main logic: file scanning, batch tracking, SQLite retry loops, derived field extraction (1633 lines) |
| `console/ExifToolHelper.cs` | ExifTool process management, stay_open protocol, command assembly (`-G1 -n -json` vs `-struct`) |
| `console/JsonConverter.cs` | Normalizes ExifTool output: converts all numbers/booleans → strings for consistency |
| `console/DeviceHelper.cs` | Device name normalization (combines `IFD0:Make` + `IFD0:Model` → "Apple iPhone 11 Pro Max") |
| `console/ImageFile.cs` | Lightweight DTO for file metadata (path, size, dates) |
| `console/MetadataStuct.cs` | POCOs for MWG Region/Collection deserialization (nested structures) |
| `console/Models/*.cs` | EF Core entities (14 models: Image, Batch, Tag, PeopleTag, Region, Collection, etc.) |
| `console/appsettings.json` | Connection string (`ImageDBConnectionString`), `IgnoreFolders` array |
| `database/ImageDB.sqlite.sql` | DDL for 12 tables + 30+ views (e.g., `vLegacyWindowsXP`, `vRegionMismatch`) |

## 4. Project-Specific Conventions

**Date formatting**: ALL timestamps use `yyyy-MM-dd HH:mm:ss` format. Methods like `ConvertDateToNewFormat()` enforce this. Do not change.

**Path normalization**: Use `GetNormalizedFolderPath()` (removes quotes, trailing slashes) + `NormalizePathCase()` (matches filesystem casing). Required for `PhotoLibrary.Folder` matching.

**File change detection**:
- `normal` mode: SHA1 hash via `getFileSHA1()` (buffered 8KB reads)
- `date`/`quick` modes: compare `Image.FileModifiedDate` string (stored as `yyyy-MM-dd HH:mm:ss`)

**ExifTool invocation patterns**:
- Standard metadata: `-G1 -n -json <filepath>` (grouped, numeric, JSON output)
- MWG structures: `-struct -XMP:RegionInfo -XMP:Collections -XMP:PersonInImageWDetails <filepath>`
- Always uses stay_open process with `-execute` delimiter and `{ready}` marker

**SQLite concurrency**: ALL `SaveChanges()` wrapped in retry loop (5 attempts, 1s sleep) catching `SqliteErrorCode == 5` (database locked).

**Metadata JSON storage**: `Image.Metadata` contains full ExifTool JSON output after `JsonConverter.ConvertNumericAndBooleanValuesToString()` processing. Queryable via SQLite's `json_extract()`.

**Tag service pattern** (follow this):
1. Delete existing relations for ImageId
2. Check if tag/person exists by name (unique constraint)
3. Insert new tag/person if missing
4. Insert `relation*` row linking ImageId to TagId/PeopleTagId

## 5. Metadata Extraction Logic (MWG-compliant)

**Field precedence** (from `Program.cs` UpdateImageRecord):
- `Title`: XMP-dc:Title → IPTC:ObjectName → IFD0:XPTitle (legacy Windows XP)
- `Description`: XMP-dc:Description → IPTC:Caption-Abstract → IFD0:ImageDescription → ExifIFD:UserComment → [5 more fallbacks]
- `DateTimeTaken`: XMP-photoshop:DateCreated → ExifIFD:DateTimeOriginal → ExifIFD:CreateDate → XMP-exif:DateTimeOriginal → IPTC:DateCreated+TimeCreated → oldest(FileCreatedDate, FileModifiedDate)
- `Device`: Uses `DeviceHelper.GetDevice(Make, Model)` which normalizes 100+ camera brands

**Region detection**: If JSON contains `XMP-mwg-rs:Region` OR `XMP-mwg-coll:Collection` OR `XMP-iptcExt:PersonInImage`, fetch struct metadata separately.

**Coordinate rounding**: GPS lat/lon rounded to 6 decimals via `RoundCoordinate()`.

## 6. Configuration & Setup

**appsettings.json** (example):
```json
{
  "ConnectionStrings": {
    "ImageDBConnectionString": "Data Source=C:\\Database\\ImageDB.sqlite"
  },
  "IgnoreFolders": [ "\\.dtrash\\" ]
}
```

**Adding photo libraries**: Insert into `PhotoLibrary` table manually:
```sql
INSERT INTO PhotoLibrary (Folder) VALUES ('C:\Photos\Year2023');
```

**NuGet dependencies** (from `ImageDB.csproj`):
- Microsoft.EntityFrameworkCore.Sqlite 9.0.10
- System.CommandLine 2.0.0-beta4
- LumenWorksCsvReader 4.0.0 (for DeviceHelper CSV test data)
- Magick.NET-Q16-AnyCPU 14.9.1 (future thumbnail support)

## 7. Testing & Debugging

**No unit tests** — validate changes by:
1. Create test SQLite DB: execute `database/ImageDB.sqlite.sql`
2. Insert test PhotoLibrary row
3. Run `ImageDB.exe --mode normal --folder "C:\TestPhotos"` with 5-10 sample images
4. Inspect `Batch`, `Image`, `Log` tables for results

**Debug techniques**:
- Add `Console.WriteLine(jsonMetadata)` in `UpdateImage()` to see ExifTool output
- Use `--mode reload` to test metadata extraction logic without file I/O
- Check `Log` table for errors: `SELECT * FROM Log ORDER BY LogEntryId DESC`
- Use DeviceHelper test: uncomment `DeviceHelper.RunTest()` in Program.cs line 67

**Common views for analysis** (in `database/ImageDB.sqlite.sql`):
- `vLegacyWindowsXP` — files with XPTitle/XPComment/XPKeywords tags
- `vRegionMismatch` — MWG regions where AppliedToDimensions ≠ actual image dimensions
- `vDuplicateFilenames` — files with same name in different folders

## 8. Code Modification Guidelines

**When adding DB fields**:
1. Update `database/ImageDB.sqlite.sql` (DDL)
2. Regenerate EF model or manually add property to `console/Models/Image.cs`
3. Add extraction logic in `UpdateImageRecord()` using `GetFirstNonEmptyExifValue()` pattern
4. Preserve SQLite retry loops in save operations

**When modifying ExifTool extraction**:
- All changes go in `ExifToolHelper.GetExiftoolMetadata()`
- Do NOT spawn per-file processes (destroys performance)
- Maintain `-execute` protocol for stay_open mode
- Keep `JsonConverter.ConvertNumericAndBooleanValuesToString()` call

**When adding tag types**:
- Follow `PeopleTagService.cs` pattern: unique name check → insert if missing → create relation
- Add corresponding `relation*` table if needed
- Add orphan cleanup in `ScanFiles()` batch completion (see lines 530-536)

## 9. Quick Reference

**Build**: `dotnet build ./console`  
**Run full scan**: `ImageDB.exe --mode normal --folder "C:\Photos"`  
**Reload metadata**: `ImageDB.exe --mode reload`  
**Target framework**: .NET 8.0  
**DB engine**: SQLite 3 (via Microsoft.EntityFrameworkCore.Sqlite)  
**Metadata source**: ExifTool (https://exiftool.org)

**Useful queries**:
```sql
-- Recent batches
SELECT * FROM Batch ORDER BY BatchID DESC LIMIT 10;

-- Images missing dates
SELECT ImageId, Filepath FROM Image WHERE DateTimeTaken IS NULL;

-- Tag usage
SELECT TagName, COUNT(*) FROM Tag JOIN relationTag USING(TagId) GROUP BY TagName;
```

---
*For questions or clarification on specific workflows, schema design decisions, or ExifTool tag mappings, reference the inline comments in `Program.cs` or the MWG 2010 specification linked in code comments.*
