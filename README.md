# Image Metadata Analysis - Database Loader Tool
Command line app which will scan the specified folder (or all libraries if no folder is provided) for image files, process the metadata, and update the database accordingly. The app DOES NOT modify file metadata, simply READS the metadata leveraging the powerfull exiftool utility and LOADS it into a SQLite database. The database is meant for users familiar with SQL and photo metadata to analyse the file metadata of their photo collections.

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
   cd image-database-tool
   ```

4. Restore the project dependencies:
   ```
   dotnet restore
   ```

5. Build the project:
   ```
   dotnet build
   ```

## Usage

1. Use a SQLite Database Editor such as SQLite DB Browser (https://sqlitebrowser.org/). 
2. Create SQLite Database by running the database\ImageDB.sqlite.sql file. Update the appsettings.json file with the path of your SQLite database file. You can also include folder paths to ingore. For example, some database management tools may create temp folders for deleted files you may wish not to include in the database. 
3. Open database and add your photo collection folders to the PhotoLibrary table. For example, you may have a divided your photo collections into folders based on Years.


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

DB Tables:
1. Batch - Keeps a log of ImageDB runs. Files scanned, processed count, and duration of scan runs.
2. Image - Image metadata table. Includes tables with derived data from file metadata (For example, date and Device). The column Metadata contains the exiftool JSON output of the files with all the key/value pairs found by Exiftool. Detailed descriptions can be found in exiftool.org.
3. Location - Location Identifiers found. If a photo metadata uses IPTC Location Identifiers. Refer to blog post: https://jmoliver.wordpress.com/2016/03/18/using-iptc-location-identifiers-to-link-your-photos-to-knowledge-bases/
4. Log - Error log
5. MetadataHistory - Prior to updating a record on the Image table, the exiftool JSON Metdata is copied to this table for archiving purposes. Say you wish to figure out what metadata fields have been changed by your photo management softare or wish.
6. PeopleTag - People names found during scanning are stored here.
7. PhotoLibrary - Your photo collection main folders. All files and subfolder contained are scan by the tool.
8. Tag - Descriptive tags found duing scanning
9. relation* - These tables maintain the ImageId relationships between tags, location identifiers, and people tags.

DB Views:
The views are meant to assist in your metadata inspection and analysis. You will note that the exiftool JSON output can be queried using SQLite's Json Query Support - https://sqlite.org/json1.html. Feel free to edit existing ones or create your own.

Additional References:
- https://savemetadata.org/
- https://www.exiftool.org/TagNames/index.html
- https://www.carlseibert.com/blog/




