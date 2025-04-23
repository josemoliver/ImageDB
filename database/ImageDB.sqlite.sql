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
	PRIMARY KEY("BatchID" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Image" (
	"ImageId"	INTEGER,
	"PhotoLibraryId"	INTEGER,
	"BatchId"	INTEGER,
	"Filepath"	TEXT UNIQUE,
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
	"RecordAdded"	TEXT,
	"RecordModified"	TEXT,
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
	"BatchID"	INTEGER,
	"Datetime"	TEXT,
	"Filepath"	TEXT,
	"SHA1"	TEXT,
	"Filesize"	INTEGER,
	"Operation"	TEXT,
	PRIMARY KEY("LogEntryId" AUTOINCREMENT)
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
CREATE VIEW vDescriptions AS
SELECT ImageId,Filepath, json_extract(Metadata, '$.Description') AS Description, json_extract(Metadata, '$.Caption-Abstract') AS CaptionAbstract, json_extract(Metadata, '$.ImageDescription') AS ImageDescription, json_extract(Metadata, '$.XPComment') AS XPComment
FROM Image;
CREATE VIEW vTitles AS
SELECT ImageId,Filepath, json_extract(Metadata, '$.Title') AS Title, json_extract(Metadata, '$.ObjectName') AS ObjectName, json_extract(Metadata, '$.Headline') AS Headline, json_extract(Metadata, '$.XPTitle') AS XPTitle
FROM Image;
COMMIT;
