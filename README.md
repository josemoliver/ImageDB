# Image Metadata Analysis - Database Loader Tool
Command line app which will scan the specified folder (or all libraries if no folder is provided) for image files, process the metadata, and update the database accordingly. The app does not modify file metadata, simply reads the metadata leveraging the powerfull exiftool utility and loads it into a SQLite database. The database is meant for users familiar with SQL and photo metadata to analyse the file metadata of their photo collections.

## Installation

1. Ensure you have the following prerequisites installed:
   - .NET Core SDK 8
   - EXIFTool.exe (https://exiftool.org/) - Include the exiftool.exe folder in your system's PATH folder.

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

## Usage

1. Use a SQLite Database Editor such as SQLite DB Browser (https://sqlitebrowser.org/). 
2. Create SQLite Database by running the database\ImageDB.sqlite.sql file.
3. Update the appsettings.json file with the path of your SQLite database file. You can also include folder paths to ingore. For example, some database management tools may create temp folders for deleted files you may wish not to include in the database. 
4. Open database and add your photo collection folders to the PhotoLibrary table. For example, you may have a divided your photo collections into folders based on Years.
5. Ensure exiftool.exe is properly installed and included in your system's PATH environment variable.
6. Run ImageDB.exe


The Image Database Management Tool provides the following command-line options:

- `--folder`: Specify the path to a specific library to scan. Path must be included in the PhotoLibrary db table.
- `--mode`: Scan modes - normal (default) | date | quick | reload

Modes:

- normal (default) - Scans all files and compares any existing file using SHA1 hash. Slowest scanning mode but the most reliable.
- date - Scans all files and updates the files based on the file modified date property. Faster scanning but if any file was updated and the file modified date is not, the app will not update the metatada on the db.
- quick - Scans files based on file modified sorted by descending modified date. If unmodified file if found, the rest are skipped.
- reload - No scan is performed of the files, existing metadata scans are re-processed at the database level. This mode is useful for updating Image table after any code change.
  

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
| `MetadataHistory`          | Prior to updating a record on the Image table, the exiftool JSON Metdata is copied to this table for archiving purposes. Say you wish to figure out what metadata fields have been changed by your photo management softare or wish. |
| `PeopleTag`  |  People tags. |
| `PhotoLibrary`  |  Your photo collection main folders. All files and subfolder contained are scan by the tool. |
| `Tag`  |  Descriptive tags |
| `relation*`  | These tables maintain the ImageId relationships between tags, location identifiers, and people tags. |

## DB Views:
The views are meant to assist in your metadata inspection and analysis. For example, identifying field discrepancies, finding duplicate filenames, etc.
You will note that the exiftool JSON output can be queried using SQLite's Json Query Support - https://sqlite.org/json1.html. Feel free to edit existing ones or create your own.

## Metadata Table Fields:
Although all the metadata tags retrieved using Exiftool are recorded into `Metadata` field, some other table values in the database are derived based on those fields for easier presentation.

| Table.Column            | Source(s)                            |
| :------------     | :--------------------------------------------------------- | 
| `Image.Title`     | XMP-dc:Title,IPTC:ObjectName,IPTC:Headline,IFD0:XPTitle  |
| `Image.Description`     | XMP-dc:Description,IPTC:Caption-Abstract,IFD0:ImageDescription,ExifIFD:UserComment,XMP-tiff:ImageDescription,IFD0:XPComment |
| `Image.Album`     | PhotoLibrary subfolder  |
| `Image.Rating`     | IFD0:Rating,XMP-xmp:Rating  |
| `Image.DateTimeTaken`     | ExifIFD:DateTimeOriginal,ExifIFD:CreateDate,XMP-photoshop:DateCreated, File Created Date |
| `Image.TimeZone`     | ExifIFD:OffsetTimeOriginal, Date Time (if included) |
| `Image.Device`     | IFD0:Make and IFD0:Model |
| `Image.Latitude`     | GPS:GPSLatitude and GPS:GPSLatitudeRef |
| `Image.Longitude`     | GPS:GPSLongitude and GPS:GPSLongitudeRef|
| `Image.GPSAltitude`     | GPS:GPSAltitude |
| `Image.Location`     | XMP-iptcExt:LocationCreatedLocation, XMP-iptcExt:LocationCreatedSublocation, IPTC:Sub-location, XMP-iptcCore:Location  |
| `Image.City`     | XMP-iptcExt:LocationCreatedCity, IPTC:City, XMP-photoshop:City |
| `Image.StateProvince`     | XMP-iptcExt:LocationCreatedProvinceState, IPTC:Province-State, XMP-photoshop:State  |
| `Image.Country`     | XMP-iptcExt:LocationCreatedCountryName, IPTC:Country-PrimaryLocationName, XMP-photoshop:Country |
| `Image.CountryCode`     | XMP-iptcExt:LocationCreatedCountryCode, IPTC:Country-PrimaryLocationCode, XMP-iptcCore:CountryCode  |
| `Image.Creator`     | IFD0:Artist, IPTC:By-line, XMP-dc:Creator, XMP-tiff:Artist |
| `Image.Copyright`     | IFD0:Copyright, IPTC:CopyrightNotice, XMP-dc:Rights  |
| `PeopleTag.PersonName`     | XMP-MP:RegionPersonDisplayName, XMP-mwg-rs:RegionName, XMP-iptcExt:PersonInImage  |
| `Tag.TagName`     | IPTC:Keywords, XMP-dc:Subject, IFD0:XPKeywords  |
| `Location.LocationIdentifier`     | XMP-iptcExt:LocationCreatedLocationId |
| `Location.LocationName`     | *From first Image.Location found during scan, can be modified afterwards.  |


## Additional References:
- https://savemetadata.org/
- https://www.exiftool.org/TagNames/index.html
- https://www.carlseibert.com/blog/




