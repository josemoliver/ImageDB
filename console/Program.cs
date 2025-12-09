// See https://aka.ms/new-console-template for more information
using ImageDB;
using ImageDB.Models;
using ImageMagick;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;


// ImageDB
// Source Repo & Documentation: https://github.com/josemoliver/ImageDB
// Author: José Oliver-Didier
// License: MIT

// Constants for configuration values
const int SqliteRetryCount = 5;
const int SqliteRetryDelayMs = 1000;
const int GpsCoordinatePrecision = 6;
const int FileReadBufferSize = 8192;

var rootCommand = new RootCommand
{
   new Option<string>(
        "--mode",
        description: "(Required) Operation modes [ normal | date | quick | reload ]"
    ),
    new Option<string>(
        "--folder",
        description: "(Optional) Specify the path to a specific library to scan. Path must be included in the PhotoLibrary db table. Leaving value empty will run through all library folders."
    )
};

//DEBUG: Uncomment the following line to run the DeviceHelper Test
//DeviceHelper.RunTest();return 0;


using var db = new CDatabaseImageDBsqliteContext();

var photoLibrary                = db.PhotoLibraries.ToList();
string photoFolderFilter        = string.Empty;
string operationMode            = string.Empty;
bool reloadMetadata             = false;
bool quickScan                  = false;
bool dateScan                   = false;
bool generateThumbnails         = true;
bool generateRegionThumbnails   = true;

Console.WriteLine("ImageDB - Scan and update your photo library.");
Console.WriteLine("---------------------------------------------");
Console.WriteLine("Code and Info: https://github.com/josemoliver/ImageDB");
Console.WriteLine("Leveraging the Exiftool utility written by Phil Harvey - https://exiftool.org");
Console.WriteLine("");

// Get RegionThumbs and Thumbnail generation settings from appsettings.json
var configuration = new ConfigurationManager().AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).Build();
try
{
    generateThumbnails = configuration.GetValue<bool>("ImageThumbs", true);
}
catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException)
{
    Console.WriteLine($"Warning: Could not read ImageThumbs setting, using default value (true). Error: {ex.Message}");
    generateThumbnails = true;
}

try
{
    generateRegionThumbnails = configuration.GetValue<bool>("RegionThumbs", true);
}
catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException)
{
    Console.WriteLine($"Warning: Could not read RegionThumbs setting, using default value (true). Error: {ex.Message}");
    generateRegionThumbnails = true;
}

// Handler to process the command-line arguments
rootCommand.Handler = CommandHandler.Create((string folder, string mode) =>
{
    // Set the operation mode based on the input
    operationMode = mode?.ToLowerInvariant() ?? string.Empty;

    // Get filter photo path, if any.
    photoFolderFilter = folder;

    return Task.CompletedTask;
});

// Parse and invoke the command
await rootCommand.InvokeAsync(args);

if (((operationMode == "normal") || (operationMode == "date") || (operationMode == "quick") || (operationMode == "reload")) == true)
{
    if (string.IsNullOrEmpty(photoFolderFilter))
    {
        photoFolderFilter = string.Empty;
        Console.WriteLine("[INFO] - No filter applied.");
    }
    else
    {
        photoFolderFilter = GetNormalizedFolderPath(photoFolderFilter);
        Console.WriteLine("[INFO] - Filtered by folder: " + photoFolderFilter);
    }


    // Determine the mode and set appropriate flags
    reloadMetadata = string.Equals(operationMode, "reload", StringComparison.OrdinalIgnoreCase);

    if (reloadMetadata)
    {
        Console.WriteLine("[MODE] - Reprocessing existing metadata, no new and update from files.");
    }
    else
    {
        // Check if Exiftool is properly installed, terminate app on error
        if (!ExifToolHelper.CheckExiftool())
        {
            Environment.Exit(1);
        }

        // Set scan modes based on the provided mode
        SetScanMode(operationMode);

        Console.WriteLine("[START] - Scanning for new and updated files.");

    }

    foreach (var folder in photoLibrary)
    {
        //Normalize Folder Path
        string photoFolder = folder.Folder.ToString() ?? "";
        photoFolder = GetNormalizedFolderPath(photoFolder);

        if ((photoFolderFilter == string.Empty) || (photoFolder == photoFolderFilter))
        {

            //Fetch photoLibraryId
            int photoLibraryId = 0;
            photoLibraryId = photoLibrary.FirstOrDefault(pl => pl.Folder.Equals(photoFolder, StringComparison.OrdinalIgnoreCase))?.PhotoLibraryId ?? 0;

            if (photoLibraryId != 0)
            {
                if (reloadMetadata == true)
                {
                    Console.WriteLine("[UPDATE] - Reprocessing metadata folder: " + photoFolder);
                    await ReloadMetadata(photoLibraryId);
                }
                else
                {
                    Console.WriteLine("[SCAN] - Scanning folder: " + photoFolder);
                    await ScanFiles(photoFolder, photoLibraryId);
                }
            }
        }
    }

    if (reloadMetadata == false)
    {
        // Shutdown ExifTool process
        ExifToolHelper.Shutdown();
    }

    // Exit the application
    Environment.Exit(0);
}
else
{
    Console.WriteLine("[ERROR] - Invalid mode. Use [ normal | date | quick | reload ]");
    Environment.Exit(1);
}

/// <summary>
/// Saves changes to the database with SQLite lock retry logic.
/// Retries up to SqliteRetryCount times with SqliteRetryDelayMs delay between attempts.
/// </summary>
/// <param name="context">The database context to save</param>
static async Task SaveChangesWithRetry(DbContext context)
{
    int retryCount = SqliteRetryCount;
    
    while (retryCount-- > 0)
    {
        try
        {
            await context.SaveChangesAsync();
            return;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
        {
            if (retryCount == 0) throw;
            await Task.Delay(SqliteRetryDelayMs);
        }
    }
}

// Method to set the scan mode based on the input
void SetScanMode(string mode)
{
    // Default to normal mode
    dateScan = false;
    quickScan = false;
    
    if (string.Equals(mode, "normal", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("[MODE] - Integrity scan for new and updated files.");
    }
    else if (string.Equals(mode, "date", StringComparison.OrdinalIgnoreCase))
    {
        dateScan = true;
        Console.WriteLine("[MODE] - Scan for changes using file modified date.");
    }
    else if (string.Equals(mode, "quick", StringComparison.OrdinalIgnoreCase))
    {
        dateScan = true;
        quickScan = true;
        Console.WriteLine("[MODE] - Quick scan for changes using file modified date.");
    }
}


static void LogEntry(int batchId, string filePath, string logEntry)
{
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        //Add entry to Log
        var newLog = new Log
        {
            BatchId = batchId,
            Filepath = filePath,
            LogEntry = logEntry,
            Datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        dbFiles.Add(newLog);
        dbFiles.SaveChanges();
    }
}

async Task ReloadMetadata(int photoLibraryId)
{
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        // Query only the ImageIds directly from the database to minimize memory usage
        var imageIdsFromLibrary = await dbFiles.Images
            .AsNoTracking()
            .Where(img => img.PhotoLibraryId == photoLibraryId)
            .Select(img => img.ImageId)
            .ToListAsync();

        foreach (var imageId in imageIdsFromLibrary)
        {
            await UpdateImageRecord(imageId, "", null);
            Console.WriteLine("[UPDATE] - Reprocessing metadata for Image Id: " + imageId);
        }
    }
}

async Task ScanFiles(string photoFolder, int photoLibraryId)
{
    Console.WriteLine("[START] - Scanning folder for images: "+ photoFolder);
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        // Pre-load all existing images from this library into a dictionary for fast lookup
        var existingImages = await dbFiles.Images
            .AsNoTracking()
            .Where(img => img.PhotoLibraryId == photoLibraryId)
            .Select(img => new { img.Filepath, img.ImageId, img.Sha1, img.FileModifiedDate })
            .ToDictionaryAsync(img => img.Filepath, StringComparer.OrdinalIgnoreCase);
        
        List<ImageFile> imageFiles = new List<ImageFile>();        
        
        // Define counters
        int filesAdded      = 0;
        int filesDeleted    = 0;
        int filesUpdated    = 0;
        int filesSkipped    = 0;
        int filesError      = 0;

        // Get all supported files in the directory and subdirectories
        DirectoryInfo info = new DirectoryInfo(photoFolder);
        string[] fileExtensions = { ".jpg", ".jpeg", ".jxl", ".heic" };

        // Get IgnoreFolders values from appsettings.json, default to empty array if empty.   
        var configuration = new ConfigurationManager().AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).Build();
        string[] ignoreFolders = configuration.GetSection("IgnoreFolders").Get<string[]>() ?? new string[0]; 

        // Read all files from photo folder, including subfolders.
        FileInfo[] files = info.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(p => fileExtensions.Contains(p.Extension.ToLower()))
            .OrderByDescending(p => p.LastWriteTime)
            .ToArray();

        // Iterate over each file and add them to imageFiles, exclude ignored folders.
        foreach (FileInfo file in files)
        {
            // Check if the file is in an ignored folder
            if (ignoreFolders.Any(folder => file.FullName.Contains(folder, StringComparison.OrdinalIgnoreCase)))
            {
                continue; // Skip this file
            }
                       
            imageFiles.Add(new ImageFile(file.FullName.ToString(), file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), file.Extension.ToString().ToLower(), file.Name.ToString(),file.Length,file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));         
        }

        // Start Batch entry get batch id
        var newBatch = new Batch
        {
            StartDateTime   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            PhotoLibraryId  = photoLibraryId,
            FilesFound      = imageFiles.Count
        };

        // Add the new batch entry to the database
        dbFiles.Add(newBatch);
        dbFiles.SaveChanges();
        
        int batchID = newBatch.BatchId;

        // Flag to suspend the scan once a file with no updates pending found.
        bool suspendScan = false;
        
        Console.WriteLine("[BATCH] - Started job # "+ batchID+" (" + imageFiles.Count + " files found.)");

        // Iterate over each image file
        for (int i = 0; i < imageFiles.Count; i++)
        {
            if (suspendScan == true)
            {
                break;
            }

            string SHA1 = string.Empty;
            string imageSHA1 = string.Empty;
            string imagelastModifiedDate = string.Empty;
            string specificFilePath = imageFiles[i].FilePath;
            int imageId = 0;

            // Check if file exists in our pre-loaded dictionary
            bool fileExistsInDb = existingImages.TryGetValue(specificFilePath, out var existingImage);

            if (!fileExistsInDb)
            {
                // File was not found in db, add it
                SHA1 = getFileSHA1(imageFiles[i].FilePath);        
                Console.WriteLine("[ADD] - " + imageFiles[i].FilePath);
                try
                {
                    await AddImage(photoLibraryId, photoFolder, batchID, imageFiles[i].FilePath, imageFiles[i].FileName, imageFiles[i].FileExtension, imageFiles[i].FileSize, SHA1);
                    filesAdded++;                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERROR] - " + imageFiles[i].FilePath);
                    LogEntry(batchID, imageFiles[i].FilePath, ex.ToString());
                    Console.Write(ex.ToString());
                    filesError++;
                }               
        
            }
            else
            {
                //Check if file has been modified
                imageId = existingImage.ImageId;

                if (dateScan == false)
                {
                    SHA1 = getFileSHA1(imageFiles[i].FilePath);
                    imageSHA1 = existingImage.Sha1 ?? string.Empty;
                }
                else
                {
                    imagelastModifiedDate = existingImage.FileModifiedDate ?? string.Empty;
                }



                // Check if the SHA1 hash is different
                if ((SHA1!=imageSHA1)&&(dateScan == false))
                {
                    // File has been modified, update it
                    Console.WriteLine("[UPDATE] - " + imageFiles[i].FilePath);
                    try
                    {
                        await CopyImageToMetadataHistory(imageId);
                        await UpdateImage(imageId, SHA1, batchID);
                        filesUpdated++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERROR] - " + imageFiles[i].FilePath);
                        LogEntry(batchID, imageFiles[i].FilePath, ex.ToString());
                        Console.Write(ex.ToString());
                        filesError++;
                    }
                }
                else if ((imagelastModifiedDate != imageFiles[i].FileModifiedDate)&&(dateScan == true))
                {
                    // File has been modified, update it
                    SHA1 = getFileSHA1(imageFiles[i].FilePath);
                    Console.WriteLine("[UPDATE] - " + imageFiles[i].FilePath);
                    try
                    {
                        // Update file record
                        await CopyImageToMetadataHistory(imageId);
                        await UpdateImage(imageId, SHA1, batchID);
                        filesUpdated++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERROR] - " + imageFiles[i].FilePath);
                        LogEntry(batchID, imageFiles[i].FilePath, ex.ToString());
                        Console.Write(ex.ToString());
                        filesError++;
                    }
                }
                else if (reloadMetadata == true)
                {
                    // File has been modified, update it
                    Console.WriteLine("[UPDATE] - " + imageFiles[i]);
                    try
                    {
                        // Update file record
                        await UpdateImage(imageId, SHA1, batchID);
                        filesUpdated++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERROR] - " + imageFiles[i].FilePath);
                        LogEntry(batchID, imageFiles[i].FilePath, ex.ToString());
                        Console.Write(ex.ToString());
                        filesError++;
                    }

                }
                else
                {   // File is unchanged                 
                    // Check if quick scan is enabled
                    if (quickScan == true)
                    {   
                        // Quick scan, skip the rest of the files
                        suspendScan = true;
                        filesSkipped = imageFiles.Count - i;
                    }
                    else
                    {
                        // File is unchanged, skip it
                        Console.WriteLine("[SKIP] - " + imageFiles[i].FilePath);
                        filesSkipped++;
                    }
                }
            }

        }

        // Check if files have been deleted
        // Use the pre-loaded dictionary for faster lookups
        var folderFilePathsSet = new HashSet<string>(imageFiles.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
        var missingFiles = existingImages.Keys.Where(dbPath => !folderFilePathsSet.Contains(dbPath)).ToList();

        // Iterate over the missing files and remove them from the database
        foreach (var missingFile in missingFiles)
        {
            Console.WriteLine("[REMOVE] - " + missingFile);

            try
            {
                // Remove the file record from the database
                var imageToRemove = dbFiles.Images.FirstOrDefault(img => img.Filepath == missingFile);
                if (imageToRemove != null)
                {
                    dbFiles.Images.Remove(imageToRemove);
                    filesDeleted++;
                    LogEntry(batchID, missingFile,"Removed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] - Failed to remove " + missingFile);
                LogEntry(batchID, missingFile, ex.ToString());
                Console.WriteLine(ex.ToString());
                filesError++;
            }
        }

        // Save changes to the database
        await SaveChangesWithRetry(dbFiles);

        // Update the batch entry with the results
        using var dbFilesUpdate = new CDatabaseImageDBsqliteContext();
        {
            var jobbatch = dbFilesUpdate.Batches.FirstOrDefault(batch => batch.BatchId == batchID);

            // Delete orphaned records
            string deleteImageQuery         = @"DELETE FROM Image WHERE PhotoLibraryId NOT IN (SELECT PhotoLibraryId FROM PhotoLibrary);"; // Delete orphaned images if PhotoLibrary does not exist
            string deleteTagQuery           = @"DELETE FROM relationTag WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = relationTag.ImageId);"; // Delete orphaned tags
            string deletePeopleTagQuery     = @"DELETE FROM relationPeopleTag WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = relationPeopleTag.ImageId);"; // Delete orphaned people tags
            string deleteRegion             = @"DELETE FROM Region WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = Region.ImageId);"; // Delete orphaned regions
            string deleteCollectionQuery    = @"DELETE FROM Collection WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = Collection.ImageId);"; // Delete orphaned images
            string deleteLocationQuery      = @"DELETE FROM Location WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = Location.ImageId);"; // Delete orphaned locations
            string deletePersonQuery        = @"DELETE FROM Person WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = Person.ImageId);"; // Delete orphaned persons

            // Execute the raw SQL command
            dbFiles.Database.ExecuteSqlRaw(deleteImageQuery);
            dbFiles.Database.ExecuteSqlRaw(deleteTagQuery);
            dbFiles.Database.ExecuteSqlRaw(deletePeopleTagQuery);
            dbFiles.Database.ExecuteSqlRaw(deleteRegion);
            dbFiles.Database.ExecuteSqlRaw(deleteCollectionQuery);
            dbFiles.Database.ExecuteSqlRaw(deleteLocationQuery);
            dbFiles.Database.ExecuteSqlRaw(deletePersonQuery);

            Console.WriteLine("[BATCH] - Completed job # " + batchID);
            Console.WriteLine("[RESULTS] - Files: "+imageFiles.Count+" found. " + filesAdded + " added. "+ filesUpdated+" updated. " + filesSkipped + " skipped. " + filesDeleted + " removed. " + filesError+" unable to read.");

            // Warn if not all files were processed
            if (imageFiles.Count != filesAdded + filesUpdated + filesSkipped + filesError)
            {
              Console.WriteLine("[WARNING] - New files may have been skipped, consider running in 'normal' mode for a thorough file scan");
            }

            // Get elapsed time in seconds
            int elapsedTime = 0;
            string endDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            try { elapsedTime = (int)(DateTime.Parse(endDateTime) - DateTime.Parse(jobbatch.StartDateTime)).TotalSeconds; } catch { elapsedTime = 0; }

            string elapsedTimeComment = string.Empty;
            if (elapsedTime >= 3600) // Greater than or equal to 1 hour
            {
                int hours = elapsedTime / 3600;
                int minutes = (elapsedTime % 3600) / 60;
                elapsedTimeComment = $"{hours} hour(s) and {minutes} minute(s).";
                Console.WriteLine($"Elapsed Time: " + elapsedTimeComment);
            }
            else if (elapsedTime >= 60) // Greater than or equal to 1 minute
            {
                int minutes = elapsedTime / 60;
                int seconds = elapsedTime % 60;
                elapsedTimeComment = $"{minutes} minute(s) and {seconds} second(s).";
                Console.WriteLine($"Elapsed Time: " + elapsedTimeComment);
            }
            else // Less than 1 minute
            {
                elapsedTimeComment = $"{elapsedTime} second(s).";
                Console.WriteLine($"Elapsed Time: " + elapsedTimeComment);
            }
            if (jobbatch != null)
            {
                jobbatch.EndDateTime    = endDateTime;
                jobbatch.FilesUpdated   = filesUpdated;
                jobbatch.FilesAdded     = filesAdded;
                jobbatch.FilesSkipped   = filesSkipped;
                jobbatch.FilesRemoved   = filesDeleted;
                jobbatch.FilesReadError = filesError;
                jobbatch.ElapsedTime    = elapsedTime;
                jobbatch.Comment        = elapsedTimeComment;

                // Save changes to the database
                await SaveChangesWithRetry(dbFilesUpdate);
            }

            Console.WriteLine("");
        }

    }
}

async Task UpdateImage(int imageId, string updatedSHA1, int batchID)
{
    string specificFilePath     = string.Empty;
    string jsonMetadata         = string.Empty;
    string structJsonMetadata   = string.Empty;
    // Find the image by ImageId
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        var image = dbFiles.Images.FirstOrDefault(i => i.ImageId == imageId);

        // Check if the image exists
        if (image != null)
        {
            specificFilePath = image.Filepath;
            //string jsonMetadata = GetExiftoolMetadata(specificFilePath);
            jsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath,"");

            if (jsonMetadata == string.Empty)
            {
                // Handle the case where jsonMetadata is empty
                Console.WriteLine("[ERROR] No metadata found for the file: " + specificFilePath);
                LogEntry(-1, specificFilePath, "No metadata found for the file.");
                throw new ArgumentException("No metadata found for the file.");
            }

            // Check if the file has regions
            if ((jsonMetadata.Contains("XMP-mwg-rs:Region") ||
                (jsonMetadata.Contains("XMP-mwg-coll:Collection") ||
                (jsonMetadata.Contains("XMP-iptcExt:PersonInImage")))))
            {
                structJsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath, "mwg");
            }

            // Get the file size and creation/modification dates
            FileInfo fileInfo       = new FileInfo(specificFilePath);
            long fileSize           = fileInfo.Length;
            string fileDateCreated  = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
            string fileDateModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

            // Smart thumbnail regeneration: only if missing or when we detect changes
            byte[]? imageThumbnail = image.Thumbnail; // Preserve existing by default
            string currentPixelHash = image.PixelHash ?? string.Empty;
            
            if (generateThumbnails && image.Thumbnail == null)
            {
                // Generate thumbnail for first time (also computes pixel hash)
                try
                {
                    var (thumb, hash) = ImageToThumbnailBlobWithHash(specificFilePath);
                    imageThumbnail = thumb;
                    currentPixelHash = hash;
                    Console.WriteLine($"[THUMBNAIL] Generated for first time: {specificFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to generate thumbnail for {specificFilePath}: {ex.Message}");
                    imageThumbnail = null;
                }
            }
            else if (image.Thumbnail != null && image.Thumbnail.Length > 0)
            {
                // Existing thumbnail preserved (PixelHash would match or thumbnail was manually generated)
                Console.WriteLine($"[THUMBNAIL] Preserved existing thumbnail: {specificFilePath}");
            }
            // Note: For existing thumbnails, we preserve them on metadata-only updates
            // User can force regeneration by deleting thumbnails (SET Thumbnail=NULL)

            // Update the fields with new values
            image.Filesize          = fileSize;
            image.FileCreatedDate   = fileDateCreated;
            image.FileModifiedDate  = fileDateModified;
            image.Metadata          = jsonMetadata;
            image.StuctMetadata     = structJsonMetadata;
            image.RecordModified    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            image.Thumbnail         = imageThumbnail;
            image.PixelHash         = currentPixelHash; // Store for future comparison 

            // Save changes to the database
            await SaveChangesWithRetry(dbFiles);
        }
    }
        
    await UpdateImageRecord(imageId, updatedSHA1, batchID);
}

async Task AddImage(int photoLibraryID, string photoFolder, int batchId, string specificFilePath, string fileName, string fileExtension, long fileSize, string SHA1)
{
    int imageId                 = 0;
    string jsonMetadata         = string.Empty;
    string structJsonMetadata   = string.Empty;

    // Dictionary to map file extensions to normalized values
    var extensionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
            { "jpg", "jpeg" },
            { "jpeg", "jpeg" },
            { "jxl", "jpeg-xl" },
            { "heic", "heic" }
    };

        jsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath,"");

        if (jsonMetadata == string.Empty)
        {
            // Handle the case where jsonMetadata is empty
            Console.WriteLine("[ERROR] No metadata found for the file: " + specificFilePath);
            LogEntry(-1, specificFilePath, "No metadata found for the file.");
            throw new ArgumentException("No metadata found for the file.");
        }

        // Check if the file has regions
        if ((jsonMetadata.Contains("XMP-mwg-rs:Region") ||
            (jsonMetadata.Contains("XMP-mwg-coll:Collection") ||
            (jsonMetadata.Contains("XMP-iptcExt:PersonInImage")))))
        {
             structJsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath, "mwg");
        }

        // Normalize the file extension
        fileExtension = fileExtension.Replace(".", "").ToLowerInvariant();
        if (extensionMap.TryGetValue(fileExtension, out string normalizedExtension))
        {
            fileExtension = normalizedExtension;
        }

        // Generate thumbnail
        // Generate thumbnail for new images (also computes pixel hash efficiently)
        byte[]? imageThumbnail = null;
        string pixelHash = string.Empty;
        
        if (generateThumbnails)
        {
            try
            {
                var (thumb, hash) = ImageToThumbnailBlobWithHash(specificFilePath);
                imageThumbnail = thumb;
                pixelHash = hash;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to generate thumbnail for {specificFilePath}: {ex.Message}");
                imageThumbnail = null;
            }
        }

    // Add the new image to the database
    using var dbFiles = new CDatabaseImageDBsqliteContext();
        {
            var newImage = new ImageDB.Models.Image
            {
                PhotoLibraryId = photoLibraryID,
                AddedBatchId = batchId,
                Filepath = specificFilePath,
                Album = GetAlbumName(photoFolder, specificFilePath.Replace(fileName, "")),

                Filename = fileName,
                Format = fileExtension,
                Filesize = fileSize,

                Metadata = jsonMetadata,
                StuctMetadata = structJsonMetadata,
                RecordAdded = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                
                Thumbnail = imageThumbnail,
                PixelHash = pixelHash
            };

            dbFiles.Add(newImage);

            await SaveChangesWithRetry(dbFiles);

            imageId = newImage.ImageId;
        }

        await UpdateImageRecord(imageId, SHA1, null);
}

/// <summary>
/// Extracts basic file properties from ExifTool JSON metadata.
/// </summary>
static (string createdDate, string modifiedDate, string filename, string sourceFile) ExtractFileProperties(JsonDocument doc)
{
    string fileCreatedDate = GetFirstNonEmptyExifValue(doc, "System:FileCreateDate");
    string fileModifiedDate = GetFirstNonEmptyExifValue(doc, "System:FileModifyDate");
    string filename = GetFirstNonEmptyExifValue(doc, "System:FileName");
    string sourceFile = GetFirstNonEmptyExifValue(doc, "SourceFile").Replace("/", "\\");

    // Format file datetime to desired format
    fileCreatedDate = DateTime.ParseExact(fileCreatedDate, "yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss");
    fileModifiedDate = DateTime.ParseExact(fileModifiedDate, "yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss");

    return (fileCreatedDate, fileModifiedDate, filename, sourceFile);
}

/// <summary>
/// Extracts title from metadata with MWG fallback precedence.
/// </summary>
static string ExtractTitle(JsonDocument doc)
{
    // No reference for this in the Metadata Working Group 2010 Spec, but it is a common tag used by many applications.
    // IPTC Spec: https://iptc.org/std/photometadata/specification/IPTC-PhotoMetadata#title
    // SaveMetadata.org Ref: https://github.com/fhmwg/current-tags/blob/stage2-essentials/stage2-essentials.md
    // Also reading legacy Windows XP Exif Title tags. The tags are still supported in Windows and written to by some applications such as Windows File Explorer.
    return GetFirstNonEmptyExifValue(doc, new string[] { "XMP-dc:Title", "IPTC:ObjectName", "IFD0:XPTitle" });
}

/// <summary>
/// Extracts description from metadata with MWG fallback precedence.
/// </summary>
static string ExtractDescription(JsonDocument doc)
{
    //Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 36
    // Also reading legacy Windows XP Exif Comment and Subject tags. These tags are still supported in Windows and written to by some applications such as Windows File Explorer.
    return GetFirstNonEmptyExifValue(doc, new string[] { "XMP-dc:Description", "IPTC:Caption-Abstract", "IFD0:ImageDescription", "XMP-tiff:ImageDescription", "ExifIFD:UserComment", "IFD0:XPSubject", "IFD0:XPComment", "IPTC:Headline", "XMP-acdsee:Caption" });
}

/// <summary>
/// Extracts rating from metadata, normalizing percentage values if needed.
/// </summary>
static string ExtractRating(JsonDocument doc)
{
    //Rating - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 41
    //The rating property allows the user to assign a fixed value (often displayed as "stars") to an image. Usually, 1 - 5 star ratings are used. In addition, some tools support negative rating values(such as - 1)
    //that allows for marking "rejects". If ratings are not found in the principal XMP or EXIF values, this code also checks
    //for RatingPercent in EXIF or Microsoft specific XMP tags.

    string rating = NormalizeRatingNumber(GetFirstNonEmptyExifValue(doc, new string[] { "XMP-xmp:Rating", "IFD0:Rating" }));

    if (rating == string.Empty)
    {
        rating = NormalizeRatingPercent(GetFirstNonEmptyExifValue(doc, new string[] { "IFD0:RatingPercent", "XMP-microsoft:RatingPercent" }));
    }

    return rating;
}

/// <summary>
/// Extracts DateTimeTaken with MWG-compliant fallback cascade and timezone handling.
/// </summary>
static (string dateTime, string source, string timezone) ExtractDateTimeTaken(JsonDocument doc, string fileCreatedDate, string fileModifiedDate)
{
    //Get DateTimeTaken - Decending Priority - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 37
    string dateTimeTaken = string.Empty;
    string dateTimeTakenSource = string.Empty;
    string tzDateTime = string.Empty;   // Value which may contain timezone

    //XMP-photoshop:DateCreated (1st option - Preferred)
    if (doc.RootElement.TryGetProperty("XMP-photoshop:DateCreated", out var propertyPhotoshopDate) && !string.IsNullOrWhiteSpace(propertyPhotoshopDate.GetString()))
    {
        dateTimeTaken = ConvertDateToNewFormat(propertyPhotoshopDate.GetString().Trim()) ?? "";
        dateTimeTakenSource = "XMP-photoshop:DateCreated";
        tzDateTime = propertyPhotoshopDate.GetString() ?? "";
    }

    //ExifIFD:DateTimeOriginal (2nd option)
    if (dateTimeTaken == string.Empty)
    {
        if (doc.RootElement.TryGetProperty("ExifIFD:DateTimeOriginal", out var propertyDateTimeOriginal) && !string.IsNullOrWhiteSpace(propertyDateTimeOriginal.GetString()))
        { dateTimeTaken = ConvertDateToNewFormat(propertyDateTimeOriginal.GetString().Trim()) ?? ""; dateTimeTakenSource = "ExifIFD:DateTimeOriginal"; } //Exif DateTime does not contain time-zone information which is stored separately per Exif 2.32 spec. 
    }

    //ExifIFD:CreateDate (3rd option)
    if (dateTimeTaken == string.Empty)
    {
        //ExifIFD:CreateDate
        if (doc.RootElement.TryGetProperty("ExifIFD:CreateDate", out var propertyCreateDate) && !string.IsNullOrWhiteSpace(propertyCreateDate.GetString()))
        { dateTimeTaken = ConvertDateToNewFormat(propertyCreateDate.GetString().Trim()) ?? ""; dateTimeTakenSource = "ExifIFD:CreateDate"; } //Exif DateTime does not contain time-zone information which is stored seperately per Exif 2.32 spec. 
    }

    // XMP-exif:DateTimeOriginal (4th option)
    if (dateTimeTaken == string.Empty)
    {
        // XMP-exif:DateTimeOriginal - Not part of the MWG spec - Use the XMP-exif:DateTimeOriginal as some applications use this.
        if (doc.RootElement.TryGetProperty("XMP-exif:DateTimeOriginal", out var propertyDateTimeCreated) && !string.IsNullOrWhiteSpace(propertyDateTimeCreated.GetString()))
        {
            dateTimeTaken = ConvertDateToNewFormat(propertyDateTimeCreated.GetString().Trim()) ?? ""; dateTimeTakenSource = "XMP-exif:DateTimeOriginal";
            if (tzDateTime == string.Empty) { tzDateTime = propertyDateTimeCreated.GetString() ?? ""; }
        }
    }

    // IPTC Date and Time (5th option)
    if (dateTimeTaken == string.Empty)
    {
        string iptcDate = GetFirstNonEmptyExifValue(doc, "IPTC:DateCreated");
        string iptcTime = GetFirstNonEmptyExifValue(doc, "IPTC:TimeCreated");

        if (iptcDate != string.Empty)
        {
            // Validate the date and time formats
            string pattern = @"^([01]?[0-9]|2[0-3]):([0-5]?[0-9]):([0-5]?[0-9])([+-](0[0-9]|1[0-3]):([0-5][0-9]))?$";

            string iptcDateTime;
            if (Regex.IsMatch(iptcTime, pattern) == true)
            {
                iptcDateTime = iptcDate + " " + iptcTime; // Combine the IPTC date and time strings
                tzDateTime = dateTimeTaken.Trim();        // IPTC may contain Time Zone
            }
            else
            {
                iptcDateTime = iptcDate + " 00:00:00"; // If no time available set to 00:00:00 (Ref https://iptc.org/std/photometadata/specification/IPTC-PhotoMetadata#date-created)
            }

            iptcDateTime = ConvertDateToNewFormat(iptcDateTime.Trim());
            dateTimeTaken = iptcDateTime;
            if (dateTimeTaken != string.Empty)
            {
                dateTimeTakenSource = "IPTC:DateCreated + IPTC:TimeCreated";
            }
        }
    }

    // Select the oldest file system date between created or modified. (6th option)
    if (dateTimeTaken == string.Empty)
    {
        // If all else fails to retrieve dateTime from the file metadata.
        // Not part of the MWG spec - Use the file's system File Creation Date as a last resort for DateTimeTaken.

        if (DateTime.Parse(fileCreatedDate) > DateTime.Parse(fileModifiedDate))
        {
            dateTimeTaken = fileModifiedDate;
            dateTimeTakenSource = "File Modified Date";
        }
        else
        {
            dateTimeTaken = fileCreatedDate;
            dateTimeTakenSource = "File Created Date";
        }
    }

    // Extract TimeZone
    // Get TimeZone - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 33
    // Deviate from MWG standard, which was last updated in 2010 and prefer to populate TimeZone field the the OffsetTimeOriginal timezone property, if it exists. Many smartphone devices automatically set this field already per newer Exif 2.32 spec. 
    string dateTimeTakenTimeZone = string.Empty;
    string offsetTimeOriginal = GetFirstNonEmptyExifValue(doc, "ExifIFD:OffsetTimeOriginal");
    if (offsetTimeOriginal != string.Empty)
    {
        dateTimeTakenTimeZone = offsetTimeOriginal;
    }

    // If the OffsetTimeOriginal property is not available, use the XMP DateTimeOriginal or DateTimeCreated property
    if (dateTimeTakenTimeZone == string.Empty)
    {
        if (DateTimeOffset.TryParseExact(tzDateTime, "yyyy:MM:dd HH:mm:sszzz", null, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset dateTimeOffset))
        {
            // Extract the timezone offset
            TimeSpan timezoneOffset = dateTimeOffset.Offset;
            dateTimeTakenTimeZone = timezoneOffset.ToString();
        }
    }

    if (dateTimeTakenTimeZone != string.Empty)
    {
        dateTimeTakenTimeZone = FormatTimezone(dateTimeTakenTimeZone);
    }

    return (dateTimeTaken, dateTimeTakenSource, dateTimeTakenTimeZone);
}

/// <summary>
/// Extracts GPS coordinates (latitude, longitude, altitude) from metadata.
/// </summary>
static (decimal? latitude, decimal? longitude, decimal? altitude) ExtractGeoCoordinates(JsonDocument doc)
{
    string stringLatitude = GetFirstNonEmptyExifValue(doc, "Composite:GPSLatitude");
    string stringLongitude = GetFirstNonEmptyExifValue(doc, "Composite:GPSLongitude");
    string stringAltitude = GetFirstNonEmptyExifValue(doc, "Composite:GPSAltitude");

    decimal? latitude;
    decimal? longitude;
    decimal? altitude;

    try
    {
        // Round values to GpsCoordinatePrecision decimal places
        stringLatitude = RoundCoordinate(stringLatitude, GpsCoordinatePrecision);
        stringLongitude = RoundCoordinate(stringLongitude, GpsCoordinatePrecision);

        latitude = string.IsNullOrWhiteSpace(stringLatitude) ? null : decimal.Parse(stringLatitude, CultureInfo.InvariantCulture);
        longitude = string.IsNullOrWhiteSpace(stringLongitude) ? null : decimal.Parse(stringLongitude, CultureInfo.InvariantCulture);
    }
    catch (FormatException)
    {
        // Invalid GPS coordinate format - set to null
        latitude = null;
        longitude = null;
    }

    try
    {
        altitude = string.IsNullOrWhiteSpace(stringAltitude) ? null : decimal.Parse(stringAltitude, CultureInfo.InvariantCulture);
    }
    catch (FormatException)
    {
        // Invalid altitude format - set to null
        altitude = null;
    }

    return (latitude, longitude, altitude);
}

/// <summary>
/// Extracts location metadata (location, city, state, country, country code).
/// </summary>
static (string location, string city, string stateProvince, string country, string countryCode) ExtractLocationMetadata(JsonDocument doc)
{
    // MWG 2010 Standard Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 45
    // Ref: https://www.iptc.org/std/photometadata/specification/IPTC-PhotoMetadata
    string[] exiftoolLocationTags = { "XMP-iptcExt:LocationCreatedSublocation", "XMP-iptcCore:Location", "IPTC:Sub-location" };
    string[] exiftoolCityTags = { "XMP-iptcExt:LocationCreatedCity", "XMP-photoshop:City", "IPTC:City" };
    string[] exiftoolStateProvinceTags = { "XMP-iptcExt:LocationCreatedProvinceState", "XMP-photoshop:State", "IPTC:Province-State" };
    string[] exiftoolCountryTags = { "XMP-iptcExt:LocationCreatedCountryName", "XMP-photoshop:Country", "IPTC:Country-PrimaryLocationName" };
    string[] exiftoolCountryCodeTags = { "XMP-iptcExt:LocationCreatedCountryCode", "XMP-iptcCore:CountryCode", "IPTC:Country-PrimaryLocationCode" };

    string location = GetFirstNonEmptyExifValue(doc, exiftoolLocationTags);
    string city = GetFirstNonEmptyExifValue(doc, exiftoolCityTags);
    string stateProvince = GetFirstNonEmptyExifValue(doc, exiftoolStateProvinceTags);
    string country = GetFirstNonEmptyExifValue(doc, exiftoolCountryTags);
    string countryCode = GetFirstNonEmptyExifValue(doc, exiftoolCountryCodeTags);

    return (location, city, stateProvince, country, countryCode);
}

/// <summary>
/// Processes people tags from metadata and updates database relationships.
/// </summary>
static async Task ProcessPeopleTags(CDatabaseImageDBsqliteContext dbFiles, int imageID, JsonDocument doc)
{
    // MWG Region Names - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 51
    // Microsoft People Tags - Ref: https://learn.microsoft.com/en-us/windows/win32/wic/-wic-people-tagging
    // IPTC Extension Person In Image - Ref: https://www.iptc.org/std/photometadata/specification/IPTC-PhotoMetadata#person-shown-in-the-image

    HashSet<string> newPeopleTags = GetExiftoolListValues(doc, new string[] { "XMP-MP:RegionPersonDisplayName", "XMP-mwg-rs:RegionName", "XMP-iptcExt:PersonInImage", "XMP-iptcExt:PersonInImageName" });

    var servicePeopleTags = new PeopleTagService(dbFiles);
    
    // Compare with existing tags - only update if changed
    var existingPeopleTags = await servicePeopleTags.GetExistingPeopleTagNames(imageID);
    
    if (!newPeopleTags.SetEquals(existingPeopleTags))
    {
        // Tags have changed - delete and recreate
        await servicePeopleTags.DeleteRelations(imageID);
        
        foreach (var name in newPeopleTags)
        {
            await servicePeopleTags.AddPeopleTags(name, imageID);
        }
    }
    // else: Tags unchanged, skip delete/insert operations
}

/// <summary>
/// Processes descriptive tags/keywords from metadata and updates database relationships.
/// </summary>
static async Task ProcessDescriptiveTags(CDatabaseImageDBsqliteContext dbFiles, int imageID, JsonDocument doc)
{
    // Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 35
    // Also reading legacy Windows XP Exif keyword tags. The tags are still supported in Windows and written to by some applications such as Windows File Explorer.
    HashSet<string> newDescriptiveTags = GetExiftoolListValues(doc, new string[] { "IPTC:Keywords", "XMP-dc:Subject", "IFD0:XPKeywords" });

    var serviceDescriptiveTags = new DescriptiveTagService(dbFiles);
    
    // Compare with existing tags - only update if changed
    var existingDescriptiveTags = await serviceDescriptiveTags.GetExistingTagNames(imageID);
    
    if (!newDescriptiveTags.SetEquals(existingDescriptiveTags))
    {
        // Tags have changed - delete and recreate
        await serviceDescriptiveTags.DeleteAllRelations(imageID);
        
        foreach (var tag in newDescriptiveTags)
        {
            await serviceDescriptiveTags.AddTags(tag, imageID);
        }
    }
    // else: Tags unchanged, skip delete/insert operations
}

/// <summary>
/// Processes MWG regions, collections, and persons from structured metadata.
/// </summary>
static async Task ProcessMWGStructuredData(CDatabaseImageDBsqliteContext dbFiles, int imageID, string structJsonMetadata, string sourceFile, bool generateRegionThumbnails)
{
    var mwgStruct = new StructService(dbFiles);
    
    // Get existing data for comparison (before deleting)
    var existingRegions = await mwgStruct.GetExistingRegions(imageID);
    var existingCollections = await mwgStruct.GetExistingCollections(imageID);
    var existingPersons = await mwgStruct.GetExistingPersons(imageID);
    var regionIdsToKeep = new List<int>();

    if (structJsonMetadata != string.Empty)
    {
        // Deserialize the JSON string into a MetadataStuct.Struct object
        try
        {
            MetadataStuct.Struct structMeta = JsonSerializer.Deserialize<MetadataStuct.Struct>(structJsonMetadata, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (structMeta != null)
            {
                // Add Regions with smart thumbnail reuse
                try
                {
                    if (structMeta?.RegionInfo?.RegionList != null && structMeta.RegionInfo.RegionList.Any())
                    {
                        foreach (var reg in structMeta.RegionInfo.RegionList)
                        {
                            // Parse region coordinates for comparison
                            decimal? h = decimal.TryParse(reg.Area.H, out var hVal) ? hVal : null;
                            decimal? w = decimal.TryParse(reg.Area.W, out var wVal) ? wVal : null;
                            decimal? x = decimal.TryParse(reg.Area.X, out var xVal) ? xVal : null;
                            decimal? y = decimal.TryParse(reg.Area.Y, out var yVal) ? yVal : null;
                            decimal? d = decimal.TryParse(reg.Area.D, out var dVal) ? dVal : null;
                            
                            // Check if this exact region already exists
                            var matchingRegion = existingRegions.FirstOrDefault(r => 
                                StructService.RegionCoordinatesMatch(r, h, w, x, y, d) &&
                                r.RegionName == reg.Name?.Trim() &&
                                r.RegionType == reg.Type?.Trim() &&
                                r.RegionAreaUnit == reg.Area.Unit?.Trim());
                            
                            if (matchingRegion != null && matchingRegion.RegionThumbnail != null && matchingRegion.RegionThumbnail.Length > 0)
                            {
                                // Region exists with thumbnail - keep it
                                regionIdsToKeep.Add(matchingRegion.RegionId);
                                Console.WriteLine($"[REGION] Preserved existing thumbnail for region: {reg.Name}");
                            }
                            else
                            {
                                // New region or coordinates changed - generate thumbnail
                                byte[]? webpRegionBlob = null;

                                if (generateRegionThumbnails == true)
                                {
                                    webpRegionBlob = ExtractRegionToBlob(sourceFile, reg.Area.H, reg.Area.W, reg.Area.X, reg.Area.Y);
                                    Console.WriteLine($"[REGION] Generated new thumbnail for region: {reg.Name}");
                                }

                                int newRegionId = await mwgStruct.AddRegion(imageID, reg.Name, reg.Type, reg.Area.Unit, reg.Area.H, reg.Area.W, reg.Area.X, reg.Area.Y, reg.Area.D, webpRegionBlob);
                                regionIdsToKeep.Add(newRegionId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle the exception - Log the error message
                    Console.WriteLine("[ERROR] - Failed to process region: " + ex.Message);
                    LogEntry(0, sourceFile, "[Unable to read MWG Region] - " + ex.ToString());
                }
                finally
                {
                    // Delete regions that weren't matched or added
                    await mwgStruct.DeleteRegionsExcept(imageID, regionIdsToKeep);
                }

                // Add Collections (with smart comparison)
                try
                {
                    if (structMeta?.Collections?.Any() == true)
                    {
                        var newCollections = structMeta.Collections
                            .Select(c => (c.CollectionName ?? string.Empty, c.CollectionURI ?? string.Empty))
                            .ToHashSet();
                        
                        if (!newCollections.SetEquals(existingCollections))
                        {
                            // Collections changed - delete and recreate
                            await mwgStruct.DeleteCollections(imageID);
                            
                            foreach (var col in structMeta.Collections)
                            {
                                await mwgStruct.AddCollection(imageID, col.CollectionName, col.CollectionURI);
                            }
                        }
                        // else: Collections unchanged
                    }
                    else if (existingCollections.Count > 0)
                    {
                        // No new collections but existing ones - delete them
                        await mwgStruct.DeleteCollections(imageID);
                    }
                }
                catch (Exception ex)
                {
                    // Handle the exception - Log the error message
                    Console.WriteLine("[ERROR] - Failed to add collection: " + ex.Message);
                    LogEntry(0, sourceFile, "[Unable to read MWG Collection] - " + ex.ToString());
                }

                // Add Persons (with smart comparison)
                try
                {
                    if (structMeta?.PersonInImageWDetails?.Any() == true)
                    {
                        var newPersons = new HashSet<(string name, string identifier)>();
                        foreach (var per in structMeta.PersonInImageWDetails)
                        {
                            string personName = per.PersonName ?? string.Empty;
                            foreach (var perId in per.PersonId)
                            {
                                newPersons.Add((personName, perId ?? string.Empty));
                            }
                        }
                        
                        if (!newPersons.SetEquals(existingPersons))
                        {
                            // Persons changed - delete and recreate
                            await mwgStruct.DeletePersons(imageID);
                            
                            foreach (var per in structMeta.PersonInImageWDetails)
                            {
                                string personName = per.PersonName ?? String.Empty;
                                foreach (var perId in per.PersonId)
                                {
                                    await mwgStruct.AddPerson(imageID, personName, perId);
                                }
                            }
                        }
                        // else: Persons unchanged
                    }
                    else if (existingPersons.Count > 0)
                    {
                        // No new persons but existing ones - delete them
                        await mwgStruct.DeletePersons(imageID);
                    }
                }
                catch (Exception ex)
                {
                    // Handle the exception - Log the error message
                    Console.WriteLine("[ERROR] - Failed to add person: " + ex.Message);
                    LogEntry(0, sourceFile, "[Unable to read MWG Person] - " + ex.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            // Handle the exception - Log the error message
            Console.WriteLine("[ERROR] - Failed to deserialize struct metadata: " + ex.Message);
            LogEntry(0, sourceFile, "[Unable to read MWG Region/Collection] - " + ex.ToString());
        }
    }
}

/// <summary>
/// Processes IPTC location identifiers from metadata.
/// </summary>
static async Task ProcessLocationIdentifiers(CDatabaseImageDBsqliteContext dbFiles, int imageID, JsonDocument doc, string location)
{
    // The location identifier is a URI that can be used to reference the location in other systems.
    // Ref: https://www.iptc.org/std/photometadata/specification/IPTC-PhotoMetadata#location-identifier
    // Ref: https://jmoliver.wordpress.com/2016/03/18/using-iptc-location-identifiers-to-link-your-photos-to-knowledge-bases/
    // When a new location identifier is found, it will be added to the database with the location name of the image.
    // The location name in table Location can be modified by the user.

    HashSet<string> locationCreatedIdentifier = GetExiftoolListValues(doc, new string[] { "XMP-iptcExt:LocationCreatedLocationId" });
    HashSet<string> locationShownIdentifier = GetExiftoolListValues(doc, new string[] { "XMP-iptcExt:LocationShownLocationId" });

    var serviceLocations = new StructService(dbFiles);
    await serviceLocations.DeleteLocations(imageID); // Delete existing location identifiers

    // Add new location identifiers
    foreach (var locationURI in locationCreatedIdentifier)
    {
        await serviceLocations.AddLocation(imageID, location, locationURI, "created");
    }

    foreach (var locationURI in locationShownIdentifier)
    {
        await serviceLocations.AddLocation(imageID, location, locationURI, "shown");
    }
}

async Task UpdateImageRecord(int imageID, string updatedSHA1, int? batchId)
{
    // Initialize variables for metadata extraction
    string jsonMetadata = string.Empty;
    string structJsonMetadata = string.Empty;

    //Get metadata from db
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        jsonMetadata = await dbFiles.Images
            .AsNoTracking()
            .Where(image => image.ImageId == imageID)
            .Select(image => image.Metadata)
            .FirstOrDefaultAsync() ?? "";
        structJsonMetadata = await dbFiles.Images
            .AsNoTracking()
            .Where(image => image.ImageId == imageID)
            .Select(image => image.StuctMetadata)
            .FirstOrDefaultAsync() ?? "";

        //Delete record PeopleTags from db
        bool tagsFound = false;

        var relationPeopleTag = await dbFiles.RelationPeopleTags.FirstOrDefaultAsync(i => i.ImageId == imageID);
        if (relationPeopleTag != null)
        {
            tagsFound = true;
            dbFiles.RelationPeopleTags.Remove(relationPeopleTag);
        }

        var relationTag = await dbFiles.RelationTags.FirstOrDefaultAsync(i => i.ImageId == imageID);

        if (relationTag != null)
        {
            tagsFound = true;
            dbFiles.RelationTags.Remove(entity: relationTag);
        }

        if (tagsFound == true)
        {
            await SaveChangesWithRetry(dbFiles);
        }
    }

    if (jsonMetadata != String.Empty)
    {
        // Parse the JSON string dynamically into a JsonDocument
        using (JsonDocument doc = JsonDocument.Parse(jsonMetadata))
        {
            // Extract file properties
            var (fileCreatedDate, fileModifiedDate, filename, sourceFile) = ExtractFileProperties(doc);

            // Extract basic metadata fields
            string title = ExtractTitle(doc);
            string description = ExtractDescription(doc);
            string rating = ExtractRating(doc);

            // Extract date/time with timezone
            var (dateTimeTaken, dateTimeTakenSource, dateTimeTakenTimeZone) = ExtractDateTimeTaken(doc, fileCreatedDate, fileModifiedDate);

            // Extract device information
            string deviceMake = GetFirstNonEmptyExifValue(doc, "IFD0:Make");
            string deviceModel = GetFirstNonEmptyExifValue(doc, "IFD0:Model");
            string device = ImageDB.DeviceHelper.GetDevice(deviceMake, deviceModel);

            // Extract geocoordinates
            var (latitude, longitude, altitude) = ExtractGeoCoordinates(doc);

            // Extract location metadata
            var (location, city, stateProvince, country, countryCode) = ExtractLocationMetadata(doc);

            // Extract creator and copyright
            string creator = GetFirstNonEmptyExifValue(doc, new string[] { "XMP-dc:Creator", "IPTC:By-line", "IFD0:Artist", "XMP-tiff:Artist", "IFD0:XPAuthor" });
            string copyright = GetFirstNonEmptyExifValue(doc, new string[] { "XMP-dc:Rights", "IPTC:CopyrightNotice", "IFD0:Copyright" });

            // Process people tags
            await ProcessPeopleTags(dbFiles, imageID, doc);

            // Process MWG Regions, Collections, and Persons
            await ProcessMWGStructuredData(dbFiles, imageID, structJsonMetadata, sourceFile, generateRegionThumbnails);

            // Process descriptive tags/keywords
            await ProcessDescriptiveTags(dbFiles, imageID, doc);

            // Process IPTC location identifiers
            await ProcessLocationIdentifiers(dbFiles, imageID, doc, location);

            // Update the image record with extracted metadata
            using var dbFilesUpdate = new CDatabaseImageDBsqliteContext();
            {
                var image = dbFilesUpdate.Images.FirstOrDefault(img => img.ImageId == imageID);

                if (image != null)
                {
                    // Update the Date field (assuming Date is a DateTime property)
                    image.Title = title;
                    image.Description = description;
                    image.Rating = rating;
                    image.DateTimeTaken = dateTimeTaken;
                    image.DateTimeTakenTimeZone = dateTimeTakenTimeZone;
                    image.Device = device;
                    image.Latitude = latitude;
                    image.Longitude = longitude;
                    image.Altitude = altitude;
                    image.Location = location;
                    image.City = city;
                    image.StateProvince = stateProvince;
                    image.Country = country;
                    image.CountryCode = countryCode;
                    image.Creator = creator;
                    image.Copyright = copyright;
                    image.FileCreatedDate = fileCreatedDate;
                    image.FileModifiedDate = fileModifiedDate;
                    image.DateTimeTakenSource = dateTimeTakenSource;

                    // Update the file path and other properties only when necessary. Not needed when executed a metadata reload.
                    if (updatedSHA1 != String.Empty)
                    {
                        image.Sha1 = updatedSHA1;
                        image.ModifiedBatchId = batchId;
                    }

                    // Save the changes to the database
                    await SaveChangesWithRetry(dbFilesUpdate);
                }
            }
        }
    }
}

// Normalize rating number to integer between -1 and 5
static string NormalizeRatingNumber(string inputRatingValue)
{
    // Ref https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 41
    // "The value -1.0 represents a “reject” rating. If a client is not capable of handling float values, it SHOULD round to the closest integer for display
    // and MUST only change the value once the user has changed the rating in the UI. Also, clients MAY store integer numbers. If a value is out of the
    // recommended scope it SHOULD be rounded to closest value.In particular, values > “5.0” SHOULD set to “5.0” as well as all values < “-1.0” SHOULD be set to “-1.0”."

    if (string.IsNullOrWhiteSpace(inputRatingValue.Trim()))
    {
        return string.Empty; // Return empty string if input is null or whitespace
    }

    // Try to parse the string into a decimal value
    if (decimal.TryParse(inputRatingValue, out decimal number))
    {
        // Round the number to the nearest whole number
        int roundedNumber = (int)Math.Round(number);

        // Ensure the number is within the range of -1 to 5
        if (roundedNumber > 5)
        {
            return "5";
        }
        if (roundedNumber < -1)
        {
            return "-1";
        }

        return roundedNumber.ToString();
    }

    // If the input is not a valid number, return empty string.
    return string.Empty;
}

// Normalize rating percentage to 0-5 scale
static string NormalizeRatingPercent(string inputRatingValue)
{
    if (!int.TryParse(inputRatingValue, out int number))
        return string.Empty; // or handle invalid input differently if needed

    if (number < 0)
        return "-1";
    if (number == 0)
        return "0";
    if (number >= 1 && number <= 24)
        return "1";
    if (number >= 25 && number <= 49)
        return "2";
    if (number >= 50 && number <= 74)
        return "3";
    if (number >= 75 && number <= 98)
        return "4";
    if (number >= 99)
        return "5";

    // default fallback (should never hit)
    return string.Empty;
}


// Returns a HashSet of values for the specified ExifTool tags
static HashSet<string> GetExiftoolListValues(JsonDocument doc, string[] exiftoolTags)
{
    HashSet<string> exiftoolValues = new HashSet<string>();

    foreach (var tag in exiftoolTags)
    {
        // Check if the given region property exists
        if (doc.RootElement.TryGetProperty(tag, out JsonElement values))
        {
            //Note: ExifTool can return a single value or an array of values

            // If it's a string, add it directly to the list
            if (values.ValueKind == JsonValueKind.String)
            {
                exiftoolValues.Add(values.GetString());
            }
            else if (values.ValueKind == JsonValueKind.Array)
            {
                // If it's an array, iterate over the array and add each value
                foreach (var name in values.EnumerateArray())
                {
                    exiftoolValues.Add(name.GetString());
                }
            }
        }
    }

    // Return the list of people tags
    return exiftoolValues;
}

// Returns the first non-empty value for the specified ExifTool tags
static string GetFirstNonEmptyExifValue(JsonDocument doc, params string[] exiftoolTags)
{
    foreach (var tag in exiftoolTags)
    {
        // Call GetFirstNonEmptyExifValue for each property name
        string value = GetJsonValue(doc, tag);

        // If a non-empty value is found, return it
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    // If no non-empty value is found, return an empty string
    return string.Empty;
}

// Returns the value for the specified ExifTool tag from JSON document
static string GetJsonValue(JsonDocument doc, string exiftoolTag)
{
    if (doc.RootElement.TryGetProperty(exiftoolTag, out var tagValue) && !string.IsNullOrWhiteSpace(tagValue.GetString()))
    {
        return tagValue.GetString().Trim();
    }
    return string.Empty;
}

// Convert the date string to the new format "yyyy-MM-dd HH:mm:ss" or  simple date format "yyyy-MM-dd"
// Example: "2023:10:01 12:34:56" -> "2023-10-01 12:34:56"
// Example: "2023:10:01 12:34:56.789" -> "2023-10-01 16:34:56"
// Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 37
// Ref: https://savemetadata.org (Dates)
static string ConvertDateToNewFormat(string inputDate)
{
    if (!string.IsNullOrWhiteSpace(inputDate))
    {
        // Remove TimeZone (Z = Zulu time)
        inputDate = inputDate.Replace("Z", "");
        inputDate = inputDate.Split(new[] { '+', '-' })[0];

        // Handle date formats and replace ":" with "-"
        if (inputDate.Length == 4) // Year only (e.g., "2014")
        {
            return inputDate.Replace(":", "-");
        }
        else if (inputDate.Length == 7) // Year and month (e.g., "2014:03")
        {
            return inputDate.Replace(":", "-");
        }
        else if (inputDate.Length == 10) // Year, month, and day (e.g., "2014:03:04")
        {
            return inputDate.Replace(":", "-");
        }

        // Define the input formats for full date with time
        string[] inputFormats =
        {
        "yyyy:MM:dd HH:mm:ss.fff",  // Full date with time and milliseconds
        "yyyy:MM:dd HH:mm:ss"       // Full date with time
        };

        // Try to parse the input string with one of the formats
        if (DateTime.TryParseExact(inputDate, inputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
        {
            // Convert to the desired format "yyyy-MM-dd HH:mm:ss"
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        else
        {
            return string.Empty; //Unable to parse date text
        }
    }

    return string.Empty;
}

static string GetAlbumName(string photoLibrary, string filePath)
{
    filePath = filePath.Replace(photoLibrary, "");
    filePath = filePath.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    filePath = filePath.Replace(Path.DirectorySeparatorChar.ToString(), " - ");
    filePath = filePath.Trim();
    return filePath;
}

static string RoundCoordinate(string stringCoordinate, int decimalPlaces)
{
    // Check if the string is not empty and can be parsed to a decimal
    if (!string.IsNullOrEmpty(stringCoordinate) && decimal.TryParse(stringCoordinate, out decimal coordinate))
    {
        // Round the decimal value to the defined decimal places
        coordinate = Math.Round(coordinate, decimalPlaces);
        // Return the rounded value as a string
        return coordinate.ToString("F"+ decimalPlaces);
    }

    // Return the original string if it's empty or not a valid decimal
    return stringCoordinate;
}
static string FormatTimezone(string input)
{
    // Remove any extra spaces from the input
    input = input.Trim();

    // Replace "Z" (Zulu Time) with "+00:00" to indicate UTC
    input = input.Replace("Z", "+00:00");

    // If the input contains a colon, remove the seconds part if present (e.g., "-04:00:00" -> "-04:00")
    if (input.Contains(":"))
    {
        string[] parts = input.Split(':');
        if (parts.Length >= 2)
        {
            input = $"{parts[0]}:{parts[1]}";
        }
    }

    // If input is a single digit (e.g., "-4" or "4"), convert it to "-04:00" or "+04:00"
    if (int.TryParse(input, out int hour))
    {
        // Handle the sign of the time
        if (hour < 0)
        {
            return $"{hour:D2}:00"; // "-4" becomes "-04:00"
        }
        else
        {
            return $"+{hour:D2}:00"; // "4" becomes "+04:00"
        }
    }

    if (input == "00:00")
    {
        return "+00:00";
    }

    // Handle if the input is not a valid number, return as it is
    return input;
}
static string GetNormalizedFolderPath(string folderPath)
{
    folderPath = folderPath.Trim();
    folderPath = folderPath.Trim('"');
    folderPath = folderPath.Trim('\'');
    folderPath = folderPath.TrimEnd('\\');
    folderPath = folderPath.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // Ensure the folder path is valid and not null or empty
    if (string.IsNullOrEmpty(folderPath))
    {
        throw new ArgumentException("Folder path cannot be null or empty", nameof(folderPath));
    }

    // Check if the directory exists
    if (!Directory.Exists(folderPath))
    {
        throw new DirectoryNotFoundException($"The directory '{folderPath}' does not exist.");
    }

    // Get the full path of the directory
    string fullPath = Path.GetFullPath(folderPath);

    // Return the normalized path with the correct case as it appears in the file system
    return NormalizePathCase(fullPath);
}

static string NormalizePathCase(string folderPath)
{
    // Check for null input and return an empty string
    if (folderPath == null)
    {
        return string.Empty;
    }
    // Get the photo library path (e.g., C:\)
    string root = Path.GetPathRoot(folderPath);

    // Get all directories and the file name if any
    string[] directories = folderPath.Substring(root.Length).Split(Path.DirectorySeparatorChar);

    // Build the normalized path by iterating through each directory
    string normalizedPath = root;
    foreach (var dir in directories)
    {
        if (!string.IsNullOrEmpty(dir))
        {
            // Get the directory with the correct case
            string[] matchingDirs = Directory.GetDirectories(normalizedPath, dir, SearchOption.TopDirectoryOnly);

            if (matchingDirs.Length > 0)
            {
                // Assuming the first matching directory is the correct one with the proper case
                normalizedPath = Path.Combine(normalizedPath, Path.GetFileName(matchingDirs[0]));
            }
            else
            {
                normalizedPath = Path.Combine(normalizedPath, dir);
            }
        }
    }

    return normalizedPath;
}


static string getFileSHA1(string filepath)
{
    using (FileStream stream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, FileReadBufferSize))
    using (SHA1 sha = SHA1.Create())
    {
        byte[] checksum = sha.ComputeHash(stream);
        return BitConverter.ToString(checksum).Replace("-", string.Empty);
    }
}

static async Task CopyImageToMetadataHistory(int imageId)
{
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        // Fetch the Image record from the Image table
         var image = dbFiles.Images.FirstOrDefault(img => img.ImageId == imageId);

        if (image == null)
        {
            throw new Exception("[EXCEPTION] - Image record not found.");
        }

        // Create a new MetadataHistory record and copy the values
        var metadataHistory = new MetadataHistory
        {
            ImageId         = image.ImageId,
            Filepath        = image.Filepath,
            Metadata        = image.Metadata,
            StuctMetadata   = image.StuctMetadata,
            RecordAdded     = image.RecordAdded,
            AddedBatchId    = image.AddedBatchId,
            RecordModified  = image.RecordModified,
            ModifiedBatchId = image.ModifiedBatchId
        };

        dbFiles.MetadataHistories.Add(metadataHistory);

        // Save changes to the database
        await SaveChangesWithRetry(dbFiles);

    }
}


static byte[] ExtractRegionToBlob( string imagePath, string hStr, string wStr, string xStr, string yStr, int maxThumbSize = 384)   // keeps SQLite BLOB sizes small
{
    // Parse MWG-normalized region strings
    double h = double.Parse(hStr, CultureInfo.InvariantCulture);
    double w = double.Parse(wStr, CultureInfo.InvariantCulture);
    double x = double.Parse(xStr, CultureInfo.InvariantCulture);
    double y = double.Parse(yStr, CultureInfo.InvariantCulture);

    using var img = new MagickImage(imagePath);

    // Width/Height are uint in MagickImage — cast explicitly to int for calculations
    int imgWidth = (int)img.Width;
    int imgHeight = (int)img.Height;

    // Convert MWG normalized width/height to pixels (rounded)
    int regionWidth = (int)Math.Round(w * imgWidth);
    int regionHeight = (int)Math.Round(h * imgHeight);

    // Compute MWG center-origin → top-left pixel
    int left = (int)Math.Round((x * imgWidth) - (regionWidth / 2.0));
    int top = (int)Math.Round((y * imgHeight) - (regionHeight / 2.0));

    // Clamp boundaries
    left = Math.Max(0, Math.Min(left, imgWidth - 1));
    top = Math.Max(0, Math.Min(top, imgHeight - 1));

    if (left + regionWidth > imgWidth)
        regionWidth = imgWidth - left;

    if (top + regionHeight > imgHeight)
        regionHeight = imgHeight - top;

    // Crop the region. MagickGeometry expects unsigned width/height in some overloads.
    var cropGeometry = new MagickGeometry(left, top, (uint)Math.Max(0, regionWidth), (uint)Math.Max(0, regionHeight))
    {
        IgnoreAspectRatio = true
    };

    using var region = img.Clone();
    region.Crop(cropGeometry);

    // Clear page offset (replacement for RePage) by resetting Page to the cropped size.
    // Page expects geometry where width/height are unsigned.
    region.Page = new MagickGeometry(0, 0, (uint)region.Width, (uint)region.Height);

    //
    // Thumbnail resize step — reduces DB BLOB size dramatically
    //
    int maxDim = Math.Max((int)region.Width, (int)region.Height);
    if (maxDim > maxThumbSize)
    {
        double scale = (double)maxThumbSize / maxDim;
        int newW = (int)Math.Round(region.Width * scale);
        int newH = (int)Math.Round(region.Height * scale);

        // Use unsigned constructor for MagickGeometry(width, height)
        region.Resize(new MagickGeometry((uint)Math.Max(1, newW), (uint)Math.Max(1, newH))
        {
            IgnoreAspectRatio = false
        });
    }

    // Encode to WebP by setting Format and Quality on the image instance.
    // Avoid using WebPWriteDefines + ToByteArray overload which is not available.
    region.Format = MagickFormat.WebP;

    // Set quality (75 is a reasonable default). Quality is an int property on MagickImage.
    region.Quality = 60;

    // Return encoded bytes (ToByteArray uses current Format)
    return region.ToByteArray();
}

static (byte[] thumbnail, string pixelHash) ImageToThumbnailBlobWithHash(string imagePath, int maxThumbSize = 384)
{
    using var img = new MagickImage(imagePath);

    int imgWidth = (int)img.Width;
    int imgHeight = (int)img.Height;
    
    // Compute pixel hash BEFORE orientation/resizing (on original decoded pixels)
    // Optimization: Use smaller thumbnail for hash instead of full-size RGB (60-90% faster)
    string pixelHash = string.Empty;
    try
    {
        // Create a small normalized version for hashing (256px max dimension)
        int hashMaxDim = Math.Max(imgWidth, imgHeight);
        int hashSize = Math.Min(hashMaxDim, 256); // Hash on 256px version (good balance)
        
        if (hashMaxDim > hashSize)
        {
            double hashScale = (double)hashSize / hashMaxDim;
            int hashW = (int)Math.Round(imgWidth * hashScale);
            int hashH = (int)Math.Round(imgHeight * hashScale);
            
            // Resize in-place (modifies img buffer temporarily)
            img.Resize(new MagickGeometry((uint)Math.Max(hashW, 1), (uint)Math.Max(hashH, 1))
            {
                IgnoreAspectRatio = false
            });
        }
        
        // Strip metadata and hash the resized pixel data (much smaller than original)
        img.Strip();
        byte[] pixelData = img.ToByteArray(MagickFormat.Rgb);
        
        using (SHA1 sha = SHA1.Create())
        {
            byte[] checksum = sha.ComputeHash(pixelData);
            // Optimization: Use StringBuilder or direct hex conversion (faster than Replace)
            pixelHash = string.Concat(checksum.Select(b => b.ToString("X2")));
        }
        
        // Reload original image for thumbnail generation
        img.Read(imagePath);
    }
    catch
    {
        // If pixel hash fails, reload image and continue with thumbnail generation
        try { img.Read(imagePath); } catch { /* Already loaded or unrecoverable */ }
        pixelHash = string.Empty;
    }

    // Apply EXIF orientation for thumbnail (after hashing)
    img.AutoOrient();
    
    imgWidth = (int)img.Width;
    imgHeight = (int)img.Height;

    //
    // Compute proper thumbnail dimensions (preserving aspect ratio)
    //
    int maxDim = Math.Max(imgWidth, imgHeight);
    int newW = imgWidth;
    int newH = imgHeight;

    // Only resize if image exceeds max thumbnail size
    if (maxDim > maxThumbSize)
    {
        double scale = (double)maxThumbSize / maxDim;
        newW = (int)Math.Round(imgWidth * scale);
        newH = (int)Math.Round(imgHeight * scale);
    }

    //
    // Resize internal image buffer
    //
    img.Resize(new MagickGeometry((uint)Math.Max(newW, 1), (uint)Math.Max(newH, 1))
    {
        IgnoreAspectRatio = false
    });

    //
    // Reset page offset (replacement for RePage)
    //
    img.Page = new MagickGeometry(0, 0, (uint)img.Width, (uint)img.Height);

    //
    // Encode as WebP
    //
    img.Format = MagickFormat.WebP;
    img.Quality = 60;   // lightweight thumbnail—tweak as needed

    //
    // Return blob and pixel hash
    //
    return (img.ToByteArray(), pixelHash);
}



