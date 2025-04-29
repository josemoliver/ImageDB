// See https://aka.ms/new-console-template for more information
using ImageDB.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.IO;
using System.ComponentModel.Design;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
using System.Text.Json.Nodes;
using static System.Net.Mime.MediaTypeNames;
using System.Xml.Linq;
using System.Globalization;
using System.Diagnostics.Metrics;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using System.Text.Encodings.Web;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using Microsoft.Extensions.Logging.Abstractions;
using ImageDB;

var rootCommand = new RootCommand
{
    new Option<string>(
        "--folder",
        description: "Path to specific library to scan."
    ),
    new Option<string>(
        "--mode",
        description: "Set modes: normal (default) | date | quick | reload"
    )
};


using var db = new CDatabaseImageDBsqliteContext();

var photoLibrary            = db.PhotoLibraries.ToList();
string photoFolderFilter    = string.Empty;
bool reloadMetadata         = false;
bool quickScan              = false;
bool dateScan               = false;

// Handler to process the command-line arguments
rootCommand.Handler = CommandHandler.Create((string folder, string mode) =>
{
    photoFolderFilter = folder;
    // Determine the mode and set appropriate flags
    reloadMetadata = string.Equals(mode, "reload", StringComparison.OrdinalIgnoreCase);

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
        SetScanMode(mode);

        Console.WriteLine("[START] - Scanning for new and updated files.");

    }

    return Task.CompletedTask;
});


// Parse and invoke the command
await rootCommand.InvokeAsync(args);


if (string.IsNullOrEmpty(photoFolderFilter))
{
    photoFolderFilter = "";
    Console.WriteLine("[INFO] - No filter applied.");
}
else
{
    photoFolderFilter = GetNormalizedFolderPath(photoFolderFilter);
    Console.WriteLine("[INFO] - Filtered by folder: " + photoFolderFilter);
}

foreach (var folder in photoLibrary)
{
    //Normalize Folder Path
    string photoFolder = folder.Folder.ToString()??"";
    photoFolder = GetNormalizedFolderPath(photoFolder);

    if ((photoFolderFilter == "") || (photoFolder == photoFolderFilter))
    {       

            //Fetch photoLibraryId
            int photoLibraryId = 0;
            photoLibraryId = photoLibrary.FirstOrDefault(pl => pl.Folder.Equals(photoFolder, StringComparison.OrdinalIgnoreCase))?.PhotoLibraryId ?? 0;

            if (photoLibraryId != 0)
            {
                if (reloadMetadata == true)
                {
                    ReloadMetadata(photoLibraryId);
                }
                else
                {
                    ScanFiles(photoFolder, photoLibraryId);
                }
  
            }        
    }
}

// Shutdown ExifTool process
ExifToolHelper.Shutdown();


// Method to set the scan mode based on the input
void SetScanMode(string mode)
{
    // Default to normal mode
    dateScan = false;
    quickScan = false;
    
    if (string.IsNullOrEmpty(mode) || string.Equals(mode, "normal", StringComparison.OrdinalIgnoreCase))
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
        var imagesdbTable = dbFiles.Images.ToList();
        var imagesFromLibrary = imagesdbTable.Where(img => img.PhotoLibraryId == photoLibraryId).Select(img => img.ImageId).ToList();

        foreach (var imageId in imagesFromLibrary)
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
        var imagesdbTable = dbFiles.Images.ToList();

        //List<string> imageFiles = new List<string>();
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

            imageFiles.Add(new ImageFile(file.FullName.ToString(), file.LastWriteTime.ToString("yyyy-MM-dd hh:mm:ss tt"), file.Extension.ToString().ToLower(), file.Name.ToString(),file.Length.ToString(),file.CreationTime.ToString("yyyy-MM-dd hh:mm:ss tt")));         
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

        // Flag to suspend the scan
        bool suspendScan = false;
        
        Console.WriteLine("[BATCH] - Started job. " + imageFiles.Count + " files Found.");

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

            if (specificFilePath == "")
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

            // Delete orphaned relation records
            string deleteTagQuery           = @"DELETE FROM relationTag WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = relationTag.ImageId);";
            string deletePeopleTagQuery     = @"DELETE FROM relationPeopleTag WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = relationPeopleTag.ImageId);";
            string deleteLocationTagQuery   = @"DELETE FROM relationLocation WHERE NOT EXISTS (SELECT 1 FROM Image WHERE Image.ImageId = relationLocation.ImageId);";

            // Execute the raw SQL command
            dbFiles.Database.ExecuteSqlRaw(deleteTagQuery);
            dbFiles.Database.ExecuteSqlRaw(deletePeopleTagQuery);
            dbFiles.Database.ExecuteSqlRaw(deleteLocationTagQuery);

            Console.WriteLine("[BATCH] - Completed batch Id: " + batchID);
            Console.WriteLine("[RESULTS] - Files: "+imageFiles.Count+" found. " + filesAdded + " added. "+ filesUpdated+" updated. " + filesSkipped + " skipped. " + filesDeleted + " removed. " + filesError+" unable to read.");
            
            // Get elapsed time in seconds
            int elapsedTime = 0;
            string endDateTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

            try { elapsedTime = (int)(DateTime.Parse(endDateTime) - DateTime.Parse(jobbatch.StartDateTime)).TotalSeconds; } catch { elapsedTime = 0; }

            string elapsedTimeComment = "";
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
        }

    }
}

async void UpdateImage(int imageId, string updatedSHA1, int batchID)
{
    string specificFilePath = string.Empty;
    // Find the image by ImageId
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        var image = dbFiles.Images.FirstOrDefault(i => i.ImageId == imageId);

        // Check if the image exists
        if (image != null)
        {
            specificFilePath = image.Filepath;
            //string jsonMetadata = GetExiftoolMetadata(specificFilePath);
            string jsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath);

            if (jsonMetadata == "")
            {
                // Handle the case where jsonMetadata is empty
                Console.WriteLine("[ERROR] No metadata found for the file: " + specificFilePath);
                LogEntry(-1, specificFilePath, "No metadata found for the file.");
                throw new ArgumentException("No metadata found for the file.");
            }

            // Get the file size and creation/modification dates
            FileInfo fileInfo       = new FileInfo(specificFilePath);
            long fileSize           = fileInfo.Length;
            string fileDateCreated  = fileInfo.CreationTime.ToString("yyyy-MM-dd hh:mm:ss tt");
            string fileDateModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd hh:mm:ss tt");

            // Update the fields with new values
            image.Filesize          = fileSize.ToString();
            image.FileCreatedDate   = fileDateCreated;
            image.FileModifiedDate  = fileDateModified;
            image.Metadata          = jsonMetadata;
            image.RecordModified    = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

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

async void AddImage(int photoLibraryID, string photoFolder, int batchId, string specificFilePath, string fileName, string fileExtension, string fileSize, string SHA1)
{
    int imageId = 0;
    
    // Dictionary to map file extensions to normalized values
    var extensionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
            { "jpg", "jpeg" },
            { "jpeg", "jpeg" },
            { "jxl", "jpeg-xl" },
            { "heic", "heic" }
    };

        string jsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath);

        if (jsonMetadata == "")
        {
            // Handle the case where jsonMetadata is empty
            Console.WriteLine("[ERROR] No metadata found for the file: " + specificFilePath);
            LogEntry(-1, specificFilePath, "No metadata found for the file.");
            throw new ArgumentException("No metadata found for the file.");
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
                Filesize = fileSize.ToString(),

                Metadata = jsonMetadata,
                RecordAdded = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")
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
        string jsonMetadata;
        string title                    = "";
        string description              = "";
        string rating                   = "";
        string dateTimeTaken            = "";
        string dateTimeTakenTimeZone    = "";
        string deviceMake               = "";
        string deviceModel              = "";
        string device                   = "";
        string location                 = "";
        string city                     = "";
        string stateProvince            = "";
        string country                  = "";
        string countryCode              = "";
        string creator                  = "";
        string copyright                = "";
        string fileCreatedDate          = "";
        string fileModifiedDate         = "";

        string stringLatitude           = "";
        string stringLongitude          = "";
        string stringAltitude           = "";
        string latitudeRef              = "";   
        string longitudeRef             = "";
    
        decimal? latitude;
        decimal? longitude;
        decimal? altitude;

        HashSet<string> peopleTag           = new HashSet<string>();
        HashSet<string> descriptiveTag      = new HashSet<string>();
        HashSet<string> locationIdentifier  = new HashSet<string>();

    //Get metadata from db
    using var dbFiles = new CDatabaseImageDBsqliteContext();
    {
        jsonMetadata = dbFiles.Images.Where(image => image.ImageId == imageID).Select(image => image.Metadata).FirstOrDefault() ?? "";
     

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

    if (jsonMetadata != "")
    {             

        // Parse the JSON string dynamically into a JsonDocument
        using (JsonDocument doc = JsonDocument.Parse(jsonMetadata))
            {
                //File Properties
                if (doc.RootElement.TryGetProperty("System:FileCreateDate", out var propertyFileDateCreated) && !string.IsNullOrWhiteSpace(propertyFileDateCreated.GetString()))
                { fileCreatedDate = propertyFileDateCreated.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("System:FileModifyDate", out var propertyFileDateModified) && !string.IsNullOrWhiteSpace(propertyFileDateModified.GetString()))
                { fileModifiedDate = propertyFileDateModified.GetString().Trim() ?? ""; }


                //Title
                if (doc.RootElement.TryGetProperty("IFD0:XPTitle", out var propertyXPTitle) && !string.IsNullOrWhiteSpace(propertyXPTitle.GetString()))
                { title = propertyXPTitle.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("IPTC:Headline", out var propertyHeadline) && !string.IsNullOrWhiteSpace(propertyHeadline.GetString()))
                { title = propertyHeadline.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("IPTC:ObjectName", out var propertyObjectName) && !string.IsNullOrWhiteSpace(propertyObjectName.GetString()))
                { title = propertyObjectName.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("XMP-dc:Title", out var propertyTitle) && !string.IsNullOrWhiteSpace(propertyTitle.GetString()))
                { title = propertyTitle.GetString().Trim() ?? ""; }

                //Description
                if (doc.RootElement.TryGetProperty("IFD0:XPComment", out var propertyXPComment) && !string.IsNullOrWhiteSpace(propertyXPComment.GetString()))
                { description = propertyXPComment.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("XMP-tiff:ImageDescription", out var propertyTiffImageDescription) && !string.IsNullOrWhiteSpace(propertyTiffImageDescription.GetString()))
                { description = propertyTiffImageDescription.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("ExifIFD:UserComment", out var propertyUserComment) && !string.IsNullOrWhiteSpace(propertyUserComment.GetString()))
                { description = propertyUserComment.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("IFD0:ImageDescription", out var propertyImageDescription) && !string.IsNullOrWhiteSpace(propertyImageDescription.GetString()))
                { description = propertyImageDescription.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("IPTC:Caption-Abstract", out var propertyCaptionAbstract) && !string.IsNullOrWhiteSpace(propertyCaptionAbstract.GetString()))
                { description = propertyCaptionAbstract.GetString().Trim() ?? ""; }
                if (doc.RootElement.TryGetProperty("XMP-dc:Description", out var propertyDescription) && !string.IsNullOrWhiteSpace(propertyDescription.GetString()))
                { description = propertyDescription.GetString().Trim() ?? ""; }

                //Rating
                if (doc.RootElement.TryGetProperty("XMP-xmp:Rating", out var propertyXMPRating) && !string.IsNullOrWhiteSpace(propertyXMPRating.GetString()))
                { rating = propertyXMPRating.GetString() ?? ""; }
                if (doc.RootElement.TryGetProperty("IFD0:Rating", out var propertyRating) && !string.IsNullOrWhiteSpace(propertyRating.GetString()))
                { rating = propertyRating.GetString() ?? ""; }

                // Get DateTimeTaken
                string xmpDateTime = "";
                dateTimeTaken = fileCreatedDate;
             //   if (doc.RootElement.TryGetProperty("Composite:DateTimeCreated", out var propertyDateTimeCreated) && !string.IsNullOrWhiteSpace(propertyDateTimeCreated.GetString()))
             //   { dateTimeTaken = propertyDateTimeCreated.GetString() ?? ""; xmpDateTime = propertyDateTimeCreated.GetString() ?? ""; }
                if (doc.RootElement.TryGetProperty("XMP-photoshop:DateCreated", out var propertyPhotoshopDate) && !string.IsNullOrWhiteSpace(propertyPhotoshopDate.GetString()))
                { dateTimeTaken = propertyPhotoshopDate.GetString() ?? ""; }
                if (doc.RootElement.TryGetProperty("ExifIFD:CreateDate", out var propertyCreateDate) && !string.IsNullOrWhiteSpace(propertyCreateDate.GetString()))
                { dateTimeTaken = propertyCreateDate.GetString() ?? ""; }
                if (doc.RootElement.TryGetProperty("ExifIFD:DateTimeOriginal", out var propertyDateTimeOriginal) && !string.IsNullOrWhiteSpace(propertyDateTimeOriginal.GetString()))
                { dateTimeTaken = propertyDateTimeOriginal.GetString() ?? "";  }

                dateTimeTaken = dateTimeTaken.Trim();

                //Get TimeZone
                if (doc.RootElement.TryGetProperty("ExifIFD:OffsetTimeOriginal", out var propertyOffsetTimeOriginal) && !string.IsNullOrWhiteSpace(propertyOffsetTimeOriginal.GetString()))
                { 
                    dateTimeTakenTimeZone = propertyOffsetTimeOriginal.GetString() ?? ""; 
                }

                if (dateTimeTakenTimeZone == "")
                {
                    if (DateTimeOffset.TryParseExact(xmpDateTime, "yyyy:MM:dd HH:mm:sszzz", null, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset dateTimeOffset))
                    {
                        // Extract the timezone offset
                        TimeSpan timezoneOffset = dateTimeOffset.Offset;
                        dateTimeTakenTimeZone= timezoneOffset.ToString();
                    }
                }

                if (dateTimeTakenTimeZone != "")
                {
                    dateTimeTakenTimeZone = FormatTimezone(dateTimeTakenTimeZone);
                }

            if (dateTimeTaken != "")
            {
                dateTimeTaken = ConvertDateToNewFormat(dateTimeTaken);              
            }

            // Format file datetime to desired format
            fileCreatedDate = DateTime.ParseExact(fileCreatedDate, "yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd hh:mm:ss tt");
            fileModifiedDate = DateTime.ParseExact(fileModifiedDate, "yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd hh:mm:ss tt");

            // Get Device Make
            deviceMake = doc.RootElement.TryGetProperty("IFD0:Make", out JsonElement propertyDeviceMake) ? propertyDeviceMake.GetString() : "";
            deviceModel = doc.RootElement.TryGetProperty("IFD0:Model", out JsonElement propertyDeviceModel) ? propertyDeviceModel.GetString() : "";
            device = ImageDB.DeviceHelper.GetDevice(deviceMake, deviceModel);
            
            // Get Geocoordinates
            stringLatitude = doc.RootElement.TryGetProperty("GPS:GPSLatitude", out JsonElement propertyLatitude) && propertyLatitude.ValueKind == JsonValueKind.String? propertyLatitude.GetString(): "";
            stringLongitude = doc.RootElement.TryGetProperty("GPS:GPSLongitude", out JsonElement propertyLongitude) && propertyLongitude.ValueKind == JsonValueKind.String ? propertyLongitude.GetString(): "";
            stringAltitude = doc.RootElement.TryGetProperty("GPS:GPSAltitude", out JsonElement propertyAltitude) && propertyAltitude.ValueKind == JsonValueKind.String ? propertyAltitude.GetString(): "";

            try
            {
                latitude = string.IsNullOrWhiteSpace(stringLatitude) ? null : decimal.Parse(stringLatitude, CultureInfo.InvariantCulture);
                longitude = string.IsNullOrWhiteSpace(stringLongitude) ? null : decimal.Parse(stringLongitude, CultureInfo.InvariantCulture);
            }
            catch
            {
                latitude = null;
                longitude = null;
            }

            try
            {
                altitude = string.IsNullOrWhiteSpace(stringAltitude) ? null : decimal.Parse(stringAltitude, CultureInfo.InvariantCulture);
            }
            catch 
            {
                altitude = null;
            }

            // Get GPS Ref
            if (doc.RootElement.TryGetProperty("GPS:GPSLatitudeRef", out var propertyLatitudeRef) && !string.IsNullOrWhiteSpace(propertyLatitudeRef.GetString()))
            { latitudeRef = propertyLatitudeRef.GetString().Trim() ?? ""; }
            
            // Change latitude value to negative if latitudeRef is "S"
            if ((latitude != null)&&(latitude>0) && (latitudeRef.Trim().ToUpper()=="S"))
            {
                latitude = latitude * -1;
            }
                        
            if (doc.RootElement.TryGetProperty("GPS:GPSLongitudeRef", out var propertyLongitudeRef) && !string.IsNullOrWhiteSpace(propertyLongitudeRef.GetString()))
            { longitudeRef = propertyLongitudeRef.GetString().Trim() ?? ""; }
            
            // Change longitude value to negative if longitudeRef is "W"
            if ((longitude != null) && (longitude > 0) && (longitudeRef.Trim().ToUpper() == "W"))
            {
                longitude = longitude * -1;
            }

            // Get Location
            if (doc.RootElement.TryGetProperty("XMP-iptcCore:Location", out var propertyLocation) && !string.IsNullOrWhiteSpace(propertyLocation.GetString()))
            { location = propertyLocation.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IPTC:Sub-location", out var propertySubLocation) && !string.IsNullOrWhiteSpace(propertySubLocation.GetString()))
            { location = propertySubLocation.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:LocationCreatedSublocation", out var propertySubLocationCreated) && !string.IsNullOrWhiteSpace(propertySubLocationCreated.GetString()))
            { location = propertySubLocationCreated.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:LocationCreatedLocation", out var propertyLocationCreated) && !string.IsNullOrWhiteSpace(propertyLocationCreated.GetString()))
            { location = propertyLocationCreated.GetString().Trim() ?? ""; }

            // Get City
            if (doc.RootElement.TryGetProperty("XMP-photoshop:City", out var propertyPhotoshopCity) && !string.IsNullOrWhiteSpace(propertyPhotoshopCity.GetString()))
            { city = propertyPhotoshopCity.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IPTC:City", out var propertyCity) && !string.IsNullOrWhiteSpace(propertyCity.GetString()))
            { city = propertyCity.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:LocationCreatedCity", out var propertyLocationCreatedCity) && !string.IsNullOrWhiteSpace(propertyLocationCreatedCity.GetString()))
            { city = propertyLocationCreatedCity.GetString().Trim() ?? ""; }

            // Get State-Province
            if (doc.RootElement.TryGetProperty("XMP-photoshop:State", out var propertyPhotoshopState) && !string.IsNullOrWhiteSpace(propertyPhotoshopState.GetString()))
            { stateProvince = propertyPhotoshopState.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IPTC:Province-State", out var propertyStateProvince) && !string.IsNullOrWhiteSpace(propertyStateProvince.GetString()))
            { stateProvince = propertyStateProvince.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:LocationCreatedProvinceState", out var propertyLocationCreatedStateProvince) && !string.IsNullOrWhiteSpace(propertyLocationCreatedStateProvince.GetString()))
            { city = propertyLocationCreatedStateProvince.GetString().Trim() ?? ""; }

            // Get Country
            if (doc.RootElement.TryGetProperty("XMP-photoshop:Country", out var propertyPhotoshopCountry) && !string.IsNullOrWhiteSpace(propertyPhotoshopCountry.GetString()))
            { country = propertyPhotoshopCountry.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IPTC:Country-PrimaryLocationName", out var propertyCountry) && !string.IsNullOrWhiteSpace(propertyCountry.GetString()))
            { country = propertyCountry.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:LocationCreatedCountryName", out var propertyLocationCreatedCountry) && !string.IsNullOrWhiteSpace(propertyLocationCreatedCountry.GetString()))
            { country = propertyLocationCreatedCountry.GetString().Trim() ?? ""; }

            // Get Country Code
            if (doc.RootElement.TryGetProperty("XMP-iptcCore:CountryCode", out var propertyCountryCode) && !string.IsNullOrWhiteSpace(propertyCountryCode.GetString()))
            { countryCode = propertyCountryCode.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IPTC:Country-PrimaryLocationCode", out var propertyIPTCCountryCode) && !string.IsNullOrWhiteSpace(propertyIPTCCountryCode.GetString()))
            { countryCode = propertyIPTCCountryCode.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:LocationCreatedCountryCode", out var propertyLocationCreatedCountryCode) && !string.IsNullOrWhiteSpace(propertyLocationCreatedCountryCode.GetString()))
            { countryCode = propertyLocationCreatedCountryCode.GetString().Trim() ?? ""; }

            // Get Creator
            if (doc.RootElement.TryGetProperty("XMP-tiff:Artist", out var propertyTiffArtist) && !string.IsNullOrWhiteSpace(propertyTiffArtist.GetString()))
            { creator = propertyTiffArtist.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("XMP-dc:Creator", out var propertyCreator) && !string.IsNullOrWhiteSpace(propertyCreator.GetString()))
            { creator = propertyCreator.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IPTC:By-line", out var propertyIPTCByLine) && !string.IsNullOrWhiteSpace(propertyIPTCByLine.GetString()))
            { creator = propertyIPTCByLine.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IFD0:Artist", out var propertyEXIFArtist) && !string.IsNullOrWhiteSpace(propertyEXIFArtist.GetString()))
            { creator = propertyEXIFArtist.GetString().Trim() ?? ""; }

            // Get Copyright
            if (doc.RootElement.TryGetProperty("XMP-dc:Rights", out var propertyRights) && !string.IsNullOrWhiteSpace(propertyRights.GetString()))
            { copyright = propertyRights.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IPTC:CopyrightNotice", out var propertyCopyrightNotice) && !string.IsNullOrWhiteSpace(propertyCopyrightNotice.GetString()))
            { copyright = propertyCopyrightNotice.GetString().Trim() ?? ""; }
            if (doc.RootElement.TryGetProperty("IFD0:Copyright", out var propertyCopyright) && !string.IsNullOrWhiteSpace(propertyCopyright.GetString()))
            { copyright = propertyCopyright.GetString().Trim() ?? ""; }

            // Check if the RegionPersonDisplayName property exists
            if (doc.RootElement.TryGetProperty("XMP-MP:RegionPersonDisplayName", out JsonElement regionPersonDisplayName))
            {
                // If it's a string
                if (regionPersonDisplayName.ValueKind == JsonValueKind.String)
                {
                    peopleTag.Add(item: regionPersonDisplayName.GetString());
                }
                else
                {
                    // Iterate over the array
                    foreach (var name in regionPersonDisplayName.EnumerateArray())
                    {
                        peopleTag.Add(item: name.GetString());
                    }
                }
    }

            // Check if the RegionPersonDisplayName property exists
            if (doc.RootElement.TryGetProperty("XMP-mwg-rs:RegionName", out JsonElement regionName))
            {
                // If it's a string
                if (regionName.ValueKind == JsonValueKind.String)
                {
                    peopleTag.Add(item: regionName.GetString());
                }
                else
                {
                    // Iterate over the array
                    foreach (var name in regionName.EnumerateArray())
                    {
                        peopleTag.Add(item: name.GetString());
                    }
                }

            }

            // Check if the PersonInImage property exists
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:PersonInImage", out JsonElement personInImage))
            {
                // If it's a string
                if (personInImage.ValueKind == JsonValueKind.String)
                {
                    peopleTag.Add(item: personInImage.GetString());
                }
                else
                {
                    // Iterate over the array
                    foreach (var name in personInImage.EnumerateArray())
                    {
                        peopleTag.Add(item: name.GetString());
                    }
                }

            }

            // Check if the Keyword property exists
            if (doc.RootElement.TryGetProperty("IPTC:Keywords", out JsonElement keywords))
            {
                // If it's a string
                if (keywords.ValueKind == JsonValueKind.String)
                {
                    descriptiveTag.Add(item: keywords.GetString());
                }
                else
                {
                    // Iterate over the array
                    foreach (var tag in keywords.EnumerateArray())
                    {
                        descriptiveTag.Add(item: tag.GetString());
                    }
                }
            }

            // Check if the Keyword property exists
            if (doc.RootElement.TryGetProperty("XMP-dc:Subject", out JsonElement subject))
            {
                // If it's a string
                if (subject.ValueKind == JsonValueKind.String)
                {
                    descriptiveTag.Add(item: subject.GetString());
                }
                else
                {
                    // Iterate over the array
                    foreach (var tag in subject.EnumerateArray())
                    {
                        descriptiveTag.Add(item: tag.GetString());
                    }
                }
            }

            // Check if the Keyword property exists
            if (doc.RootElement.TryGetProperty("IFD0:XPKeywords", out JsonElement xpKeywords))
            {
                // If it's a string
                if (xpKeywords.ValueKind == JsonValueKind.String)
                {
                    descriptiveTag.Add(item: xpKeywords.GetString());
                }
                else
                {
                    // Iterate over the array
                    foreach (var tag in xpKeywords.EnumerateArray())
                    {
                        descriptiveTag.Add(item: tag.GetString());
                    }
                }
            }

            // Check if the Location Identifiers exists
            if (doc.RootElement.TryGetProperty("XMP-iptcExt:LocationCreatedLocationId", out JsonElement locationId))
            {
                    // If it's a string
                    if (locationId.ValueKind == JsonValueKind.String)
                    {
                        locationIdentifier.Add(item: locationId.GetString());
                    }
                    else
                    {
                        // Iterate over the array
                        foreach (var tag in locationId.EnumerateArray())
                        {
                            locationIdentifier.Add(item: tag.GetString());
                        }
                    }
            }


                foreach (var name in peopleTag)
                {
                    var service = new PeopleTagService(dbFiles);
                    await service.AddPeopleTags(name, imageID);
                }


                foreach (var tag in descriptiveTag)
                {
                    var service = new DescriptiveTagService(dbFiles);
                    await service.AddTags(tag, imageID);
                }

                foreach (var locationURI in locationIdentifier)
                {
                    var service = new LocationIdService(dbFiles);
                    await service.AddLocationId(locationURI,location, imageID);
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

                // Update the file path and other properties only when necessary. Not needed when perfoming a metadata reload.
                if (updatedSHA1 != "")
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

static string ConvertDateToNewFormat(string inputDate)
{
    // Define the input format
    string inputFormat = "yyyy:MM:dd HH:mm:ss";

    // Parse the input string to a DateTime object
    if (DateTime.TryParseExact(inputDate, inputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
    {
        // Convert to the desired format "yyyy-MM-dd h:mm:ss tt"
        return dateTime.ToString("yyyy-MM-dd h:mm:ss tt");
    }
    else
    {
        return "Invalid date format";
    }
}

static string GetAlbumName(string photoLibrary, string filePath)
{
    filePath = filePath.Replace(photoLibrary, "");
    filePath = filePath.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    filePath = filePath.Replace(Path.DirectorySeparatorChar.ToString(), " - ");
    filePath = filePath.Trim();
    return filePath;
}
static string FormatTimezone(string input)
{
    // Remove any extra spaces from the input
    input = input.Trim();

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

    // Handle if the input is not a valid number, return as it is
    return input;
}
static string GetNormalizedFolderPath(string folderPath)
{
    folderPath = folderPath.Trim();
    folderPath = folderPath.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    folderPath = folderPath.TrimEnd('\\');
    
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
    // Get the root drive (e.g., C:\)
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
    const int bufferSize = 8192; // 8KB buffer size, you can adjust as needed

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
            ImageId = image.ImageId,
            Filepath = image.Filepath,
            Metadata = image.Metadata,
            RecordAdded = image.RecordAdded,
            AddedBatchId = image.AddedBatchId,
            RecordModified = image.RecordModified,
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



