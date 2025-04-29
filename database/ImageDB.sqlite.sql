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
CREATE VIEW vCreator AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:Artist') AS Artist,
json_extract(Metadata, '$.IPTC:By-line') AS ByLine, 
json_extract(Metadata, '$.XMP-dc:Creator') AS Creator, 
json_extract(Metadata, '$.XMP-tiff:Artist') AS TiffArtist
FROM Image;
CREATE VIEW vDescriptions AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.XMP-dc:Description') AS Description,
json_extract(Metadata, '$.IPTC:Caption-Abstract') AS CaptionAbstract, 
json_extract(Metadata, '$.IFD0:ImageDescription') AS ImageDescription, 
json_extract(Metadata, '$.ExifIFD:UserComment') AS UserComment, 
json_extract(Metadata, '$.XMP-tiff:ImageDescription') AS TiffImageDescription,
json_extract(Metadata, '$.IFD0:XPComment') AS XPComment
FROM Image;
CREATE VIEW vDevices AS
SELECT ImageId,Filepath,Device, 
json_extract(Metadata, '$.IFD0:Make') AS Make,
json_extract(Metadata, '$.IFD0:Model') AS Model 
FROM Image;
CREATE VIEW vDevicesCount AS
SELECT Device, COUNT(Device) AS DeviceCount FROM vDevices
GROUP BY Device
ORDER BY DeviceCount DESC;
CREATE VIEW vDuplicateFilenames AS
SELECT LOWER(Filename) AS Filename, COUNT(*) 
FROM Image
GROUP BY LOWER(Filename)
HAVING COUNT(*) > 1;
CREATE VIEW vLocations AS
SELECT Location,StateProvince,Country,City,AVG(Latitude) AS Latitude, AVG(Longitude) AS Longitude 
FROM Image
GROUP BY Location,StateProvince,Country,City;
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
CREATE VIEW vPhotoDates AS
SELECT Filepath,DateTimeTaken, DateTimeTakenTimeZone,json_extract(Metadata, '$.IPTC:DateCreated') AS IPTCDate,json_extract(Metadata, '$.ExifIFD:DateTimeOriginal') AS ExifDateTimeOriginal,json_extract(Metadata, '$.IPTC:TimeCreated') AS IPTCTime, json_extract(Metadata, '$.XMP-photoshop:DateCreated') AS XMPDateTaken, Metadata FROM Image;
CREATE VIEW vRights AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.IFD0:Copyright') AS Copyright,
json_extract(Metadata, '$.IPTC:CopyrightNotice') AS CopyrightNotice, 
json_extract(Metadata, '$.XMP-dc:Rights') AS Rights,
json_extract(Metadata, '$.XMP-tiff:Copyright') AS TiffCopyright
FROM Image;
CREATE VIEW vTitles AS
SELECT ImageId,Filepath, 
json_extract(Metadata, '$.XMP-dc:Title') AS Title,
json_extract(Metadata, '$.IPTC:ObjectName') AS ObjectName, 
json_extract(Metadata, '$.IPTC:Headline') AS Headline, 
json_extract(Metadata, '$.IFD0:XPTitle') AS XPTitle
FROM Image;
COMMIT;
