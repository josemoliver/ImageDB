# ImageDB — Guidance for AI code assistants

This file provides project-specific patterns, workflows and architectural context to help AI coding agents be immediately productive.

## 1. Project Purpose & Architecture

**What it does**: CLI tool that scans photo library folders, extracts metadata via ExifTool, and updates a SQLite database for metadata analysis.

**Core components**:
- Entry point: `console/Program.cs` — main scanning loop, mode handling, batch tracking, parallel optimization, and orchestration (~2565 lines)
- Metadata extraction: `console/ExifToolHelper.cs` — manages long-running ExifTool process (`-stay_open True`) with optimized reusable buffers
- Data persistence: `console/Models/CDatabaseImageDBsqliteContext.cs` — EF Core 9.0 context with SQLite
- Schema definition: `database/ImageDB.sqlite.sql` — canonical SQL for 12 tables + 40+ views + 9 indexes
- Tag services: `PeopleTagService`, `DescriptiveTagService`, `StructService` — CRUD for relationships with smart comparison logic

**Data flow**:
1. Scan photo folders → enumerate files → compare against DB (SHA1 or modified date)
2. **Pre-computation phase (parallel)**: For new/changed files → compute SHA1 + generate thumbnails + compute pixel hashes in batches of 50
3. **Processing phase (sequential)**: Invoke ExifTool → parse JSON → normalize values → update DB
4. **Optimized**: Single call to `GetExiftoolMetadataBoth()` retrieves both standard + struct metadata (40% faster for images with regions)
5. Store raw JSON in `Image.Metadata` field; store derived fields (Title, DateTimeTaken, Device, etc.) in dedicated columns
6. **Smart caching**: Compare existing tags/regions/collections before delete+insert (70-90% faster for metadata-only updates)

**Performance optimizations**:
- **Parallel SHA1 + thumbnail generation**: Batches of 50 files processed in parallel using `Parallel.ForEach` with `Partitioner.Create`
- **Combined operations**: Single MagickImage load for both thumbnail generation and pixel hash computation (eliminates duplicate file decoding)
- **Memory management**: Forced garbage collection between batches, ~36MB peak per file, ~290MB for 8-core parallel processing
- **Progress tracking**: Real-time progress bars with percentage completion and elapsed time display
- **Buffer optimizations**: 1MB SHA1 buffer, 128KB ExifTool output buffer, 64KB stream buffers

Quick inline example of the combined ExifTool call used internally:
`-stay_open True -@ args.txt -execute -G1 -n -json -struct -XMP:RegionInfo -XMP:Collections -XMP:PersonInImageWDetails <filepath> {ready}`

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

To verify on Windows PowerShell:
```powershell
Get-Command exiftool | Select-Object Source
```

## 3. Key Files & Responsibilities

| File | Purpose |
|------|---------|
| `console/Program.cs` | Main logic: file scanning, batch tracking, parallel SHA1/thumbnail processing, SQLite retry loops, derived field extraction, smart thumbnail/region caching, progress tracking (~2565 lines) |
| `console/ExifToolHelper.cs` | ExifTool process management with optimized reusable StringBuilders, `GetExiftoolMetadataBoth()` for combined metadata retrieval |
| `console/JsonConverter.cs` | Normalizes ExifTool output: converts all numbers/booleans → strings for consistency |
| `console/DeviceHelper.cs` | Device name normalization (combines `IFD0:Make` + `IFD0:Model` → "Apple iPhone 11 Pro Max") |
| `console/ImageFile.cs` | Lightweight DTO for file metadata (path, size, dates) |
| `console/MetadataStuct.cs` | POCOs for MWG Region/Collection deserialization (nested structures) |
| `console/PeopleTagService.cs` | People tag management with `GetExistingPeopleTagNames()` for smart comparison |
| `console/DescriptiveTagService.cs` | Keyword tag management with `GetExistingTagNames()` for smart comparison |
| `console/StructService.cs` | MWG structure services with smart region caching via `RegionCoordinatesMatch()` |
| `console/Models/*.cs` | EF Core entities (14 models: Image, Batch, Tag, PeopleTag, Region, Collection, etc.) |
| `console/appsettings.json` | Connection string, `IgnoreFolders` array, `ImageThumbs`, `RegionThumbs` boolean flags |
| `database/ImageDB.sqlite.sql` | DDL for 12 tables + 40+ views + 9 performance indexes |
| `database/migrations/*.sql` | Migration scripts for PixelHash column and Region indexes |


## 4. Project-Specific Conventions

**Date formatting**: ALL timestamps use `yyyy-MM-dd HH:mm:ss` format. Methods like `ConvertDateToNewFormat()` enforce this. Do not change.

**Path normalization**: Use `GetNormalizedFolderPath()` (removes quotes, trailing slashes) + `NormalizePathCase()` (matches filesystem casing). Required for `PhotoLibrary.Folder` matching.

Inline example:
`GetNormalizedFolderPath("\"C:\\Photos\\2023\\\"")` → `C:\Photos\2023`
`NormalizePathCase("c:\\photos\\2023")` → matches actual filesystem casing

**File change detection**:
- `normal` mode: SHA1 hash via `getFileSHA1()` (buffered 1MB reads)
- `date`/`quick` modes: compare `Image.FileModifiedDate` string (stored as `yyyy-MM-dd HH:mm:ss`)

**ExifTool invocation patterns**:
- Standard metadata: `-G1 -n -json <filepath>` (grouped, numeric, JSON output)
- MWG structures: `-struct -XMP:RegionInfo -XMP:Collections -XMP:PersonInImageWDetails <filepath>`
- Always uses stay_open process with `-execute` delimiter and `{ready}` marker

Minimal args file (`args.txt`) example used by stay_open:
```
-G1
-n
-json
-struct
-XMP:RegionInfo
-XMP:Collections
-XMP:PersonInImageWDetails
<filepath>
```

**SQLite concurrency**: ALL `SaveChanges()` wrapped in retry loop (5 attempts, 1s sleep) catching `SqliteErrorCode == 5` (database locked).

Pattern snippet:
```csharp
for (var attempt = 1; attempt <= 5; attempt++)
{
  try
  {
    await context.SaveChangesAsync();
    break;
  }
  catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
  {
    await Task.Delay(1000);
    if (attempt == 5) throw;
  }
}
```

**Metadata JSON storage**: `Image.Metadata` contains full ExifTool JSON output after `JsonConverter.ConvertNumericAndBooleanValuesToString()` processing. Queryable via SQLite's `json_extract()`.

**Tag service pattern** (follow this):
1. Delete existing relations for ImageId
2. Check if tag/person exists by name (unique constraint)
3. Insert new tag/person if missing
4. Insert `relation*` row linking ImageId to TagId/PeopleTagId

Inline example (keywords):
```csharp
var existing = await descriptiveTagService.GetExistingTagNames(imageId);
var incoming = new HashSet<string>(newTags, StringComparer.OrdinalIgnoreCase);
if (!existing.SetEquals(incoming))
{
  await descriptiveTagService.DeleteAllRelations(imageId);
  foreach (var tag in incoming)
    await descriptiveTagService.AddTagRelation(imageId, tag);
}
```

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

Optional flags:
```json
{
  "ImageThumbs": true,
  "RegionThumbs": true
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
- Magick.NET-Q16-AnyCPU 14.9.1 (thumbnail generation and pixel hash computation)

## 7. Performance Optimizations

**Parallel pre-computation** (implemented in `ScanFiles()`):
- **Batch processing**: Files processed in parallel batches of 50 using `Parallel.ForEach` with `Partitioner.Create(NoBuffering)`
- **Combined operations**: Single file read for SHA1 + thumbnail + pixel hash generation
- **Thread-safe caching**: `ConcurrentDictionary` instances for sha1Cache, thumbnailCache, pixelHashCache
- **Memory management**: `GC.Collect()` + `GC.WaitForPendingFinalizers()` after each batch

**GenerateSHA1AndThumbnailCombined() function**:
1. **Step 1**: Compute SHA1 from raw file bytes using `getFileSHA1()` (1MB buffer, FileOptions.SequentialScan)
2. **Step 2**: Load single `MagickImage` instance from file
3. **Step 3**: Compute pixel hash from 256px downscaled version
4. **Step 4**: Generate main thumbnail (384px WebP, quality 60)
5. **Returns**: (fileSHA1, thumbnail bytes, pixelHash, optional loaded image)

**Memory profile**:
- Per-file peak: ~36 MB (12MP image)
- Parallel processing (8 cores): ~290 MB peak
- Persistent cache: ~30 KB per file (compressed WebP)
- Batch size: 50 files with forced GC between batches

**Progress tracking**:
- Pre-computation phase: `[OPTIMIZED] Progress: X/Y (Z%)` updated every 10 files
- Main processing phase: `[PROCESSING] Progress: X/Y (Z%)` updated every 1%
- Elapsed time: Formatted as hours/minutes/seconds at completion
- No per-file console messages (preserved in [RESULTS] summary only)

**Buffer sizes**:
- SHA1 file read: 1 MB (1024 * 1024 bytes)
- ExifTool command: 2 KB
- ExifTool output: 128 KB
- ExifTool streams: 64 KB

**Performance gains**:
- SHA1 computation: 3-4x faster (parallelized with Partitioner)
- Thumbnail generation: ~50% faster per file (single MagickImage load)
- Overall new file processing: 20-30% faster
- Memory usage: 48% lower per file vs sequential approach

**Architectural constraints**:
- **SQLite**: Single-writer limitation prevents parallel database updates
- **ExifTool**: External Perl process requires file paths (cannot accept streams)
- **EF Core DbContext**: Not thread-safe, requires sequential SaveChanges()
- **Result**: Pre-computation parallelized, main processing loop remains sequential

## 8. Testing & Debugging

## 8. Testing & Debugging

**No unit tests** — validate changes by:
1. Create test SQLite DB: execute `database/ImageDB.sqlite.sql`
2. Insert test PhotoLibrary row
3. Run `ImageDB.exe --mode normal --folder "C:\TestPhotos"` with 5-10 sample images
4. Inspect `Batch`, `Image`, `Log` tables for results

**Debug techniques**:
- Add `Console.WriteLine(jsonMetadata)` in `UpdateImageRecord()` to see ExifTool output
- Use `--mode reload` to test metadata extraction logic without file I/O
- Check `Log` table for errors: `SELECT * FROM Log ORDER BY LogEntryId DESC`
- Use DeviceHelper test: uncomment `DeviceHelper.RunTest()` in Program.cs line 47

Quick log query examples:
```sql
SELECT * FROM Log ORDER BY LogEntryId DESC LIMIT 50;
SELECT * FROM Batch ORDER BY BatchID DESC LIMIT 10;
```

**Common views for analysis** (in `database/ImageDB.sqlite.sql`):
- `vLegacyWindowsXP` — files with XPTitle/XPComment/XPKeywords tags
- `vRegionMismatch` — MWG regions where AppliedToDimensions ≠ actual image dimensions
- `vDuplicateFilenames` — files with same name in different folders

## 9. Code Modification Guidelines

## 9. Code Modification Guidelines

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

Preferred method for combined metadata:
`ExifToolHelper.GetExiftoolMetadataBoth(filePath)`

**When adding tag types**:
- Follow `PeopleTagService.cs` pattern: unique name check → insert if missing → create relation
- Add corresponding `relation*` table if needed
- Add orphan cleanup in `ScanFiles()` batch completion (see lines 530-536)

**When modifying parallel operations**:
- SHA1/thumbnail generation uses batches of 50 files
- Always use `ConcurrentDictionary` for thread-safe caching
- Use `Interlocked.Increment` for thread-safe counters
- Add `GC.Collect()` + `GC.WaitForPendingFinalizers()` after batches
- Main processing loop must remain sequential (SQLite + ExifTool constraints)

## 10. Quick Reference

**Build**: `dotnet build ./console`  
**Run full scan**: `ImageDB.exe --mode normal --folder "C:\Photos"`  
**Reload metadata**: `ImageDB.exe --mode reload`  
**Target framework**: .NET 8.0  
**DB engine**: SQLite 3 (via Microsoft.EntityFrameworkCore.Sqlite)  
**Metadata source**: ExifTool (https://exiftool.org)

Quick PowerShell run examples:
```powershell
./console/bin/Debug/net8.0/ImageDB.exe --mode quick
./console/bin/Release/net8.0/ImageDB.exe --mode normal --folder "C:\Photos\2024"
```

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
