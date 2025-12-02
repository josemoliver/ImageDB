// See https://aka.ms/new-console-template for more information
using ImageDB;
using ImageDB.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Sqlite;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.ComponentModel.Design;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Xml.Linq;
using static ImageDB.MetadataStuct;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using static System.Formats.Asn1.AsnWriter;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;

// ImageDB
// Source Repo & Documentation: https://github.com/josemoliver/ImageDB
// Author: José Oliver-Didier
// License: MIT


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

var photoLibrary            = db.PhotoLibraries.ToList();
string photoFolderFilter    = string.Empty;
string operationMode        = string.Empty;
bool reloadMetadata         = false;
bool quickScan              = false;
bool dateScan               = false;

Console.WriteLine("ImageDB - Scan and update your photo library.");
Console.WriteLine("---------------------------------------------");
Console.WriteLine("Code and Info: https://github.com/josemoliver/ImageDB");
Console.WriteLine("Leveraging the Exiftool utility written by Phil Harvey - https://exiftool.org");
Console.WriteLine("");

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
        photoFolderFilter = String.Empty;
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

        if ((photoFolderFilter == String.Empty) || (photoFolder == photoFolderFilter))
        {

            //Fetch photoLibraryId
            int photoLibraryId = 0;
            photoLibraryId = photoLibrary.FirstOrDefault(pl => pl.Folder.Equals(photoFolder, StringComparison.OrdinalIgnoreCase))?.PhotoLibraryId ?? 0;

            if (photoLibraryId != 0)
            {
                if (reloadMetadata == true)
                {
                    Console.WriteLine("[UPDATE] - Reprocessing metadata folder: " + photoFolder);
                    ReloadMetadata(photoLibraryId);
                }
                else
                {
                    Console.WriteLine("[SCAN] - Scanning folder: " + photoFolder);
                    ScanFiles(photoFolder, photoLibraryId);
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


void LogEntry(int batchId, string filePath, string logEntry)
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

void ReloadMetadata(int photoLibraryId)
{
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        // Query only the ImageIds directly from the database to minimize memory usage
        var imageIdsFromLibrary = dbFiles.Images.Where(img => img.PhotoLibraryId == photoLibraryId).Select(img => img.ImageId).ToList();

        foreach (var imageId in imageIdsFromLibrary)
        {
            UpdateImageRecord(imageId, "", null);
            Console.WriteLine("[UPDATE] - Reprocessing metadata for Image Id: " + imageId);
        }
    }
}

void ScanFiles(string photoFolder, int photoLibraryId)
{
    Console.WriteLine("[START] - Scanning folder for images: "+ photoFolder);
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        var imagesdbTable = dbFiles.Images;
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
            string specificFilePath = string.Empty;
            int imageId = 0;

            // Get current file path
            specificFilePath = imagesdbTable.Where(img => img.Filepath == imageFiles[i].FilePath).Select(img => img.Filepath).FirstOrDefault() ?? "";

            if (specificFilePath == String.Empty)
            {
                // File was not found in db, add it
                SHA1 = getFileSHA1(imageFiles[i].FilePath);        
                Console.WriteLine("[ADD] - " + imageFiles[i].FilePath);
                try
                {
                    AddImage(photoLibraryId, photoFolder, batchID, imageFiles[i].FilePath, imageFiles[i].FileName, imageFiles[i].FileExtension, imageFiles[i].FileSize, SHA1);
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

                if (dateScan == false)
                {
                    SHA1 = getFileSHA1(imageFiles[i].FilePath);
                    imageSHA1 = imagesdbTable.Where(img => img.Filepath == imageFiles[i].FilePath).Select(img => img.Sha1).FirstOrDefault() ?? "";
                }
                else
                {
                    imagelastModifiedDate = imagesdbTable.Where(img => img.Filepath == imageFiles[i].FilePath).Select(img => img.FileModifiedDate).FirstOrDefault() ?? "";
                }



                // Check if the SHA1 hash is different
                if ((SHA1!=imageSHA1)&&(dateScan == false))
                {
                    // File has been modified, update it
                    imageId = imagesdbTable.Where(img => img.Filepath == imageFiles[i].FilePath).Select(img => img.ImageId).FirstOrDefault();
                    Console.WriteLine("[UPDATE] - " + imageFiles[i].FilePath);
                    try
                    {
                        CopyImageToMetadataHistory(imageId);
                        UpdateImage(imageId, SHA1, batchID);
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
                    imageId = imagesdbTable.Where(img => img.Filepath == imageFiles[i].FilePath).Select(img => img.ImageId).FirstOrDefault();
                    Console.WriteLine("[UPDATE] - " + imageFiles[i].FilePath);
                    try
                    {
                        // Update file record
                        CopyImageToMetadataHistory(imageId);
                        UpdateImage(imageId, SHA1, batchID);
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
                    imageId = imagesdbTable.Where(img => img.Filepath == imageFiles[i].FilePath).Select(img => img.ImageId).FirstOrDefault();
                    Console.WriteLine("[UPDATE] - " + imageFiles[i]);
                    try
                    {
                        // Update file record
                        UpdateImage(imageId, SHA1, batchID);
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
        // Get the list of file paths from the database
        var dbFilePaths = imagesdbTable
        .Where(img => img.PhotoLibraryId == photoLibraryId)
        .Select(img => img.Filepath)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Get the list of file paths from the current folder

        List<string> compareImageFiles = new List<string>();

        // Get all files in the directory and subdirectories
        foreach (var imageFile in imageFiles)
        {
            compareImageFiles.Add(imageFile.FilePath);
        }

        // Convert the list to a HashSet for faster lookups
        var folderFilePaths = compareImageFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find files that are in the database but not in the current folder
        var missingFiles = dbFilePaths.Except(folderFilePaths).ToList();

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
        int retryCount = 5;
        while (retryCount-- > 0)
        {
            try
            {
                dbFiles.SaveChanges();
                break;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
            {
                Thread.Sleep(1000); // Wait and retry
            }
        }

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

            string elapsedTimeComment = String.Empty;
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
                retryCount = 5;
                while (retryCount-- > 0)
                {
                    try
                    {
                        dbFilesUpdate.SaveChanges();
                        break;
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
                    {
                        Thread.Sleep(1000); // Wait and retry
                    }
                }
            }

            Console.WriteLine("");
        }

    }
}

async void UpdateImage(int imageId, string updatedSHA1, int batchID)
{
    string specificFilePath     = String.Empty;
    string jsonMetadata         = String.Empty;
    string structJsonMetadata   = String.Empty;
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

            if (jsonMetadata == String.Empty)
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

            // Update the fields with new values
            image.Filesize          = fileSize;
            image.FileCreatedDate   = fileDateCreated;
            image.FileModifiedDate  = fileDateModified;
            image.Metadata          = jsonMetadata;
            image.StuctMetadata     = structJsonMetadata;
            image.RecordModified    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Save changes to the database
            int retryCount = 5;
            while (retryCount-- > 0)
            {
                try
                {
                    dbFiles.SaveChanges();
                    break;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
                {
                    Thread.Sleep(1000); // Wait and retry
                }
            }
        }
    }

    UpdateImageRecord(imageId, updatedSHA1, batchID);
}

async void AddImage(int photoLibraryID, string photoFolder, int batchId, string specificFilePath, string fileName, string fileExtension, long fileSize, string SHA1)
{
    int imageId                 = 0;
    string jsonMetadata         = String.Empty;
    string structJsonMetadata   = String.Empty;

    // Dictionary to map file extensions to normalized values
    var extensionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
            { "jpg", "jpeg" },
            { "jpeg", "jpeg" },
            { "jxl", "jpeg-xl" },
            { "heic", "heic" }
    };

        jsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath,"");

        if (jsonMetadata == String.Empty)
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
                RecordAdded = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            dbFiles.Add(newImage);

            int retryCount = 5;
            while (retryCount-- > 0)
            {
                try
                {
                    dbFiles.SaveChanges();
                    break;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
                {
                    Thread.Sleep(1000); // Wait and retry
                }
            }

            imageId = newImage.ImageId;
        }

        UpdateImageRecord(imageId, SHA1, null);
}

async void UpdateImageRecord(int imageID, string updatedSHA1, int? batchId)
{
        // Initialize variables for metadata extraction
        string jsonMetadata             = String.Empty;
        string structJsonMetadata       = String.Empty;
        string title                    = String.Empty;
        string description              = String.Empty;
        string rating                   = String.Empty;
        string dateTimeTaken            = String.Empty;
        string dateTimeTakenTimeZone    = String.Empty;
        string dateTimeTakenSource      = String.Empty;
        string deviceMake               = String.Empty;
        string deviceModel              = String.Empty;
        string device                   = String.Empty;
        string location                 = String.Empty;
        string city                     = String.Empty;
        string stateProvince            = String.Empty;
        string country                  = String.Empty;
        string countryCode              = String.Empty;
        string creator                  = String.Empty;
        string copyright                = String.Empty;
        string fileCreatedDate          = String.Empty;
        string fileModifiedDate         = String.Empty;
        string filename                 = String.Empty; 
        string stringLatitude           = String.Empty; 
        string stringLongitude          = String.Empty;
        string stringAltitude           = String.Empty; 
        string latitudeRef              = String.Empty;    
        string longitudeRef             = String.Empty; 
        string sourceFile               = String.Empty;
      
        decimal? latitude;
        decimal? longitude;
        decimal? altitude;

        HashSet<string> peopleTag                   = new HashSet<string>();
        HashSet<string> descriptiveTag              = new HashSet<string>();
        HashSet<string> locationCreatedIdentifier   = new HashSet<string>();
        HashSet<string> locationShownIdentifier     = new HashSet<string>();

    //Get metadata from db
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        jsonMetadata = dbFiles.Images.Where(image => image.ImageId == imageID).Select(image => image.Metadata).FirstOrDefault() ?? "";
        structJsonMetadata = dbFiles.Images.Where(image => image.ImageId == imageID).Select(image => image.StuctMetadata).FirstOrDefault() ?? "";

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
            int retryCount = 5;

            while (retryCount-- > 0)
            {
                try
                {
                    dbFiles.SaveChanges();
                    break;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
                {
                    Thread.Sleep(1000); // Wait and retry
                }
            }
        }
    }

    if (jsonMetadata != String.Empty)
    {             

        // Parse the JSON string dynamically into a JsonDocument
        using (JsonDocument doc = JsonDocument.Parse(jsonMetadata))
        {
            //File Properties - Decending Priority
            fileCreatedDate     = GetExiftoolValue(doc, "System:FileCreateDate");
            fileModifiedDate    = GetExiftoolValue(doc, "System:FileModifyDate");
            filename            = GetExiftoolValue(doc, "System:FileName");
            sourceFile          = GetExiftoolValue(doc, "SourceFile").Replace("/","\\");

            // Format file datetime to desired format
            fileCreatedDate     = DateTime.ParseExact(fileCreatedDate, "yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss");
            fileModifiedDate    = DateTime.ParseExact(fileModifiedDate, "yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss");

            // I. Image.Title 

                // No reference for this in the Metadata Working Group 2010 Spec, but it is a common tag used by many applications.
                // IPTC Spec: https://iptc.org/std/photometadata/specification/IPTC-PhotoMetadata#title
                // SaveMetadata.org Ref: https://github.com/fhmwg/current-tags/blob/stage2-essentials/stage2-essentials.md
                // Also reading legacy Windows XP Exif Title tags. The tags are still supported in Windows and written to by some applications such as Windows File Explorer.
                title = GetExiftoolValue(doc, new string[] { "XMP-dc:Title", "IPTC:ObjectName", "IFD0:XPTitle" });

            // II. Image.Description 

                //Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 36
                // Also reading legacy Windows XP Exif Comment and Subject tags. These tags are still supported in Windows and written to by some applications such as Windows File Explorer.
                description = GetExiftoolValue(doc, new string[] { "XMP-dc:Description", "IPTC:Caption-Abstract", "IFD0:ImageDescription","XMP-tiff:ImageDescription", "ExifIFD:UserComment", "IFD0:XPSubject", "IFD0:XPComment", "IPTC:Headline", "XMP-acdsee:Caption" });

            // III. Image.Rating 

            //Rating - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 41
            //The rating property allows the user to assign a fixed value (often displayed as "stars") to an image. Usually, 1 - 5 star ratings are used. In addition, some tools support negative rating values(such as - 1)
            //that allows for marking "rejects". If ratings are not found in the principal XMP or EXIF values, this code also checks
            //for RatingPercent in EXIF or Microsoft specific XMP tags.


                rating = NormalizeRatingNumber(GetExiftoolValue(doc, new string[] { "XMP-xmp:Rating", "IFD0:Rating" }));

                if (rating == String.Empty)
                {
                    rating = NormalizeRatingPercent(GetExiftoolValue(doc, new string[] { "IFD0:RatingPercent", "XMP-microsoft:RatingPercent" }));
                }               


            // IV. Image.DateTimeTaken 

            //Get DateTimeTaken - Decending Priority - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 37
            string tzDateTime   = String.Empty;   // Value which may contain timezone

                //XMP-photoshop:DateCreated (1st option - Preferred)
                if (doc.RootElement.TryGetProperty("XMP-photoshop:DateCreated", out var propertyPhotoshopDate) && !string.IsNullOrWhiteSpace(propertyPhotoshopDate.GetString()))
                { 
                    dateTimeTaken = ConvertDateToNewFormat(propertyPhotoshopDate.GetString().Trim()) ?? "";
                    dateTimeTakenSource = "XMP-photoshop:DateCreated";
                    tzDateTime = propertyPhotoshopDate.GetString() ?? "";
                }

                //ExifIFD:DateTimeOriginal (2nd option)
                if (dateTimeTaken == String.Empty)
                {       
                    if (doc.RootElement.TryGetProperty("ExifIFD:DateTimeOriginal", out var propertyDateTimeOriginal) && !string.IsNullOrWhiteSpace(propertyDateTimeOriginal.GetString()))
                    { dateTimeTaken = ConvertDateToNewFormat(propertyDateTimeOriginal.GetString().Trim()) ?? ""; dateTimeTakenSource = "ExifIFD:DateTimeOriginal"; } //Exif DateTime does not contain time-zone information which is stored separately per Exif 2.32 spec. 
                }

                //ExifIFD:CreateDate (3rd option)
                if (dateTimeTaken == String.Empty)
                {
                    //ExifIFD:CreateDate
                    if (doc.RootElement.TryGetProperty("ExifIFD:CreateDate", out var propertyCreateDate) && !string.IsNullOrWhiteSpace(propertyCreateDate.GetString()))
                    { dateTimeTaken = ConvertDateToNewFormat(propertyCreateDate.GetString().Trim()) ?? ""; dateTimeTakenSource = "ExifIFD:CreateDate"; } //Exif DateTime does not contain time-zone information which is stored seperately per Exif 2.32 spec. 
                }

                // XMP-exif:DateTimeOriginal (4th option)
                if (dateTimeTaken == String.Empty)
                {
                    // XMP-exif:DateTimeOriginal - Not part of the MWG spec - Use the XMP-exif:DateTimeOriginal as some applications use this.
                    if (doc.RootElement.TryGetProperty("XMP-exif:DateTimeOriginal", out var propertyDateTimeCreated) && !string.IsNullOrWhiteSpace(propertyDateTimeCreated.GetString()))
                    { dateTimeTaken = ConvertDateToNewFormat(propertyDateTimeCreated.GetString().Trim()) ?? ""; dateTimeTakenSource = "XMP-exif:DateTimeOriginal";
                    if (tzDateTime== String.Empty) { tzDateTime = propertyDateTimeCreated.GetString() ?? ""; }
                    }
                }

                // IPTC Date and Time (5th option)
                if (dateTimeTaken == String.Empty)
                {
                    string iptcDate     = String.Empty;   // IPTC Date       
                    string iptcTime     = String.Empty;   // IPTC Time
                    string iptcDateTime = String.Empty;   // IPTC DateTime

                    iptcDate = GetExiftoolValue(doc, "IPTC:DateCreated");
                    iptcTime = GetExiftoolValue(doc, "IPTC:TimeCreated");

                    if (iptcDate != String.Empty)
                    {
                        // Validate the date and time formats
                        string pattern = @"^([01]?[0-9]|2[0-3]):([0-5]?[0-9]):([0-5]?[0-9])([+-](0[0-9]|1[0-3]):([0-5][0-9]))?$";

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
                    }

                    dateTimeTaken = iptcDateTime;
                    if (dateTimeTaken != String.Empty)
                    {
                        dateTimeTakenSource = "IPTC:DateCreated + IPTC:TimeCreated";
                    }
                }      
           
                // Select the oldest file system date between created or modified. (6th option)
                if (dateTimeTaken == String.Empty)
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


            // V. Image.TimeZone

                // Get TimeZone - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 33
                // Deviate from MWG standard, which was last updated in 2010 and prefer to populate TimeZone field the the OffsetTimeOriginal timezone property, if it exists. Many smartphone devices automatically set this field already per newer Exif 2.32 spec. 
                string offsetTimeOriginal = GetExiftoolValue(doc, "ExifIFD:OffsetTimeOriginal");
                if (offsetTimeOriginal!=String.Empty)
                {
                    dateTimeTakenTimeZone = offsetTimeOriginal;
                }

                // If the OffsetTimeOriginal property is not available, use the XMP DateTimeOriginal or DateTimeCreated property
                if (dateTimeTakenTimeZone == String.Empty)
                {
                    if (DateTimeOffset.TryParseExact(tzDateTime, "yyyy:MM:dd HH:mm:sszzz", null, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset dateTimeOffset))
                    {
                        // Extract the timezone offset
                        TimeSpan timezoneOffset = dateTimeOffset.Offset;
                        dateTimeTakenTimeZone = timezoneOffset.ToString();
                    }
                }

                if (dateTimeTakenTimeZone != String.Empty)
                {
                    dateTimeTakenTimeZone = FormatTimezone(dateTimeTakenTimeZone);
                }


            // VI. Image.Device

                // Get Device Model and Make (Exif Values)
                deviceMake  = GetExiftoolValue(doc,"IFD0:Make");
                deviceModel = GetExiftoolValue(doc,"IFD0:Model");

                // How the Make and Model values differ by device manufacturers. For the database field of "Device", we will use the device name as defined in the ImageDB.DeviceHelper.GetDevice method.
                // Combining Make and Model into a single field "Device" for presentation purposes.
                device = ImageDB.DeviceHelper.GetDevice(deviceMake, deviceModel);

            // VII. Geocoordinates - Image.Latitude, Image.Longitude, and Image.Altitude
                stringLatitude  = GetExiftoolValue(doc, "Composite:GPSLatitude");
                stringLongitude = GetExiftoolValue(doc, "Composite:GPSLongitude");
                stringAltitude  = GetExiftoolValue(doc, "Composite:GPSAltitude");

                try
                {
                    // Round values to 6 decimal places
                    stringLatitude  = RoundCoordinate(stringLatitude, 6);
                    stringLongitude = RoundCoordinate(stringLongitude, 6);

                    latitude    = string.IsNullOrWhiteSpace(stringLatitude) ? null : decimal.Parse(stringLatitude, CultureInfo.InvariantCulture);
                    longitude   = string.IsNullOrWhiteSpace(stringLongitude) ? null : decimal.Parse(stringLongitude, CultureInfo.InvariantCulture);
                }
                catch
                {
                    latitude    = null;
                    longitude   = null;
                }

                try
                {
                    altitude = string.IsNullOrWhiteSpace(stringAltitude) ? null : decimal.Parse(stringAltitude, CultureInfo.InvariantCulture);
                }
                catch 
                {
                    altitude = null;
                }


            // VIII. Geotags

                // MWG 2010 Standard Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 45
                // Ref: https://www.iptc.org/std/photometadata/specification/IPTC-PhotoMetadata
                string[] exiftoolLocationTags       = { "XMP-iptcExt:LocationCreatedSublocation", "XMP-iptcCore:Location", "IPTC:Sub-location" };
                string[] exiftoolCityTags           = { "XMP-iptcExt:LocationCreatedCity", "XMP-photoshop:City", "IPTC:City" };
                string[] exiftoolStateProvinceTags  = { "XMP-iptcExt:LocationCreatedProvinceState", "XMP-photoshop:State", "IPTC:Province-State" };
                string[] exiftoolCountryTags        = { "XMP-iptcExt:LocationCreatedCountryName", "XMP-photoshop:Country", "IPTC:Country-PrimaryLocationName" };
                string[] exiftoolCountryCodeTags    = { "XMP-iptcExt:LocationCreatedCountryCode", "XMP-iptcCore:CountryCode", "IPTC:Country-PrimaryLocationCode" };

                location        = GetExiftoolValue(doc, exiftoolLocationTags);
                city            = GetExiftoolValue(doc, exiftoolCityTags);
                stateProvince   = GetExiftoolValue(doc, exiftoolStateProvinceTags);
                country         = GetExiftoolValue(doc, exiftoolCountryTags);
                countryCode     = GetExiftoolValue(doc, exiftoolCountryCodeTags);

            // IX. Get Creator - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 43
            
                // Also reading legacy Windows Exif XPAuthor value. The tags are still supported in Windows and written to by some applications such as Windows File Explorer.
                creator = GetExiftoolValue(doc, new string[] { "XMP-dc:Creator", "IPTC:By-line", "IFD0:Artist", "XMP-tiff:Artist", "IFD0:XPAuthor" });

            // X.  Get Copyright - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 42
                
                copyright = GetExiftoolValue(doc, new string[] { "XMP-dc:Rights", "IPTC:CopyrightNotice", "IFD0:Copyright" });


            // XI. Get People tags/names - Support various schemas for people tags

                // MWG Region Names - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 51
                // Microsoft People Tags - Ref: https://learn.microsoft.com/en-us/windows/win32/wic/-wic-people-tagging
                // IPTC Extension Person In Image - Ref: https://www.iptc.org/std/photometadata/specification/IPTC-PhotoMetadata#person-shown-in-the-image

                peopleTag = GetExiftoolListValues(doc, new string[] { "XMP-MP:RegionPersonDisplayName", "XMP-mwg-rs:RegionName", "XMP-iptcExt:PersonInImage", "XMP-iptcExt:PersonInImageName" });

                var servicePeopleTags = new PeopleTagService(dbFiles);

                await servicePeopleTags.DeleteRelations(imageID); // Delete existing tags

                foreach (var name in peopleTag)
                {         
                    await servicePeopleTags.AddPeopleTags(name, imageID);
                }



            // XII. Get MWG Regions and Collections - Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 51

            // Delete existing regions and collections.
            var mwgStruct = new StructService(dbFiles);
            await mwgStruct.DeleteRegions(imageID);
            await mwgStruct.DeleteCollections(imageID);
            await mwgStruct.DeletePersons(imageID);

            if (structJsonMetadata != String.Empty)
            {
                // Deserialize the JSON string into a MetadataStuct.Struct object
                try
                {
                    MetadataStuct.Struct structMeta = JsonSerializer.Deserialize<MetadataStuct.Struct>(structJsonMetadata, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (structMeta != null)
                    {
                        // Add Regions
                        try
                        {
                            if (structMeta?.RegionInfo?.RegionList != null && structMeta.RegionInfo.RegionList.Any())
                            {
                                foreach (var reg in structMeta.RegionInfo.RegionList)
                                {
                                    await mwgStruct.AddRegion(imageID, reg.Name, reg.Type, reg.Area.Unit, reg.Area.H, reg.Area.W, reg.Area.X, reg.Area.Y, reg.Area.D);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Handle the exception - Log the error message
                            Console.WriteLine("[ERROR] - Failed to add region: " + ex.Message);
                            LogEntry(0, sourceFile, "[Unable to read MWG Region] - " + ex.ToString());
                        }

                        // Add Collections
                        try
                        {
                            if (structMeta?.Collections?.Any() == true)
                            {
                                foreach (var col in structMeta.Collections)
                                {
                                    await mwgStruct.AddCollection(imageID, col.CollectionName, col.CollectionURI);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Handle the exception - Log the error message
                            Console.WriteLine("[ERROR] - Failed to add collection: " + ex.Message);
                            LogEntry(0, sourceFile, "[Unable to read MWG Collection] - " + ex.ToString());
                        }

                        // Add Persons
                        try
                        {
                            if (structMeta?.PersonInImageWDetails?.Any() == true)
                            {
                                foreach (var per in structMeta.PersonInImageWDetails)
                                {
                                    //If person name is not available, use empty string  
                                    string personName = per.PersonName ?? String.Empty;

                                    //Add each PersonId associated with the person
                                    foreach (var perId in per.PersonId)
                                    {
                                        await mwgStruct.AddPerson(imageID, personName, perId);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Handle the exception - Log the error message
                            Console.WriteLine("[ERROR] - Failed to add collection: " + ex.Message);
                            LogEntry(0, sourceFile, "[Unable to read MWG Collection] - " + ex.ToString());
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

            // XIII. Get Descriptive tags/keywords

            // Ref: https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf page 35
            // Also reading legacy Windows XP Exif keyword tags. The tags are still supported in Windows and written to by some applications such as Windows File Explorer.
            descriptiveTag = GetExiftoolListValues(doc, new string[] { "IPTC:Keywords", "XMP-dc:Subject", "IFD0:XPKeywords" });

                var serviceDescriptiveTags = new DescriptiveTagService(dbFiles);

            await serviceDescriptiveTags.DeleteAllRelations(imageID); // Delete existing tags

            foreach (var tag in descriptiveTag)
            {
                    await serviceDescriptiveTags.AddTags(tag, imageID);
            }

            // XIV. Get IPTC Location Identifiers - Introduced by IPTC in the 2014 IPTC Extension Standard.

                // The location identifier is a URI that can be used to reference the location in other systems.
                // Ref: https://www.iptc.org/std/photometadata/specification/IPTC-PhotoMetadata#location-identifier
                // Ref: https://jmoliver.wordpress.com/2016/03/18/using-iptc-location-identifiers-to-link-your-photos-to-knowledge-bases/
                // When a new location identifier is found, it will be added to the database with the location name of the image.
                // The location name in table Location can be modified by the user.

                locationCreatedIdentifier = GetExiftoolListValues(doc, new string[] { "XMP-iptcExt:LocationCreatedLocationId" });
                locationShownIdentifier   = GetExiftoolListValues(doc, new string[] { "XMP-iptcExt:LocationShownLocationId" });

                var serviceLocations = new StructService(dbFiles);

                await serviceLocations.DeleteLocations(imageID); // Delete existing location identifiers

                // Add new location identifiers
                foreach (var locationURI in locationCreatedIdentifier)
                {
                       await serviceLocations.AddLocation(imageID,location,locationURI,"created");
                }

                foreach (var locationURI in locationShownIdentifier)
                {
                       await serviceLocations.AddLocation(imageID, location, locationURI, "shown");
                }

        }

            using var dbFilesUpdate = new CDatabaseImageDBsqliteContext();
            {
                var image = dbFilesUpdate.Images.FirstOrDefault(img => img.ImageId == imageID);

                if (image != null)
                {
                // Update the Date field (assuming Date is a DateTime property)
                image.Title                     = title;
                image.Description               = description;
                image.Rating                    = rating;
                image.DateTimeTaken             = dateTimeTaken;
                image.DateTimeTakenTimeZone     = dateTimeTakenTimeZone;
                image.Device                    = device;
                image.Latitude                  = latitude;
                image.Longitude                 = longitude;
                image.Altitude                  = altitude;
                image.Location                  = location;
                image.City                      = city; 
                image.StateProvince             = stateProvince;
                image.Country                   = country;
                image.CountryCode               = countryCode;
                image.Creator                   = creator;
                image.Copyright                 = copyright;
                image.FileCreatedDate           = fileCreatedDate;
                image.FileModifiedDate          = fileModifiedDate;
                image.DateTimeTakenSource       = dateTimeTakenSource;

                // Update the file path and other properties only when necessary. Not needed when executed a metadata reload.
                if (updatedSHA1 != String.Empty)
                {
                    image.Sha1              = updatedSHA1;
                    image.ModifiedBatchId   = batchId;
                }

                // Save the changes to the database
                int retryCount = 5;
                    while (retryCount-- > 0)
                    {
                        try
                        {
                            dbFilesUpdate.SaveChanges();
                            break;
                        }
                        catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
                        {
                            Thread.Sleep(1000); // Wait and retry
                        }
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
        return String.Empty; // Return empty string if input is null or whitespace
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
    return String.Empty;
}

// Normalize rating percentage to 0-5 scale
static string NormalizeRatingPercent(string inputRatingValue)
{
    if (!int.TryParse(inputRatingValue, out int number))
        return String.Empty; // or handle invalid input differently if needed

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
    return String.Empty;
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

static string[] GetStringExiftoolListValues(JsonDocument doc, string[] exiftoolTags)
{
    List<string> exiftoolValues = new();

    foreach (var tag in exiftoolTags)
    {
        // Check if the given property exists
        if (doc.RootElement.TryGetProperty(tag, out JsonElement values))
        {
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
                    if (name.ValueKind == JsonValueKind.String)
                        exiftoolValues.Add(name.GetString());
                }
            }
        }
    }

    return exiftoolValues.ToArray();
}

// Returns the first non-empty value for the specified ExifTool tags
static string GetExiftoolValue(JsonDocument doc, params string[] exiftoolTags)
{
    foreach (var tag in exiftoolTags)
    {
        // Call GetExiftoolValue for each property name
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
            return String.Empty; //Unable to parse date text
        }
    }

    return String.Empty;
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
        // Round the decimal value to defined decimal places
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
    const int bufferSize = 8192; // 8KB buffer size

    using (FileStream stream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize))
    using (SHA1 sha = SHA1.Create())
    {
        byte[] checksum = sha.ComputeHash(stream);
        return BitConverter.ToString(checksum).Replace("-", string.Empty);
    }
}

static void CopyImageToMetadataHistory(int imageId)
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
        int retryCount = 5;
        while (retryCount-- > 0)
        {
            try
            {
                dbFiles.SaveChanges();
                break;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // database is locked
            {
                Thread.Sleep(1000); // Wait and retry
            }
        }

    }
}



