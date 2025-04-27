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

1. Use a SQLite Database Editor such as SQLite DB Browser. 
2. Create SQLite Database by running the database\ImageDB.sqlite.sql file.
3. Open database and add your photo collection folders to the PhotoLibrary table.


The Image Database Management Tool provides the following command-line options:

- `--folder`: Specify the path to a specific library to scan.
- `--reloadmeta`: Re-process already scanned metadata.

To run the tool, use the following command:

```
dotnet run -- [options]
```

For example, to scan a specific folder, included in the PhotoLibrary db table:

```
dotnet run -- --folder "C:\Users\YourUsername\Pictures"
```

To reload the metadata for an existing library:

```
dotnet run -- --reloadmeta true
```

The tool will scan the specified folder (or all libraries if no folder is provided) for image files, process the metadata, and update the database accordingly. The database is meant for users familiar with SQL and photo metadata to analyse the file metadata of their photo collections.
