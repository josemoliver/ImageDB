# Image Metadata Analysis - Database Loader Tool
This command-line tool scans a specified folder (or all libraries if no folder is provided) for image files, and using the powerful ExifTool utility- extracts the metadata, and updates a SQLite database accordingly. It does not alter the file metadata. The tool and database is designed for users with knowledge of SQL and photo metadata, allowing them to analyze the metadata of their photo collections. More information can be obtain in the [ImageDB Project Wiki] (https://github.com/josemoliver/ImageDB/wiki).


## Installation

1. Ensure you have the following prerequisites installed:
   - .NET Core SDK 8
   - EXIFTool.exe installed (https://exiftool.org/) - Include the exiftool.exe folder in your system's PATH folder.

2. Clone the repository:
   ```
   git clone https://github.com/josemoliver/ImageDB.git
   ```

3. Navigate to the project directory:
   ```
   cd ImageDB
   ```

4. Restore the project dependencies:
   ```
   dotnet restore
   ```

5. Build the project included under "console" folder:
   ```
   dotnet build
   ```

## Setup and Usage

1. Use a SQLite Database Editor such as SQLite DB Browser (https://sqlitebrowser.org/). 
2. Create SQLite Database (Example name ImageDB.sqlite) by running the database\ImageDB.sqlite.sql file. This will create all the tables, views and indexes.
3. a. Update the appsettings.json file with the path of your SQLite database file. 
   b. You can also include folder paths to ingore. For example, some database management tools may create temp folders for deleted files you may wish not to include in the database. 
   c. Enable or disable "ImageThumbs" and "RegionThumbs". These option allow save of the image thumbnail and/or mwg region thumbnail to the database. Storing this information will increase database size and has a slight performance cost on the data read operation. 
4. Open database and add your photo collection folders to the PhotoLibrary table. For example, you may have a divided your photo collections into folders based on Years.
5. Ensure exiftool.exe is properly installed and included in your system's PATH environment variable.
6. Run ImageDB.exe refer to the options.


The Image Database Management Tool provides the following command-line options:

- `--mode`: (Required) Operation modes [ normal | date | quick | reload ]
- `--folder`: (Optional) Specify the path to a specific library to scan. Paths must be included in the PhotoLibrary db table. Leaving value empty will run through all library folders.


## Operation Modes:

| Options         | Description                                             |
| :------------     | :--------------------------------------------------------- | 
| `normal`     | Scans all files and compares any existing file using SHA1 hash. Slowest scanning mode but the most reliable.|
| `date`       | Scans all files and updates the files based on the file modified date property. Faster scanning but if any file was updated and the file modified date is not, the app will not update the metatada on the database. |
|  `quick` | Scans files based on file modified date, in order of most recently modified files. When a file is deemed unmodified by date, the rest are skipped. |
|  `reload` |No scan is performed of the file metadata. Metadata obtained from the previous scan is re-processed at the database level. This mode is useful for updating the database tables after any code modification. |


To run the tool, use the following command:

```
ImageDB.exe --mode [options] --folder [path]
```

For example, to perform a normal scan for a specific folder, included in the PhotoLibrary db table:

```
ImageDB.exe --folder "C:\Users\YourUsername\Pictures"
```

Metadata Analisis:
Using a SQLlite database management tool, open the database file so you can analyze your photo file's metadata.

## DB Tables:

| Table            | Description                                             |
| :------------     | :--------------------------------------------------------- | 
| `Batch`     | Log of ImageDB runs. Files scanned, processed count, and duration of scan runs. |
| `Image`    | Image metadata table. Includes tables with derived data from file metadata (For example, date and Device). The column Metadata contains the exiftool JSON output of the files with all the key/value pairs found by Exiftool. Detailed descriptions can be found in exiftool.org. |
| `Log`  |  Error/Warning logs |
| `Location`          | Location Identifiers found. If a photo metadata uses IPTC Location Identifiers. Refer to blog post: https://jmoliver.wordpress.com/2016/03/18/using-iptc-location-identifiers-to-link-your-photos-to-knowledge-bases/ |
| `Collection`          | MWG Collections |
| `MetadataHistory`          | Prior to updating a record on the Image table, the exiftool JSON Metdata is copied to this table for archiving purposes. Particularly useful to figure out what metadata fields have been changed by your photo management software. |
| `PeopleTag`  |  People tags. |
| `PhotoLibrary`  |  Your photo collection main folders. All files and subfolder contained are scan by the tool. |
| `Tag`  |  Descriptive tags |
| `Region`  |  Metadata Working Group (MWG) Regions |
| `relation*`  | These tables maintain the ImageId relationships between tags, location identifiers, and people tags. |


## Metadata Table Fields:
Although all the metadata tags retrieved using Exiftool are loaded into the `Image.Metadata` field in JSON format and accessible for queries and views, some other table values in the database are derived based on those fields for easier presentation. Unless specified, if various fields correspond to the same property the first non-empty/null value is used. For example, in the case of `Image.Title` the order of precedence is "XMP-dc:Title,IPTC:ObjectName, or IFD0:XPTitle". Other values are derived from merging similar values such as PeopleTag.PersonName or derived from a combination of fields as in the case of Image.Device.

| Table.Column            | Source(s)                            |
| :------------     | :--------------------------------------------------------- | 
| `Image.Title`     | XMP-dc:Title,IPTC:ObjectName, or IFD0:XPTitle  |
| `Image.Description`     | XMP-dc:Description, IPTC:Caption-Abstract, IFD0:ImageDescription, ExifIFD:UserComment, XMP-tiff:ImageDescription,IFD0:XPComment,IFD0:XPSubject,IFD0:XPComment, IPTC:Headline or XMP-acdsee:Caption |
| `Image.Album`     | Derived from the PhotoLibrary subfolder(s). Nested subfolder names are concatenated with a dash (-).  |
| `Image.Rating`     | XMP-xmp:Rating, IFD0:Rating, IFD0:RatingPercent, or XMP-microsoft:RatingPercent |
| `Image.DateTimeTaken`     | XMP-photoshop:DateCreated, ExifIFD:DateTimeOriginal, ExifIFD:CreateDate, XMP-exif:DateTimeOriginal, IPTC:DateCreated+IPTC:TimeCreated, or use oldest value from the File Create or Modified Date. |
| `Image.TimeZone`     | ExifIFD:OffsetTimeOriginal, otherwise obtained from XMP-photoshop:DateCreated, XMP-exif:DateTimeOriginal, or IPTC:TimeCreated   |
| `Image.Device`     | Combined from IFD0:Make and IFD0:Model |
| `Image.Latitude`     | Composite:GPSLatitude (Rounded to 6 decimal places) |
| `Image.Longitude`     | Composite:GPSLongitude (Rounded to 6 decimal places) |
| `Image.Altitude`     | Composite:GPSAltitude |
| `Image.Location`     | XMP-iptcExt:LocationCreatedLocationName, XMP-iptcExt:LocationCreatedSublocation, XMP-iptcCore:Location, or IPTC:Sub-location  |
| `Image.City`     | XMP-iptcExt:LocationCreatedCity, XMP-photoshop:City, or IPTC:City |
| `Image.StateProvince`     | XMP-iptcExt:LocationCreatedProvinceState, XMP-photoshop:State, or IPTC:Province-State  |
| `Image.Country`     | XMP-iptcExt:LocationCreatedCountryName, XMP-photoshop:Country or IPTC:Country-PrimaryLocationName |
| `Image.CountryCode`     | XMP-iptcExt:LocationCreatedCountryCode, XMP-iptcCore:CountryCode or IPTC:Country-PrimaryLocationCode  |
| `Image.Creator`     | XMP-dc:Creator, IPTC:By-line, IFD0:Artist, XMP-tiff:Artist, or IFD0:XPAuthor |
| `Image.Copyright`     | XMP-dc:Rights, IPTC:CopyrightNotice, or IFD0:Copyright  |
| `Image.DateTimeTakenSource`     | Source from were DateTimeTaken value was obtained.  |
| `Image.Thumbnail`     | Image thumbnail WebP blob.  |
| `PeopleTag.PersonName`     | Merged names from XMP-MP:RegionPersonDisplayName, XMP-mwg-rs:RegionName, and XMP-iptcExt:PersonInImage  |
| `Tag.TagName`     | Merged values from IPTC:Keywords, XMP-dc:Subject, and IFD0:XPKeywords  |
| `Location.LocationIdentifier`     | XMP-iptcExt:LocationCreatedLocationId |
| `Location.LocationName`     | *From first Image.Location found during scan, can be modified afterwards.  |
| `Collection.CollectionName`     | Collection Name  |
| `Collection.CollectionURI`     | Collection URI  |
| `Region.*`     | MWG Region Info https://www.exiftool.org/TagNames/MWG.html#RegionInfo also include region thumbnail WebP blob |

## DB Views:
The views are meant to assist in your metadata inspection and analysis. For example, identifying field discrepancies, finding duplicate filenames, etc. You will note that the exiftool JSON output can be queried using SQLite's Json Query Support - https://sqlite.org/json1.html. Feel free to edit existing ones or create your own.


## Additional References:
- https://savemetadata.org/
- https://www.exiftool.org/TagNames/index.html
- https://www.carlseibert.com/blog/
- https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf


## Example usage:
- Duplicate file detection, either by filename or hash value.
- Identify files with missing metadata, such as dates, captions, geotags, etc.
- WindowsXP Legacy Image Fields - Prior to better image photo metadata standards, Microsoft introduced as as part of the Windows XP operating system (released in 2001) to store descriptive metadata about images (and some other file types) in a way that supported Unicode text. The WindowsXP XPTitle, XPSubject, XPComment, XPAuthor and XPKeywords are stored in the EXIF tag space, but are not part of the official EXIF standard. They were intended to enable users to tag and search images using Windows Explorer, and are still supported by some applications. Use the `vLegacyWindowsXP` view to identify files containing data in these fields. This view is particulary useful if migrating your photo collection from apps such as Windows Photo Gallery - Refer to blog post: https://jmoliver.wordpress.com/2017/02/12/accessing-windows-photo-gallery-metadata-using-exiftool/
- IPTC IIM – Developed in the 1990s, the IPTC IIM metadata standard was widely used in journalism and media. Although it has largely been replaced by newer formats like XMP, it remains supported by many tools for backward compatibility. To view IPTC IIM metadata in your images, use the `vLegacy_IPTC_IMM` view. 
- MWG Region mistmatch - Cropping or resizing images can lead to inconsistencies in MWG Region metadata. Refer to page 51 of the MWG Guidance document for details. The `vRegionMismatch` view checks whether the AppliedToDimensions values in MWG Regions match the actual image dimensions and flags any discrepancies.
- Reports for miscellaneous photo metadata such as Weather Tags (vWeatherTags) - https://jmoliver.wordpress.com/2018/07/07/capturing-the-moment-and-the-ambient-weather-information-in-photos/ 

For additional usage scenarios read: [Using ImageDB to identify common photo metadata issues](https://github.com/josemoliver/ImageDB/wiki/Using-ImageDB-to-identify-common-photo-metadata-issues)
