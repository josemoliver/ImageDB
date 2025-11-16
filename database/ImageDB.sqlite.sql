BEGIN TRANSACTION;
CREATE TABLE IF NOT EXISTS "Batch" (
	"BatchID"	INTEGER,
	"StartDateTime"	TEXT,
	"EndDateTime"	TEXT,
	"PhotoLibraryId"	INTEGER,
	"FilesFound"	INTEGER,
	"FilesAdded"	INTEGER,
	"FilesUpdated"	INTEGER,
	"FilesSkipped"	INTEGER,
	"FilesRemoved"	INTEGER,
	"FilesReadError"	INTEGER,
	"ElapsedTime"	INTEGER,
	"Comment"	TEXT,
	PRIMARY KEY("BatchID" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Collection" (
	"CollectionId"	INTEGER,
	"ImageId"	INTEGER NOT NULL,
	"CollectionName"	TEXT,
	"CollectionURI"	TEXT,
	PRIMARY KEY("CollectionId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Image" (
	"ImageId"	INTEGER,
	"PhotoLibraryId"	INTEGER,
	"Filepath"	TEXT UNIQUE,
	"Album"	TEXT,
	"SHA1"	TEXT,
	"Format"	TEXT,
	"Filename"	TEXT,
	"Filesize"	TEXT,
	"FileCreatedDate"	TEXT,
	"FileModifiedDate"	TEXT,
	"Title"	TEXT,
	"Description"	TEXT,
	"Rating"	TEXT,
	"DateTimeTaken"	TEXT,
	"DateTimeTakenTimeZone"	TEXT,
	"Device"	TEXT,
	"Latitude"	NUMERIC,
	"Longitude"	NUMERIC,
	"Altitude"	NUMERIC,
	"Location"	TEXT,
	"City"	TEXT,
	"StateProvince"	TEXT,
	"Country"	TEXT,
	"CountryCode"	TEXT,
	"Creator"	TEXT,
	"Copyright"	TEXT,
	"Metadata"	TEXT,
	"StuctMetadata"	TEXT,
	"RecordAdded"	TEXT,
	"AddedBatchId"	INTEGER,
	"RecordModified"	TEXT,
	"ModifiedBatchId"	INTEGER,
	PRIMARY KEY("ImageId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Location" (
	"LocationId"	INTEGER,
	"LocationIdentifier"	TEXT,
	"LocationName"	TEXT,
	"Latitude"	TEXT,
	"Longitude"	TEXT,
	PRIMARY KEY("LocationId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Log" (
	"LogEntryId"	INTEGER,
	"Datetime"	TEXT,
	"BatchID"	INTEGER,
	"Filepath"	TEXT,
	"LogEntry"	TEXT,
	PRIMARY KEY("LogEntryId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "MetadataHistory" (
	"HistoryId"	INTEGER,
	"ImageId"	INTEGER,
	"Filepath"	TEXT,
	"AddedBatchId"	INTEGER,
	"RecordAdded"	TEXT,
	"ModifiedBatchId"	INTEGER,
	"RecordModified"	TEXT,
	"Metadata"	TEXT,
	"StuctMetadata"	TEXT,
	PRIMARY KEY("HistoryId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "PeopleTag" (
	"PeopleTagId"	INTEGER,
	"PersonName"	TEXT UNIQUE,
	"FSId"	TEXT,
	PRIMARY KEY("PeopleTagId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "PhotoLibrary" (
	"PhotoLibraryId"	INTEGER,
	"Folder"	TEXT NOT NULL,
	PRIMARY KEY("PhotoLibraryId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Region" (
	"RegionId"	INTEGER,
	"ImageId"	INTEGER NOT NULL,
	"RegionName"	TEXT,
	"RegionType"	TEXT,
	"RegionAreaUnit"	TEXT,
	"RegionAreaH"	NUMERIC,
	"RegionAreaW"	NUMERIC,
	"RegionAreaX"	NUMERIC,
	"RegionAreaY"	NUMERIC,
	"RegionAreaD"	NUMERIC,
	PRIMARY KEY("RegionId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Tag" (
	"TagId"	INTEGER,
	"TagName"	TEXT UNIQUE,
	"Source"	INTEGER,
	PRIMARY KEY("TagId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "relationLocation" (
	"LocationRelationId"	INTEGER,
	"ImageId"	INTEGER,
	"LocationId"	INTEGER,
	PRIMARY KEY("LocationRelationId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "relationPeopleTag" (
	"PeopleRelationId"	INTEGER,
	"ImageId"	INTEGER,
	"PeopleTagId"	INTEGER,
	PRIMARY KEY("PeopleRelationId" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "relationTag" (
	"RelationTagId"	INTEGER,
	"ImageId"	INTEGER,
	"TagId"	INTEGER,
	PRIMARY KEY("RelationTagId" AUTOINCREMENT)
);
CREATE VIEW vAlbums AS
WITH Converted AS (
  SELECT
    Album,
    datetime(
      substr(DateTimeTaken, 1, 10) || ' ' ||
      printf('%02d', 
        CASE 
          WHEN substr(DateTimeTaken, 12, 2) = '12' AND substr(DateTimeTaken, 21, 2) = 'AM' THEN 0
          WHEN substr(DateTimeTaken, 21, 2) = 'PM' AND substr(DateTimeTaken, 12, 2) != '12' THEN CAST(substr(DateTimeTaken, 12, 2) AS INTEGER) + 12
          ELSE CAST(substr(DateTimeTaken, 12, 2) AS INTEGER)
        END
      ) || substr(DateTimeTaken, 14, 6)
    ) AS ConvertedDateTime
  FROM Image
)
SELECT
  Album,
  MIN(ConvertedDateTime) AS MinDateTimeTaken,
  MAX(ConvertedDateTime) AS MaxDateTimeTaken,
  -- Days difference: Max - Min
  CAST(
    (julianday(MAX(ConvertedDateTime)) - julianday(MIN(ConvertedDateTime)))
    AS INTEGER
  ) AS Days
FROM Converted
GROUP BY Album;
CREATE VIEW vCollections AS
SELECT CollectionName, CollectionURI, Count (CollectionId) AS GroupingCount FROM Collection GROUP BY CollectionName, CollectionURI ORDER BY CollectionName, CollectionURI;
CREATE VIEW vCreator AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:Artist') AS Artist,
json_extract(Metadata, '$.IPTC:By-line') AS ByLine, 
json_extract(Metadata, '$.XMP-dc:Creator') AS Creator, 
json_extract(Metadata, '$.XMP-tiff:Artist') AS TiffArtist
FROM Image;
CREATE VIEW vDates AS
SELECT Filepath,DateTimeTaken, DateTimeTakenTimeZone,json_extract(Metadata, '$.ExifIFD:DateTimeOriginal') AS Exif_DateTimeOriginal,json_extract(Metadata, '$.ExifIFD:CreateDate') AS Exif_CreateDate, json_extract(Metadata, '$.IPTC:DateCreated') AS IPTC_DateCreated,json_extract(Metadata, '$.IPTC:TimeCreated') AS IPTC_TimeCreated, json_extract(Metadata, '$.XMP-exif:DateTimeOriginal') AS XMPexif_DateTimeOriginal, json_extract(Metadata, '$.XMP-photoshop:DateCreated') AS XMPphotoshop_DateCreated, Metadata FROM Image;
CREATE VIEW vDescriptions AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.XMP-dc:Description') AS Description,
json_extract(Metadata, '$.IPTC:Caption-Abstract') AS CaptionAbstract, 
json_extract(Metadata, '$.IFD0:ImageDescription') AS ImageDescription, 
json_extract(Metadata, '$.ExifIFD:UserComment') AS UserComment, 
json_extract(Metadata, '$.XMP-tiff:ImageDescription') AS TiffImageDescription,
json_extract(Metadata, '$.IFD0:XPComment') AS XPComment,
json_extract(Metadata, '$.IPTC:Headline') AS Headline
FROM Image;
CREATE VIEW vDevices AS
SELECT ImageId,Filepath,Device, 
json_extract(Metadata, '$.IFD0:Make') AS Make,
json_extract(Metadata, '$.IFD0:Model') AS Model 
FROM Image;
CREATE VIEW vDevicesCount AS
SELECT 
    COALESCE(NULLIF(Device, ''), '(unknown)') AS Device,
    COUNT(Device) AS DeviceCount,
    ROUND(
        100.0 * COUNT(Device) / (SELECT COUNT(*) FROM vDevices),
        2
    ) AS DevicePercent
FROM vDevices
GROUP BY COALESCE(NULLIF(Device, ''), '(unknown)')
ORDER BY DeviceCount DESC;
CREATE VIEW vDuplicateFilenames AS
SELECT LOWER(Filename) AS Filename, COUNT(*) 
FROM Image
GROUP BY LOWER(Filename)
HAVING COUNT(*) > 1;
CREATE VIEW vExifTimeZone AS
SELECT ImageId,Filepath, DateTimeTakenTimeZone, 
json_extract(Metadata, '$.ExifIFD:OffsetTimeOriginal') AS ExifTimeZone,
Metadata
FROM Image
ORDER BY ExifTimeZone ASC;
CREATE VIEW vFileDimensions AS
SELECT ImageId,Filepath,Filename,
json_extract(Metadata, '$.File:ImageWidth') AS FileWidth,
json_extract(Metadata, '$.File:ImageHeight') AS FileHeight,
json_extract(Metadata, '$.XMP-mwg-rs:RegionAppliedToDimensionsW') AS RWidth, 
json_extract(Metadata, '$.XMP-mwg-rs:RegionAppliedToDimensionsH') AS RHeight,
Metadata
FROM Image;
CREATE VIEW vGeneralMetadata AS
SELECT 
    Image.Filepath,
    Image.Title,
    Image.Description,
	json_extract(Metadata, '$.ExifIFD:DateTimeOriginal') AS Exif_DateTimeOriginal,
    Image.DateTimeTakenTimeZone,
	Image.Creator,
	Image.Copyright,
	vTagsPerImage.Tags AS Tags,
	vPeopleTagsPerImage.PeopleTags AS PeopleTags,
    Image.Location,
    Image.City,
    Image.StateProvince,
    Image.Country,
    CASE 
        WHEN Image.Latitude IS NULL AND Image.Longitude IS NULL THEN ''
        ELSE COALESCE(Image.Latitude, '') || ',' || COALESCE(Image.Longitude, '')
    END AS GeoCoordinates
    
FROM 
    Image
LEFT JOIN 
    vTagsPerImage ON Image.ImageId = vTagsPerImage.ImageId
LEFT JOIN 
    vPeopleTagsPerImage ON Image.ImageId = vPeopleTagsPerImage.ImageId
GROUP BY 
    Image.ImageId;
CREATE VIEW vGeotags AS
SELECT Location,City,StateProvince,Country,CountryCode, AVG(Latitude) AS Latitude, AVG(Longitude) AS Longitude, Count(ImageId) AS FileCount
FROM Image
GROUP BY Location,StateProvince,Country,City;
CREATE VIEW vIPTCDigest AS
SELECT Filepath, json_extract(Metadata, '$.XMP-photoshop:LegacyIPTCDigest') AS LegacyIPTCDigest,json_extract(Metadata, '$.File:CurrentIPTCDigest') AS CurrentIPTCDigest, Metadata FROM Image WHERE LegacyIPTCDigest IS NOT NULL;
CREATE VIEW vImageCameraSettings AS
SELECT ImageId,Filepath,Filename,
json_extract(Metadata, '$.Composite:Aperture') AS Aperture,
json_extract(Metadata, '$.Composite:ShutterSpeed') AS ShutterSpeed,
json_extract(Metadata, '$.ExifIFD:ExposureTime') AS ExposureTime, 
json_extract(Metadata, '$.ExifIFD:FNumber') AS FNumber,
json_extract(Metadata, '$.ExifIFD:ExposureProgram') AS ExposureProgram,
json_extract(Metadata, '$.ExifIFD:ISO') AS ISO,
json_extract(Metadata, '$.Composite:FocalLength35efl') AS FocalLength35efl,
json_extract(Metadata, '$.Composite:HyperfocalDistance') AS HyperfocalDistance,
json_extract(Metadata, '$.Composite:LensID') AS LensID
FROM Image;
CREATE VIEW vLegacyWindowsXP AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:XPTitle') AS XPTitle,
json_extract(Metadata, '$.IFD0:XPSubject') AS XPSubject,
json_extract(Metadata, '$.IFD0:XPComment') AS XPComment,
json_extract(Metadata, '$.IFD0:XPAuthor') AS XPAuthor,
json_extract(Metadata, '$.IFD0:XPKeywords') AS XPKeywords,
Metadata
FROM Image;
CREATE VIEW vLegacy_IPTC_IMM AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IPTC:ObjectName') AS ObjectName,
json_extract(Metadata, '$.IPTC:Headline') AS Headline,
json_extract(Metadata, '$.IPTC:Credit') AS Credit,
json_extract(Metadata, '$.IPTC:Caption-Abstract') AS CaptionAbstract, 
json_extract(Metadata, '$.IPTC:By-Line') AS Byline,
json_extract(Metadata, '$.IPTC:By-lineTitle') AS BylineTitle,
json_extract(Metadata, '$.IPTC:CopyrightNotice') AS CopyrightNotice,
json_extract(Metadata, '$.IPTC:Contact') AS Contact,
json_extract(Metadata, '$.IPTC:DateCreated') AS DateCreated, 
json_extract(Metadata, '$.IPTC:TimeCreated') AS TimeCreated, 
json_extract(Metadata, '$.IPTC:Sub-location') AS SubLocation,
json_extract(Metadata, '$.IPTC:Province-State') AS ProvinceState,
json_extract(Metadata, '$.IPTC:City') AS City,
json_extract(Metadata, '$.IPTC:Country') AS Country,
json_extract(Metadata, '$.IPTC:Country-PrimaryLocationCode') AS CountryPrimaryLocationCode,
json_extract(Metadata, '$.IPTC:Keywords') AS Keywords,
json_extract(Metadata, '$.IPTC:SpecialInstructions') AS SpecialInstructions,
json_extract(Metadata, '$.IPTC:Category') AS Category,
Metadata
FROM Image;
CREATE VIEW vMetadataKeys AS
WITH json_keys AS (
    SELECT 
        json_each.key AS json_key
    FROM 
        Image,
        json_each(Metadata)
    WHERE 
        Metadata IS NOT NULL
)
SELECT 
    json_key, 
    COUNT(*) AS key_count
FROM 
    json_keys
GROUP BY 
    json_key
ORDER BY 
    key_count DESC;
CREATE VIEW vMetadataModificationComparison AS
WITH PrevMetadata AS (
    SELECT
        mh.ImageId,
        mh.Metadata AS PrevMetadata,
        mh.HistoryId
    FROM
        MetadataHistory mh
    WHERE
        mh.HistoryId = (
            SELECT MAX(HistoryId)
            FROM MetadataHistory
            WHERE ImageId = mh.ImageId
            AND HistoryId < (
                SELECT MAX(HistoryId)
                FROM MetadataHistory
                WHERE ImageId = mh.ImageId
            )
        )
)
SELECT
    i.ImageId,i.Filepath,
    i.Metadata AS CurrentMetadata,
    pm.PrevMetadata
FROM
    Image i
LEFT JOIN
    PrevMetadata pm
    ON i.ImageId = pm.ImageId
WHERE
    i.RecordModified >= datetime('now', '-7 days')  -- Filter for the last week
    AND i.RecordModified IS NOT NULL
ORDER BY
    i.RecordModified DESC;
CREATE VIEW vMissingGeotags AS
SELECT Filepath, Latitude,Longitude Location,StateProvince,Country,City
FROM Image
WHERE length(Location)=0 AND length(StateProvince)=0 AND length(Country)=0 AND length(City)=0;
CREATE VIEW vMonthlyPhotosTaken AS
SELECT
    strftime('%Y', datetime(
        substr(DateTimeTaken, 1, 10) || ' ' ||
        printf('%02d',
            CASE 
                WHEN substr(DateTimeTaken, 12, 2) = '12' AND substr(DateTimeTaken, 21, 2) = 'AM' THEN 0
                WHEN substr(DateTimeTaken, 21, 2) = 'PM' AND substr(DateTimeTaken, 12, 2) != '12' THEN CAST(substr(DateTimeTaken, 12, 2) AS INTEGER) + 12
                ELSE CAST(substr(DateTimeTaken, 12, 2) AS INTEGER)
            END
        ) || substr(DateTimeTaken, 14, 6)
    )) AS Year,
    strftime('%m', datetime(
        substr(DateTimeTaken, 1, 10) || ' ' ||
        printf('%02d',
            CASE 
                WHEN substr(DateTimeTaken, 12, 2) = '12' AND substr(DateTimeTaken, 21, 2) = 'AM' THEN 0
                WHEN substr(DateTimeTaken, 21, 2) = 'PM' AND substr(DateTimeTaken, 12, 2) != '12' THEN CAST(substr(DateTimeTaken, 12, 2) AS INTEGER) + 12
                ELSE CAST(substr(DateTimeTaken, 12, 2) AS INTEGER)
            END
        ) || substr(DateTimeTaken, 14, 6)
    )) AS Month,
    COUNT(*) AS ImageCount
FROM Image
GROUP BY Year, Month
ORDER BY Year, Month;
CREATE VIEW vPeopleTagCount AS
SELECT 
    PeopleTag.PeopleTagID,
    PeopleTag.PersonName,
    IFNULL(COUNT(relationPeopleTag.PeopleTagId), 0) AS PeopleTagCount,
    MIN(Image.DateTimeTaken) AS MinDate,
    MAX(Image.DateTimeTaken) AS MaxDate,
	MIN(json_extract(Metadata, '$.ExifIFD:DateTimeOriginal')) AS MinExifDateTimeOriginal,
	MAX(json_extract(Metadata, '$.ExifIFD:DateTimeOriginal')) AS MaxExifDateTimeOriginal
FROM 
    PeopleTag
LEFT JOIN 
    relationPeopleTag ON relationPeopleTag.PeopleTagId = PeopleTag.PeopleTagId
LEFT JOIN 
    Image ON relationPeopleTag.ImageId = Image.ImageId
GROUP BY 
    PeopleTag.PeopleTagID,
    PeopleTag.PersonName
ORDER BY 
    PeopleTagCount DESC;
CREATE VIEW vPeopleTagRegionCountDiff AS
SELECT i.ImageId,i.Filepath,i.Filename, i.StuctMetadata, peopleTagCount, regionCount
FROM Image i
LEFT JOIN (
    SELECT ImageId, COUNT(*) AS peopleTagCount
    FROM relationPeopleTag
    GROUP BY ImageId
) rpt ON i.ImageId = rpt.ImageId
LEFT JOIN (
    SELECT ImageId, COUNT(*) AS regionCount
    FROM Region
    GROUP BY ImageId
) r ON i.ImageId = r.ImageId
WHERE IFNULL(rpt.peopleTagCount, 0) != IFNULL(r.regionCount, 0)
ORDER BY i.ImageId;
CREATE VIEW vPeopleTagsPerImage AS
SELECT 
	Image.ImageId,
    Image.Filepath,
    COALESCE(GROUP_CONCAT(PeopleTag.PersonName, ';'), '') AS PeopleTags
FROM 
    Image
LEFT JOIN 
    relationPeopleTag ON Image.ImageId = relationPeopleTag.ImageId
LEFT JOIN 
    PeopleTag ON relationPeopleTag.PeopleTagId = PeopleTag.PeopleTagId
GROUP BY 
    Image.ImageId;
CREATE VIEW vPhotoDates AS
SELECT Filepath,DateTimeTaken, DateTimeTakenTimeZone,json_extract(Metadata, '$.ExifIFD:DateTimeOriginal') AS Exif_DateTimeOriginal,json_extract(Metadata, '$.ExifIFD:CreateDate') AS Exif_CreateDate, json_extract(Metadata, '$.IPTC:DateCreated') AS IPTC_DateCreated,json_extract(Metadata, '$.IPTC:TimeCreated') AS IPTC_TimeCreated, json_extract(Metadata, '$.XMP-exif:DateTimeOriginal') AS XMPexif_DateTimeOriginal, json_extract(Metadata, '$.XMP-photoshop:DateCreated') AS XMPphotoshop_DateCreated, Metadata FROM Image;
CREATE VIEW vPhotoLibraries AS
SELECT 
    p.PhotoLibraryId,
	p.Folder,
    COUNT(i.ImageId) AS ImageCount,
	COUNT(DISTINCT i.Album) AS AlbumCount,
    IFNULL(SUM(i.Filesize), 0) AS TotalFilesize,
    COUNT(DISTINCT i.Device) AS UniqueDeviceCount,
	COUNT(DISTINCT i.Creator) AS DistinctCreatorCount,
    IFNULL(COUNT(rpt.PeopleTagId), 0) AS PeopleTagCount,
	IFNULL(COUNT(rt.TagId), 0) AS DescriptiveTagCount
FROM 
    PhotoLibrary p
LEFT JOIN 
    Image i 
    ON p.PhotoLibraryId = i.PhotoLibraryId
LEFT JOIN 
    relationPeopleTag rpt 
    ON i.ImageId = rpt.ImageId
LEFT JOIN 
    relationTag rt 
    ON i.ImageId = rt.ImageId
GROUP BY 
    p.PhotoLibraryId
ORDER BY 
    p.PhotoLibraryId;
CREATE VIEW vRatingCounts AS
select Rating, Count(ImageId) As ImageCount from Image
GROUP BY Rating;
CREATE VIEW vRecentlyModified AS
SELECT Filepath,FileModifiedDate, Metadata
FROM Image
ORDER BY FileModifiedDate DESC;
CREATE VIEW vRegionMismatch AS
SELECT *
FROM vFileDimensions
WHERE (FileWidth<>RWidth OR FileHeight<>RHeight) AND (FileWidth<>RHeight OR FileHeight<>RWidth);
CREATE VIEW vRights AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:Copyright') AS Copyright,
json_extract(Metadata, '$.IPTC:CopyrightNotice') AS CopyrightNotice, 
json_extract(Metadata, '$.XMP-dc:Rights') AS Rights,
json_extract(Metadata, '$.XMP-tiff:Copyright') AS TiffCopyright
FROM Image;
CREATE VIEW vSaveMetadataDotOrg AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.XMP-dc:Title') AS Title,
json_extract(Metadata, '$.XMP-dc:Description') AS Description,
json_extract(Metadata, '$.XMP-photoshop:DateCreated') AS Date,
json_extract(Metadata, '$.XMP-iptcExt:PersonInImage') AS PersonInImage,
json_extract(Metadata, '$.XMP-iptcExt:LocationShownLocation') AS Location,
json_extract(Metadata, '$.XMP-iptcExt:LocationShownCity') AS City,
json_extract(Metadata, '$.XMP-iptcExt:LocationProvinceState') AS ProvinceState,
json_extract(Metadata, '$.XMP-iptcExt:LocationShownCountryName') AS CountryName,
json_extract(Metadata, '$.XMP-iptcExt:LocationShownLocationId') AS LocationId,
Metadata
FROM Image;
CREATE VIEW vTagCount
AS 
SELECT Tag.TagID,Tag.TagName, IFNULL(COUNT(relationTag.TagId),0) AS 'TagCount' 
FROM Tag 
LEFT JOIN relationTag on relationTag.TagId = Tag.TagId
GROUP BY Tag.TagName
ORDER BY TagCount DESC;
CREATE VIEW vTagsPerImage AS
SELECT 
	Image.ImageId,
    Image.Filepath,
    COALESCE(GROUP_CONCAT(Tag.TagName, ';'), '') AS Tags
FROM 
    Image
LEFT JOIN 
    relationTag ON Image.ImageId = relationTag.ImageId
LEFT JOIN 
    Tag ON relationTag.TagId = Tag.TagId
GROUP BY 
    Image.ImageId;
CREATE VIEW vTagsSansPeople AS
SELECT TagName
FROM Tag
WHERE TagName NOT IN (
    SELECT PersonName FROM PeopleTag
);
CREATE VIEW vTitles AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.XMP-dc:Title') AS Title,
json_extract(Metadata, '$.IPTC:ObjectName') AS ObjectName, 
json_extract(Metadata, '$.IFD0:XPTitle') AS XPTitle
FROM Image;
CREATE VIEW vWeatherTags AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.ExifIFD:AmbientTemperature') AS AmbientTemperature,
json_extract(Metadata, '$.ExifIFD:Humidity') AS Humidity,
json_extract(Metadata, '$.ExifIFD:Pressure') AS Pressure,
Metadata
FROM Image;
CREATE INDEX IF NOT EXISTS "idx_image_filepath" ON "Image" (
	"Filepath"
);
CREATE INDEX IF NOT EXISTS "idx_image_record_modified" ON "Image" (
	"RecordModified"
);
CREATE INDEX IF NOT EXISTS "idx_metadata_history_image_id" ON "MetadataHistory" (
	"ImageId"
);
COMMIT;
