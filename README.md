# Image Management - Database Loader Tool

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
2. Create SQLite Database by running the database\ImageDB.sqlite.sql file.
3. Open database and add your photo collection folders to the PhotoLibrary table. For example, you may have a divided your photo collections into folders based on Years.


The Image Database Management Tool provides the following command-line options:

- `--folder`: Specify the path to a specific library to scan. Path must be included in the PhotoLibrary db table.
- `--mode`: Scan modes - normal (default) | date | quick | reload

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


The tool will scan the specified folder (or all libraries if no folder is provided) for image files, process the metadata, and update the database accordingly. The database is meant for users familiar with SQL and photo metadata to analyse the file metadata of their photo collections.
