BEGIN TRANSACTION;
DROP TABLE IF EXISTS "Batch";
CREATE TABLE "Batch" (
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
DROP TABLE IF EXISTS "Collection";
CREATE TABLE "Collection" (
	"CollectionId"	INTEGER,
	"ImageId"	INTEGER NOT NULL,
	"CollectionName"	TEXT,
	"CollectionURI"	TEXT,
	PRIMARY KEY("CollectionId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "Image";
CREATE TABLE "Image" (
	"ImageId"	INTEGER,
	"PhotoLibraryId"	INTEGER,
	"Filepath"	TEXT UNIQUE,
	"Album"	TEXT,
	"SHA1"	TEXT,
	"Format"	TEXT,
	"Filename"	TEXT,
	"Filesize"	INTEGER,
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
	"DateTimeTakenSource"	TEXT,
	PRIMARY KEY("ImageId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "Location";
CREATE TABLE "Location" (
	"LocationId"	INTEGER,
	"ImageId"	INTEGER,
	"LocationName"	TEXT,
	"LocationURI"	TEXT,
	"LocationType"	TEXT,
	PRIMARY KEY("LocationId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "Log";
CREATE TABLE "Log" (
	"LogEntryId"	INTEGER,
	"Datetime"	TEXT,
	"BatchID"	INTEGER,
	"Filepath"	TEXT,
	"LogEntry"	TEXT,
	PRIMARY KEY("LogEntryId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "MetadataHistory";
CREATE TABLE "MetadataHistory" (
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
DROP TABLE IF EXISTS "PeopleTag";
CREATE TABLE "PeopleTag" (
	"PeopleTagId"	INTEGER,
	"PersonName"	TEXT UNIQUE,
	"FSId"	TEXT,
	PRIMARY KEY("PeopleTagId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "Person";
CREATE TABLE "Person" (
	"PersonId"	INTEGER,
	"ImageId"	INTEGER,
	"PersonName"	TEXT,
	"PersonIdentifier"	TEXT,
	PRIMARY KEY("PersonId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "PhotoLibrary";
CREATE TABLE "PhotoLibrary" (
	"PhotoLibraryId"	INTEGER,
	"Folder"	TEXT NOT NULL,
	PRIMARY KEY("PhotoLibraryId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "Region";
CREATE TABLE "Region" (
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
DROP TABLE IF EXISTS "Tag";
CREATE TABLE "Tag" (
	"TagId"	INTEGER,
	"TagName"	TEXT UNIQUE,
	"Source"	INTEGER,
	PRIMARY KEY("TagId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "relationPeopleTag";
CREATE TABLE "relationPeopleTag" (
	"PeopleRelationId"	INTEGER,
	"ImageId"	INTEGER,
	"PeopleTagId"	INTEGER,
	PRIMARY KEY("PeopleRelationId" AUTOINCREMENT)
);
DROP TABLE IF EXISTS "relationTag";
CREATE TABLE "relationTag" (
	"RelationTagId"	INTEGER,
	"ImageId"	INTEGER,
	"TagId"	INTEGER,
	PRIMARY KEY("RelationTagId" AUTOINCREMENT)
);
DROP VIEW IF EXISTS "vAlbums";
CREATE VIEW vAlbums AS
WITH Converted AS (
  SELECT
    PhotoLibraryId,
    Album,
    Filesize,
    DateTimeTaken
  FROM Image
)
SELECT
  PhotoLibraryId,
  Album,
  MIN(DateTimeTaken) AS MinDateTimeTaken,
  MAX(DateTimeTaken) AS MaxDateTimeTaken,
  CAST(
    (julianday(MAX(DateTimeTaken)) - julianday(MIN(DateTimeTaken)))
    AS INTEGER
  ) AS Days,
  SUM(Filesize) AS TotalFilesize,
  COUNT(*) AS ImageCount
FROM Converted
GROUP BY PhotoLibraryId, Album;
DROP VIEW IF EXISTS "vCollections";
CREATE VIEW vCollections AS
SELECT CollectionName, CollectionURI, Count (CollectionId) AS GroupingCount FROM Collection GROUP BY CollectionName, CollectionURI ORDER BY CollectionName, CollectionURI;
DROP VIEW IF EXISTS "vCreator";
CREATE VIEW vCreator AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:Artist') AS Artist,
json_extract(Metadata, '$.IPTC:By-line') AS ByLine, 
json_extract(Metadata, '$.XMP-dc:Creator') AS Creator, 
json_extract(Metadata, '$.XMP-tiff:Artist') AS TiffArtist
FROM Image;
DROP VIEW IF EXISTS "vCreatorCount";
CREATE VIEW vCreatorCount AS
WITH Converted AS (
    SELECT
        COALESCE(NULLIF(Creator, ''), '(unknown)') AS Creator,
        Filesize,
		DateTimeTaken  
    FROM Image
),
Totals AS (
    SELECT COUNT(*) AS TotalImages FROM Converted
)
SELECT
    Creator,
    COUNT(*) AS ImageCount,
    ROUND(COUNT(*) * 100.0 / (SELECT TotalImages FROM Totals), 2) AS ImagePercentage,
    SUM(Filesize) AS Size,
    MIN(DateTimeTaken) AS MinDateTimeTaken,
    MAX(DateTimeTaken) AS MaxDateTimeTaken
FROM Converted
GROUP BY Creator
ORDER BY ImageCount DESC;
DROP VIEW IF EXISTS "vDateTimeTakenSourceCount";
CREATE VIEW vDateTimeTakenSourceCount AS
SELECT DateTimeTakenSource,
       COUNT(*) AS ImageCount
FROM Image
GROUP BY DateTimeTakenSource

UNION ALL

SELECT 'TotalImageCount' AS DateTimeTakenSource,
       COUNT(*) AS ImageCount
FROM Image;
DROP VIEW IF EXISTS "vDates";
CREATE VIEW vDates AS
SELECT Filepath,DateTimeTaken, DateTimeTakenTimeZone,json_extract(Metadata, '$.ExifIFD:DateTimeOriginal') AS Exif_DateTimeOriginal,json_extract(Metadata, '$.ExifIFD:CreateDate') AS Exif_CreateDate, json_extract(Metadata, '$.IPTC:DateCreated') AS IPTC_DateCreated,json_extract(Metadata, '$.IPTC:TimeCreated') AS IPTC_TimeCreated, json_extract(Metadata, '$.XMP-exif:DateTimeOriginal') AS XMPexif_DateTimeOriginal, json_extract(Metadata, '$.XMP-photoshop:DateCreated') AS XMPphotoshop_DateCreated, Metadata FROM Image;
DROP VIEW IF EXISTS "vDescriptions";
CREATE VIEW vDescriptions AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.XMP-dc:Description') AS Description,
json_extract(Metadata, '$.IPTC:Caption-Abstract') AS CaptionAbstract, 
json_extract(Metadata, '$.IFD0:ImageDescription') AS ImageDescription, 
json_extract(Metadata, '$.ExifIFD:UserComment') AS UserComment, 
json_extract(Metadata, '$.XMP-tiff:ImageDescription') AS TiffImageDescription,
json_extract(Metadata, '$.IFD0:XPComment') AS XPComment,
json_extract(Metadata, '$.IPTC:Headline') AS Headline,
json_extract(Metadata, '$.XMP-acdsee:Caption') AS ACDSeeCaption
FROM Image;
DROP VIEW IF EXISTS "vDescriptionsCount";
CREATE VIEW vDescriptionsCount AS
SELECT
    COUNT(*) AS TotalImages,
    COUNT(CASE WHEN Description IS NOT NULL AND TRIM(Description) <> '' THEN 1 END) AS ImagesWithDescription,
    COUNT(CASE WHEN Description IS NULL OR TRIM(Description) = '' THEN 1 END) AS ImagesWithoutDescription,
    ROUND(
        100.0 * COUNT(CASE WHEN Description IS NOT NULL AND TRIM(Description) <> '' THEN 1 END) 
        / COUNT(*),
        2
    ) AS PercentageWithDescription
FROM Image;
DROP VIEW IF EXISTS "vDevices";
CREATE VIEW vDevices AS
SELECT ImageId,Filepath,Device, 
json_extract(Metadata, '$.IFD0:Make') AS Make,
json_extract(Metadata, '$.IFD0:Model') AS Model 
FROM Image;
DROP VIEW IF EXISTS "vDevicesCount";
CREATE VIEW vDevicesCount AS
WITH Converted AS (
    SELECT
        COALESCE(NULLIF(Device, ''), '(unknown)') AS Device,
        Filesize,
	    DateTimeTaken
    FROM Image
),
Totals AS (
    SELECT COUNT(*) AS TotalImages FROM Converted
)
SELECT
    Device,
    COUNT(*) AS ImageCount,
    ROUND(COUNT(*) * 100.0 / (SELECT TotalImages FROM Totals), 2) AS ImagePercentage,
    SUM(Filesize) AS Size,
    MIN(DateTimeTaken) AS MinDateTimeTaken,
    MAX(DateTimeTaken) AS MaxDateTimeTaken
FROM Converted
GROUP BY Device
ORDER BY ImageCount DESC;
DROP VIEW IF EXISTS "vDuplicateFilenames";
CREATE VIEW vDuplicateFilenames AS
SELECT LOWER(Filename) AS Filename, COUNT(*) 
FROM Image
GROUP BY LOWER(Filename)
HAVING COUNT(*) > 1;
DROP VIEW IF EXISTS "vExifTimeZone";
CREATE VIEW vExifTimeZone AS
SELECT ImageId,Filepath, DateTimeTakenTimeZone, 
json_extract(Metadata, '$.ExifIFD:OffsetTimeOriginal') AS ExifTimeZone,
Metadata
FROM Image
ORDER BY ExifTimeZone ASC;
DROP VIEW IF EXISTS "vFileDimensions";
CREATE VIEW vFileDimensions AS
SELECT ImageId,Filepath,Filename,
json_extract(Metadata, '$.File:ImageWidth') AS FileWidth,
json_extract(Metadata, '$.File:ImageHeight') AS FileHeight,
json_extract(Metadata, '$.XMP-mwg-rs:RegionAppliedToDimensionsW') AS RWidth, 
json_extract(Metadata, '$.XMP-mwg-rs:RegionAppliedToDimensionsH') AS RHeight,
Metadata
FROM Image;
DROP VIEW IF EXISTS "vGeneralMetadata";
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
DROP VIEW IF EXISTS "vGeotags";
CREATE VIEW vGeotags AS
SELECT Location,City,StateProvince,Country,CountryCode, AVG(Latitude) AS Latitude, AVG(Longitude) AS Longitude, Count(ImageId) AS FileCount
FROM Image
GROUP BY Location,StateProvince,Country,City;
DROP VIEW IF EXISTS "vIPTCDigest";
CREATE VIEW vIPTCDigest AS
SELECT Filepath, json_extract(Metadata, '$.XMP-photoshop:LegacyIPTCDigest') AS LegacyIPTCDigest,json_extract(Metadata, '$.File:CurrentIPTCDigest') AS CurrentIPTCDigest, Metadata FROM Image WHERE LegacyIPTCDigest IS NOT NULL;
DROP VIEW IF EXISTS "vIPTCRightsContacts";
CREATE VIEW vIPTCRightsContacts AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IPTC:By-Line') AS IPTC_ByLine,
json_extract(Metadata, '$.IPTC:By-lineTitle') AS IPTC_ByLineTitle,
json_extract(Metadata, '$.IPTC:CopyrightNotice') AS IPTC_CopyrightNotice,
json_extract(Metadata, '$.XMP-dc:Creator') AS Creator,
json_extract(Metadata, '$.XMP-dc:Rights') AS Rights,
json_extract(Metadata, '$.XMP-iptcCore:CreatorAddress') AS CreatorAddress,
json_extract(Metadata, '$.XMP-iptcCore:CreatorCity') AS CreatorCity,
json_extract(Metadata, '$.XMP-iptcCore:CreatorPostalCode') AS CreatorPostalCode,
json_extract(Metadata, '$.XMP-iptcCore:CreatorRegion') AS CreatorRegion,
json_extract(Metadata, '$.XMP-iptcCore:CreatorCountry') AS CreatorCountry,
json_extract(Metadata, '$.XMP-iptcCore:CreatorWorkEmail') AS CreatorWorkEmail,
json_extract(Metadata, '$.XMP-iptcCore:CreatorWorkTelephone') AS CreatorWorkTelephone,
json_extract(Metadata, '$.XMP-iptcCore:CreatorWorkURL') AS CreatorWorkURL
FROM Image;
DROP VIEW IF EXISTS "vImage";
CREATE VIEW vImage AS
SELECT ImageId, PhotoLibraryId, Filepath, Album, Format, Filename, Filesize,
strftime('%Y-%m-%d %I:%M:%S %p', FileCreatedDate) AS FileCreatedDate,
strftime('%Y-%m-%d %I:%M:%S %p', FileModifiedDate) AS FileModifiedDate, 
Title,
Description,
Rating,
strftime('%Y-%m-%d %I:%M:%S %p', DateTimeTaken) AS DateTimeTaken, 
DateTimeTakenTimeZone,
Device,
Latitude,
Longitude,
Altitude,
Location,
City,
StateProvince,
Country,
CountryCode,
Creator,
Copyright,
Metadata,
StuctMetadata,
strftime('%Y-%m-%d %I:%M:%S %p', RecordAdded) AS RecordAdded,
AddedBatchId,
strftime('%Y-%m-%d %I:%M:%S %p', RecordModified) AS RecordModified,
ModifiedBatchId,
DateTimeTakenSource,
SHA1
FROM Image;
DROP VIEW IF EXISTS "vImageCameraSettings";
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
DROP VIEW IF EXISTS "vImageUniqueID";
CREATE VIEW vImageUniqueID AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.ExifIFD:ImageUniqueID') AS ImageUniqueID
FROM Image
ORDER BY Filepath ASC;
DROP VIEW IF EXISTS "vLabels";
CREATE VIEW vLabels AS
SELECT ImageId,Filepath,
json_extract(Metadata, '$.XMP-xmp:Label') AS Label,
json_extract(Metadata, '$.XMP-photoshop:Urgency') AS Urgency
FROM Image;
DROP VIEW IF EXISTS "vLegacyWindowsXP";
CREATE VIEW vLegacyWindowsXP AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:XPTitle') AS XPTitle,
json_extract(Metadata, '$.IFD0:XPSubject') AS XPSubject,
json_extract(Metadata, '$.IFD0:XPComment') AS XPComment,
json_extract(Metadata, '$.IFD0:XPAuthor') AS XPAuthor,
json_extract(Metadata, '$.IFD0:XPKeywords') AS XPKeywords,
Metadata
FROM Image;
DROP VIEW IF EXISTS "vLegacy_IPTC_IMM";
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
DROP VIEW IF EXISTS "vLensInfo";
CREATE VIEW vLensInfo AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.Composite:LensID') AS LensID
FROM Image;
DROP VIEW IF EXISTS "vMetadataKeys";
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
DROP VIEW IF EXISTS "vMetadataModificationComparison";
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
DROP VIEW IF EXISTS "vMissingGeotags";
CREATE VIEW vMissingGeotags AS
SELECT Filepath, Latitude,Longitude Location,StateProvince,Country,City
FROM Image
WHERE length(Location)=0 AND length(StateProvince)=0 AND length(Country)=0 AND length(City)=0;
DROP VIEW IF EXISTS "vMonthlyPhotosTaken";
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
DROP VIEW IF EXISTS "vPeopleTagCount";
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
DROP VIEW IF EXISTS "vPeopleTagRegionCountDiff";
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
DROP VIEW IF EXISTS "vPeopleTagsPerImage";
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
DROP VIEW IF EXISTS "vPhotoDates";
CREATE VIEW vPhotoDates AS
SELECT Filepath,DateTimeTaken, DateTimeTakenTimeZone,json_extract(Metadata, '$.ExifIFD:DateTimeOriginal') AS Exif_DateTimeOriginal,json_extract(Metadata, '$.ExifIFD:CreateDate') AS Exif_CreateDate, json_extract(Metadata, '$.IPTC:DateCreated') AS IPTC_DateCreated,json_extract(Metadata, '$.IPTC:TimeCreated') AS IPTC_TimeCreated, json_extract(Metadata, '$.XMP-exif:DateTimeOriginal') AS XMPexif_DateTimeOriginal, json_extract(Metadata, '$.XMP-photoshop:DateCreated') AS XMPphotoshop_DateCreated, Metadata FROM Image;
DROP VIEW IF EXISTS "vPhotoLibraries";
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
DROP VIEW IF EXISTS "vRatingCounts";
CREATE VIEW vRatingCounts AS
SELECT 
    CASE 
        WHEN TRIM(Rating) = '' THEN '(not specified)'
        ELSE Rating
    END AS Rating,
    COUNT(ImageId) AS ImageCount
FROM 
    Image
GROUP BY 
    CASE 
        WHEN TRIM(Rating) = '' THEN '(not specified)'
        ELSE Rating
    END;
DROP VIEW IF EXISTS "vRatings";
CREATE VIEW vRatings AS
SELECT ImageId,Filepath, Rating,
json_extract(Metadata, '$.XMP-xmp:Rating') AS XMPRating,
json_extract(Metadata, '$.XMP-microsoft:RatingPercent') AS MicrosoftRating, 
json_extract(Metadata, '$.IFD0:Rating') AS ExifRating,
json_extract(Metadata, '$.IFD0:RatingPercent') AS ExifRatingPercent
FROM Image;
DROP VIEW IF EXISTS "vRecentlyModified";
CREATE VIEW vRecentlyModified AS
SELECT Filepath,FileModifiedDate, Metadata
FROM Image
ORDER BY FileModifiedDate DESC;
DROP VIEW IF EXISTS "vRegionMismatch";
CREATE VIEW vRegionMismatch AS
SELECT *
FROM vFileDimensions
WHERE (FileWidth<>RWidth OR FileHeight<>RHeight) AND (FileWidth<>RHeight OR FileHeight<>RWidth);
DROP VIEW IF EXISTS "vRights";
CREATE VIEW vRights AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:Copyright') AS Copyright,
json_extract(Metadata, '$.IPTC:CopyrightNotice') AS CopyrightNotice, 
json_extract(Metadata, '$.XMP-dc:Rights') AS Rights,
json_extract(Metadata, '$.XMP-tiff:Copyright') AS TiffCopyright
FROM Image;
DROP VIEW IF EXISTS "vSaveMetadataDotOrg";
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
DROP VIEW IF EXISTS "vTagCount";
CREATE VIEW vTagCount
AS 
SELECT Tag.TagID,Tag.TagName, IFNULL(COUNT(relationTag.TagId),0) AS 'TagCount' 
FROM Tag 
LEFT JOIN relationTag on relationTag.TagId = Tag.TagId
GROUP BY Tag.TagName
ORDER BY TagCount DESC;
DROP VIEW IF EXISTS "vTagsPerImage";
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
DROP VIEW IF EXISTS "vTagsSansPeople";
CREATE VIEW vTagsSansPeople AS
SELECT TagName
FROM Tag
WHERE TagName NOT IN (
    SELECT PersonName FROM PeopleTag
);
DROP VIEW IF EXISTS "vTitles";
CREATE VIEW vTitles AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.XMP-dc:Title') AS Title,
json_extract(Metadata, '$.IPTC:ObjectName') AS ObjectName, 
json_extract(Metadata, '$.IFD0:XPTitle') AS XPTitle
FROM Image;
DROP VIEW IF EXISTS "vTitlesCount";
CREATE VIEW vTitlesCount AS
SELECT
    COUNT(*) AS TotalImages,
    COUNT(CASE WHEN Title IS NOT NULL AND TRIM(Title) <> '' THEN 1 END) AS ImagesWithTitle,
    COUNT(CASE WHEN Title IS NULL OR TRIM(Title) = '' THEN 1 END) AS ImagesWithoutTitle,
    ROUND(
        100.0 * COUNT(CASE WHEN Title IS NOT NULL AND TRIM(Title) <> '' THEN 1 END) 
        / COUNT(*),
        2
    ) AS PercentageWithTitle
FROM Image;
DROP VIEW IF EXISTS "vWeatherTags";
CREATE VIEW vWeatherTags AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.ExifIFD:AmbientTemperature') AS AmbientTemperature,
json_extract(Metadata, '$.ExifIFD:Humidity') AS Humidity,
json_extract(Metadata, '$.ExifIFD:Pressure') AS Pressure,
Metadata
FROM Image;
DROP INDEX IF EXISTS "idx_image_filepath";
CREATE INDEX "idx_image_filepath" ON "Image" (
	"Filepath"
);
DROP INDEX IF EXISTS "idx_image_record_modified";
CREATE INDEX "idx_image_record_modified" ON "Image" (
	"RecordModified"
);
DROP INDEX IF EXISTS "idx_metadata_history_image_id";
CREATE INDEX "idx_metadata_history_image_id" ON "MetadataHistory" (
	"ImageId"
);
COMMIT;
