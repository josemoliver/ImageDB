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

var rootCommand = new RootCommand
{
    new Option<string>(
        "--folder",
        description: "Path to specific library to scan."
    ),
    new Option<string>(
        "--reloadmeta",
        description: "Re-process already scanned metatada. Ignore any file updates."
    ),
        new Option<string>(
        "--scanmode",
        description: "Set scan modes: normal (default) | date | quick"
        )
};

using var db = new CDatabaseImageDBsqliteContext();

var photoLibrary = db.PhotoLibraries.ToList();
string photoFolderFilter = string.Empty;
bool reloadMetadata = false;
bool quickScan = false;
bool dateScan = false;

// Handler to process the command-line arguments
rootCommand.Handler = CommandHandler.Create((string folder, string reloadmeta, string scanmode) =>
{
    photoFolderFilter = folder;
    
    if ((reloadmeta != null) && (reloadmeta.ToLower() != "false"))
    {
        reloadMetadata = true;
        Console.WriteLine("[START] - Reloading existing metadata, no new and update from files.");
    }
    else
    {
        reloadMetadata = false;
        Console.WriteLine("[START] - Scanning for new and updated files.");
    }

    if ((scanmode == null) || (scanmode.ToLower() == "normal"))
    {
        quickScan = false;
        Console.WriteLine("[START] - Integrity scan for new and updated files.");
    }
    else if (scanmode.ToLower() == "date")
    {
        dateScan = true;
        quickScan = false;
        Console.WriteLine("[START] - Scan for changes using file modified date.");        
    }
    else if (scanmode.ToLower() == "quick")
    {
        dateScan = true;
        quickScan = true;
        Console.WriteLine("[START] - Quick scan for changes using file modified date.");
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
    photoFolderFilter = photoFolderFilter.Trim('\\');
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
            UpdateImageRecord(imageId, "");
            Console.WriteLine("[UPDATE] - Reloading metadata for Image Id: " + imageId);
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

        // Define the file extensions to look for


        // Get all supported files in the directory and subdirectories
        DirectoryInfo info = new DirectoryInfo(photoFolder);
        string[] fileExtensions = { ".jpg", ".jpeg", ".jxl", ".heic" };

        FileInfo[] files = info.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(p => fileExtensions.Contains(p.Extension.ToLower()))
            .OrderByDescending(p => p.LastWriteTime)
            .ToArray();

        // Iterate over each file and add them to imageFiles
        foreach (FileInfo file in files)
        {
            imageFiles.Add(new ImageFile(file.FullName.ToString(), file.LastWriteTime.ToString("yyyy-MM-dd hh:mm:ss tt"), file.Extension.ToString().ToLower(), file.Name.ToString(),file.Length.ToString(),file.CreationTime.ToString("yyyy-MM-dd hh:mm:ss tt")));         
        }

        // Start Batch entry get batch id
        var newBatch = new Batch
        {
            StartDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            PhotoLibraryId = photoLibraryId,
            FilesFound = imageFiles.Count
        };

        // Add the new batch entry to the database
        dbFiles.Add(newBatch);
        dbFiles.SaveChanges();
        int batchID = newBatch.BatchId;
        bool suspendScan = false;
        Console.WriteLine("[BATCH] - Started batch Id: " + batchID+" Files Found: "+imageFiles.Count);

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
                        UpdateImage(imageId, SHA1);
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
                        UpdateImage(imageId, SHA1);
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
                        UpdateImage(imageId, SHA1);
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

        foreach (var imageFile in imageFiles)
        {
            compareImageFiles.Add(imageFile.FilePath);
        }
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
            Console.WriteLine("[RESULTS] - Files Found: "+imageFiles.Count+" Added: " + filesAdded + " Updated: "+ filesUpdated+" Skipped: " + filesSkipped + " Removed: " + filesDeleted + " Error: " + filesError);

            // Get elapsed time in seconds
            int elapsedTime = 0;
            try { elapsedTime = (int)(DateTime.Parse(jobbatch.EndDateTime) - DateTime.Parse(jobbatch.StartDateTime)).TotalSeconds; } catch { elapsedTime = 0; }

            string elapsedTimeComment = "";
            if (elapsedTime >= 3600) // Greater than or equal to 1 hour
            {
                int hours           = elapsedTime / 3600;
                int minutes         = (elapsedTime % 3600) / 60;
                elapsedTimeComment  = $"{hours} hour(s) and {minutes} minute(s)";
                Console.WriteLine($"Elapsed Time: "+ elapsedTimeComment);
            }
            else if (elapsedTime >= 60) // Greater than or equal to 1 minute
            {
                int minutes         = elapsedTime / 60;
                int seconds         = elapsedTime % 60;
                elapsedTimeComment  = $"{minutes} minute(s) and {seconds} second(s)";
                Console.WriteLine($"Elapsed Time: " +elapsedTimeComment);
            }
            else // Less than 1 minute
            {
                elapsedTimeComment  = $"{elapsedTime} second(s)";
                Console.WriteLine($"Elapsed Time: " + elapsedTimeComment);
            }

            if (jobbatch != null)
            {
                jobbatch.EndDateTime    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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

async void UpdateImage(int imageId, string updatedSHA1)
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
                LogEntry(-1, specificFilePath, "No metadata found for the file");
                return;
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

    UpdateImageRecord(imageId, updatedSHA1);
}

async void AddImage(int photoLibraryID, string photoFolder, int batchId, string specificFilePath, string fileName, string fileExtension, string fileSize, string SHA1)
{
    //string jsonMetadata = GetExiftoolMetadata(specificFilePath);
    string jsonMetadata = ExifToolHelper.GetExiftoolMetadata(specificFilePath);

    if (jsonMetadata == "")
    {
        // Handle the case where jsonMetadata is empty
        Console.WriteLine("[ERROR] No metadata found for the file: " + specificFilePath);
        return;
    }

    int imageId = 0;
    string recordAdded = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

    // Dictionary to map file extensions to normalized values
    var extensionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "jpg", "jpeg" },
        { "jpeg", "jpeg" },
        { "jxl", "jpeg-xl" },
        { "heic", "heic" }
    };

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
            PhotoLibraryId  = photoLibraryID,
            BatchId         = batchId,           
            Filepath        = specificFilePath,
            Album           = GetAlbumName(photoFolder,specificFilePath.Replace(fileName,"")),

            Filename        = fileName,
            Format          = fileExtension,
            Filesize        = fileSize.ToString(),
           
            Metadata        = jsonMetadata,
            RecordAdded     = recordAdded
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

    UpdateImageRecord(imageId,SHA1);

}

async void UpdateImageRecord(int imageID, string updatedSHA1)
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
                if (doc.RootElement.TryGetProperty("Composite:DateTimeCreated", out var propertyDateTimeCreated) && !string.IsNullOrWhiteSpace(propertyDateTimeCreated.GetString()))
                { dateTimeTaken = propertyDateTimeCreated.GetString() ?? ""; xmpDateTime = propertyDateTimeCreated.GetString() ?? ""; }
                if (doc.RootElement.TryGetProperty("XMP-photoshop:DateCreated", out var propertyPhotoshopDate) && !string.IsNullOrWhiteSpace(propertyPhotoshopDate.GetString()))
                { dateTimeTaken = propertyPhotoshopDate.GetString() ?? ""; }
                if (doc.RootElement.TryGetProperty("ExifIFD:CreateDate", out var propertyCreateDate) && !string.IsNullOrWhiteSpace(propertyCreateDate.GetString()))
                { dateTimeTaken = propertyCreateDate.GetString() ?? ""; }
                if (doc.RootElement.TryGetProperty("ExifIFD:DateTimeOriginal", out var propertyDateTimeOriginal) && !string.IsNullOrWhiteSpace(propertyDateTimeOriginal.GetString()))
                { dateTimeTaken = propertyDateTimeOriginal.GetString() ?? "";  }

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
            device = DeviceHelper.GetDevice(deviceMake, deviceModel);
            
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
                    image.Sha1 = updatedSHA1;                 
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

static string NormalizePathCase(string path)
{
    // Get the root drive (e.g., C:\)
    string root = Path.GetPathRoot(path);

    // Get all directories and the file name if any
    string[] directories = path.Substring(root.Length).Split(Path.DirectorySeparatorChar);

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

public static class DeviceHelper
{
    private static readonly Dictionary<string, string> CameraMakerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "CANON", "Canon" },
        { "NIKON", "Nikon" },
        { "SONY", "Sony" },
        { "FUJIFILM", "Fujifilm" },
        { "PANASONIC", "Panasonic" },
        { "OLYMPUS", "Olympus" },
        { "OM SYSTEM", "OM System" },
        { "LEICA", "Leica" },
        { "PENTAX", "Pentax" },
        { "RICOH", "Ricoh" },
        { "KODAK", "Kodak" },
        { "CASIO", "Casio" },
        { "SAMSUNG", "Samsung" },
        { "SIGMA", "Sigma" },
        { "HASSELBLAD", "Hasselblad" },
        { "GOPRO", "GoPro" },
        { "DJI", "DJI" },
        { "PHASE ONE", "Phase One" },
        { "APPLE", "Apple" },
        { "GOOGLE", "Google" },
        { "HUAWEI", "Huawei" },
        { "XIAOMI", "Xiaomi" },
        { "ONEPLUS", "OnePlus" },
        { "BLACKMAGIC DESIGN", "Blackmagic Design" },
        { "RED DIGITAL CINEMA", "RED Digital Cinema" },
        { "SHARP", "Sharp" },
        { "VIVITAR", "Vivitar" },
        { "YASHICA", "Yashica" },
        { "BELL & HOWELL", "Bell & Howell" },
        { "TAMRON", "Tamron" },
        { "TOKINA", "Tokina" },
        { "HOLGA", "Holga" },
        { "POLAROID", "Polaroid" },
        { "LOMOGRAPHY", "Lomography" },
        { "MEIKE", "Meike" },
        { "SJCAM", "SJCAM" },
        { "AKASO", "Akaso" },
        { "INSTA360", "Insta360" },
        { "Z CAM", "Z CAM" },
        { "IKONOSKOP", "Ikonoskop" },
        { "ARRI", "ARRI" },
        { "KINEFINITY", "Kinefinity" },
        { "ZEISS", "Zeiss" },
        { "ROLLEI", "Rollei" },
        { "THINKWARE", "Thinkware" },
        { "NEXTBASE", "Nextbase" },
        { "GARMIN", "Garmin" },
        { "PAPAGO", "Papago" },
        { "VIOFO", "Viofo" },
        { "MOTO", "Motorola" }
    };

    private static readonly Dictionary<string, string> SpecialFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "HEWLETT-PACKARD", "HP" },
        { "KONICA MINOLTA", "Konica Minolta" },
        { "Konica Minolta Camera, Inc.", "Konica Minolta" },
        { "LG ELECTRONICS", "LG" },
        { "Minolta Co., Ltd.", "Minolta" },
        { "NIKON CORPORATION", "Nikon" },
        { "CASIO COMPUTER CO.,LTD", "Casio" },
        { "EASTMAN KODAK COMPANY", "Kodak" },
        { "OLYMPUS CORPORATION", "Olympus" },
        { "OLYMPUS IMAGING CORP.", "Olympus" },
        { "OLYMPUS OPTICAL CO.,LTD", "Olympus" },
        { "SAMSUNG TECHWIN CO., LTD.", "Samsung" },
        { "SAMSUNG ELECTRONICS", "Samsung" },
        { "SAMSUNG TECHWIN", "Samsung" },
        { "SONY CORPORATION", "Sony" },
        { "SONY INTERACTIVE ENTERTAINMENT", "Sony" },
        { "SONY MOBILE COMMUNICATIONS INC.", "Sony" },
        { "SONY MOBILE COMMUNICATIONS", "Sony" },
        { "SONY ERICSSON MOBILE COMMUNICATIONS AB", "Sony Ericsson" },
        { "PENTAX CORPORATION", "Pentax" }
    };

    public static string GetDevice(string deviceMake, string deviceModel)
    {
        deviceMake = deviceMake.Trim();
        deviceModel = deviceModel.Trim();

        // If device model is empty, return the device make or an empty string
        if (string.IsNullOrWhiteSpace(deviceModel))
            return deviceMake ?? "";

        // If device make is empty, return the device model
        deviceMake ??= "";

        // Normalize the device make to uppercase for comparison
        string upperMake = deviceMake.ToUpperInvariant();


        if (deviceMake == upperMake && CameraMakerMap.TryGetValue(upperMake, out var normalized))
        {
            deviceMake = normalized;
        }

        if (!string.IsNullOrEmpty(deviceMake) &&
            deviceModel.ToUpperInvariant().Contains(deviceMake.ToUpperInvariant()))
        {
            deviceMake = "";
        }

        foreach (var fix in SpecialFixes)
        {
            if (upperMake.Contains(fix.Key.ToUpperInvariant()) || deviceMake == fix.Key)
            {
                deviceMake = fix.Value;
                break;
            }
        }

        return string.IsNullOrEmpty(deviceMake)
            ? deviceModel
            : $"{deviceMake} {deviceModel.Replace(deviceMake + " ", "", StringComparison.OrdinalIgnoreCase)}".Trim();
    }
}


public static class ExifToolHelper
{
    private static Process? exiftoolProcess;
    private static StreamWriter? exiftoolInput;
    private static StreamReader? exiftoolOutput;
    private static readonly object exiftoolLock = new();
    private const string ReadyMarker = "{ready}";

    static ExifToolHelper()
    {
        StartExifTool();
    }

    private static void StartExifTool()
    {
        exiftoolProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "exiftool.exe",
                Arguments = "-stay_open True -@ -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };

        exiftoolProcess.StartInfo.EnvironmentVariables["LANG"] = "en_US.UTF-8";
        exiftoolProcess.Start();
        exiftoolInput = exiftoolProcess.StandardInput;
        exiftoolOutput = exiftoolProcess.StandardOutput;

        Console.WriteLine("[EXIFTOOL] - Exiftool Process Started");
    }

    public static string GetExiftoolMetadata(string filepath)
    {

        lock (exiftoolLock)
        {
            try
            {
                if (exiftoolProcess == null || exiftoolProcess.HasExited)
                    StartExifTool();

                var cmd = new StringBuilder();
                
                // Add command options
                cmd.AppendLine($"-json");   // JSON output
                cmd.AppendLine($"-G1");     // Group output by tag
                cmd.AppendLine($"-n");      // Numeric output     
                cmd.AppendLine(filepath);   // File path
                cmd.AppendLine("-execute"); // Execute the command

                exiftoolInput!.Write(cmd.ToString());
                exiftoolInput.Flush();

                var outputBuilder = new StringBuilder();
                string? line;

                while ((line = exiftoolOutput!.ReadLine()) != null)
                {                    
                    if (line == ReadyMarker)
                        break;

                    outputBuilder.AppendLine(line);
                }

                string result = outputBuilder.ToString().Trim();



                // This is a workaround for Exiftool which adds the array brackets as well as sometimes changes the datatype of the values. All values will be returning as text.
                result = result.Trim('[', ']');
                result = JsonConverter.ConvertNumericAndBooleanValuesToString(result);

                return result; // clean up array brackets
            }
            catch (Exception ex)
            {
                Console.WriteLine("[EXCEPTION] - " + ex.Message);
                return string.Empty;
            }
        }
    } 
}

public class ImageFile
{
    public string FilePath { get; set; }
    public string FileModifiedDate { get; set; }
    public string FileExtension { get; set; }
    public string FileName { get; set; }
    public string FileSize { get; set; }
    public string FileCreatedDate { get; set; }

    public ImageFile(string filePath, string fileModifiedDate, string fileExtension, string fileName, string fileSize, string fileCreatedDate)
    {
        FilePath = filePath;
        FileModifiedDate = fileModifiedDate;
        FileExtension = fileExtension;
        FileName = fileName;
        FileSize = fileSize;
        FileCreatedDate = fileCreatedDate;
    }
}


public class PeopleTagService
{
    private readonly CDatabaseImageDBsqliteContext dbFiles;

    public PeopleTagService(CDatabaseImageDBsqliteContext context)
    {
        dbFiles = context;
    }

    public async Task AddPeopleTags(string personName, int imageId)
    {
        // Check if the person already exists in the PeopleTag table
        var existingTag = dbFiles.PeopleTags
            .FirstOrDefault(tag => tag.PersonName == personName);

        int peopleTagId;

        if (existingTag == null)
        {
            // Person does not exist, add new entry to PeopleTag
            var newTag = new PeopleTag
            {
                PersonName = personName
            };

            dbFiles.PeopleTags.Add(newTag);
            await dbFiles.SaveChangesAsync();

            peopleTagId = newTag.PeopleTagId; // Get the newly created PeopleTagId
        }
        else
        {
            // Person exists, use the existing PeopleTagId
            peopleTagId = existingTag.PeopleTagId;
        }

        // Add an entry to relationPeopleTag with the PeopleTagId and ImageId
        var relationEntry = new RelationPeopleTag
        {
            PeopleTagId = peopleTagId,
            ImageId = imageId
        };

        dbFiles.RelationPeopleTags.Add(relationEntry);
        await dbFiles.SaveChangesAsync();
    }
}

public class LocationIdService
{
    private readonly CDatabaseImageDBsqliteContext dbFiles;

    public LocationIdService(CDatabaseImageDBsqliteContext context)
    {
        dbFiles = context;
    }

    public async Task AddLocationId(string locationIdentifier, string locationName, int imageId)
    {
        // Check if the person already exists in the PeopleTag table
        var existingTag = dbFiles.Locations
            .FirstOrDefault(tag => tag.LocationIdentifier == locationIdentifier);

        int locationId;

        if (existingTag == null)
        {
            // Person does not exist, add new entry to PeopleTag
            var newTag = new Location
            {
                LocationIdentifier = locationIdentifier,
                LocationName = locationName
            };

            dbFiles.Locations.Add(newTag);
            await dbFiles.SaveChangesAsync();

            locationId = newTag.LocationId; // Get the newly created PeopleTagId
        }
        else
        {
            // Person exists, use the existing PeopleTagId
            locationId = existingTag.LocationId;
        }

        // Add an entry to relationPeopleTag with the PeopleTagId and ImageId
        var relationEntry = new RelationLocation
        {
            LocationId = locationId,
            ImageId = imageId
        };


        dbFiles.RelationLocations.Add(relationEntry);
        await dbFiles.SaveChangesAsync();
    }
}

public class DescriptiveTagService
{
    private readonly CDatabaseImageDBsqliteContext dbFiles;

    public DescriptiveTagService(CDatabaseImageDBsqliteContext context)
    {
        dbFiles = context;
    }

    public async Task AddTags(string tagName, int imageId)
    {
        // Check if the person already exists in the Tag table
        var existingTag = dbFiles.Tags
            .FirstOrDefault(tag => tag.TagName == tagName);

        int tagId;

        if (existingTag == null)
        {
            // Tag does not exist, add new entry to Tag
            var newTag = new Tag
            {
                TagName = tagName
            };

            dbFiles.Tags.Add(newTag);
            await dbFiles.SaveChangesAsync();

            tagId = newTag.TagId; // Get the newly created Tag
        }
        else
        {
            // Person exists, use the existing TagId
            tagId = existingTag.TagId;
        }

        // Add an entry to relationTag with the TagId and ImageId
        var relationEntry = new RelationTag
        {
            TagId = tagId,
            ImageId = imageId
        };

        dbFiles.RelationTags.Add(relationEntry);
        await dbFiles.SaveChangesAsync();
    }
}

public class JsonConverter
{
    public static string ConvertNumericAndBooleanValuesToString(string json)
    {
        // Parse the JSON document
        JsonDocument doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        // Recursively convert all numeric and boolean values to strings
        JsonElement transformedElement = TransformJsonElement(rootElement);

        // Serialize the modified JSON to a human-readable string with UTF-8 characters
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Allow UTF-8 characters
        };

        return JsonSerializer.Serialize(transformedElement, options);
    }

    private static JsonElement TransformJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var objectDict = new System.Collections.Generic.Dictionary<string, JsonElement>();
                foreach (var property in element.EnumerateObject())
                {
                    objectDict[property.Name] = TransformJsonElement(property.Value);
                }
                return JsonDocument.Parse(JsonSerializer.Serialize(objectDict)).RootElement;
            case JsonValueKind.Array:
                var arrayList = new System.Collections.Generic.List<JsonElement>();
                foreach (var item in element.EnumerateArray())
                {
                    arrayList.Add(TransformJsonElement(item));
                }
                return JsonDocument.Parse(JsonSerializer.Serialize(arrayList)).RootElement;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return JsonDocument.Parse($"\"{element.ToString()}\"").RootElement; // Convert numeric and boolean to string
            default:
                return element; // For other types (null, string, etc.), return as is
        }
    }
}