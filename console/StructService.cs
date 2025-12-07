using ImageDB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    internal class StructService
    {
        private readonly CDatabaseImageDBsqliteContext dbFiles;

        public StructService(CDatabaseImageDBsqliteContext context)
        {
            dbFiles = context;
        }

        public async Task DeleteRegions(int imageId)
        {
            // Delete all regions for the given imageId
            var regionsToDelete = dbFiles.Regions
                .Where(r => r.ImageId == imageId)
                .ToList();

            if (regionsToDelete.Count == 0)
            {
                return;
            }

            foreach (var region in regionsToDelete)
            {
                dbFiles.Regions.Remove(region);
            }

            dbFiles.SaveChanges();
        }

        public async Task DeleteCollections(int imageId)
        {
            // Delete all collections for the given imageId
            var collectionsToDelete = dbFiles.Collections
                .Where(c => c.ImageId == imageId)
                .ToList();
            
            if (collectionsToDelete.Count == 0)
            {
                return;
            }
            foreach (var collection in collectionsToDelete)
            {
                dbFiles.Collections.Remove(collection);
            }
            dbFiles.SaveChanges();
        }

        public async Task DeleteLocations(int imageId)
        {
            // Delete all relations for the given imageId
            var locationsToDelete = dbFiles.Locations
                .Where(r => r.ImageId == imageId)
                .ToList();

            if (locationsToDelete.Count == 0)
            {
                return;
            }

            foreach (var location in locationsToDelete)
            {
                dbFiles.Locations.Remove(location);
            }

            dbFiles.SaveChanges();
        }

        public async Task DeletePersons(int imageId)
        {
            // Delete all relations for the given imageId
            var personsToDelete = dbFiles.Persons
                .Where(r => r.ImageId == imageId)
                .ToList();

            if (personsToDelete.Count == 0)
            {
                return;
            }

            foreach (var person in personsToDelete)
            {
                dbFiles.Persons.Remove(person);
            }

            dbFiles.SaveChanges();
        }

        public async Task AddRegion(int imageId, string? regionName, string? regionType, string? regionAreaUnit, string? regionAreaH, string? regionAreaW, string? regionAreaX, string? regionAreaY, string? regionAreaD, byte[] regionThumbnail)
        {
            regionName = regionName?.Trim();
            regionAreaUnit = regionAreaUnit?.Trim();
            regionType = regionType?.Trim();

            decimal? regionAreaHDecimal;
            decimal? regionAreaWDecimal;
            decimal? regionAreaXDecimal;
            decimal? regionAreaYDecimal;
            decimal? regionAreaDDecimal;

            regionAreaHDecimal = decimal.TryParse(regionAreaH, out var H) ? H : null;
            regionAreaWDecimal = decimal.TryParse(regionAreaW, out var W) ? W : null;
            regionAreaXDecimal = decimal.TryParse(regionAreaX, out var X) ? X : null;
            regionAreaYDecimal = decimal.TryParse(regionAreaY, out var Y) ? Y : null;
            regionAreaDDecimal = decimal.TryParse(regionAreaD, out var D) ? D : null;

            var region = new Region
            {
                ImageId = imageId,
                RegionName = regionName,
                RegionType = regionType,
                RegionAreaUnit = regionAreaUnit,
                RegionAreaH = regionAreaHDecimal,
                RegionAreaW = regionAreaWDecimal,
                RegionAreaX = regionAreaXDecimal,
                RegionAreaY = regionAreaYDecimal,
                RegionAreaD = regionAreaDDecimal,
                RegionThumbnail = regionThumbnail,
            };

            dbFiles.Regions.Add(region);
            await dbFiles.SaveChangesAsync();
        }

        public async Task AddCollection(int imageId, string? collectionName, string? collectionURI)
        {
            collectionName = collectionName?.Trim();
            collectionURI = collectionURI?.Trim();

            var collection = new Collection
            {
                ImageId = imageId,
                CollectionName = collectionName,
                CollectionUri = collectionURI,
            };

            dbFiles.Collections.Add(collection);
            await dbFiles.SaveChangesAsync();
        }


        public async Task AddLocation(int imageId, string? locationName, string? locationURI, string locationType)
        {
            locationName = locationName?.Trim();
            locationURI = locationURI?.Trim();
            locationType = locationType?.Trim();

            var location = new Location
            {
                ImageId = imageId,
                LocationName = locationName,
                LocationUri = locationURI,
                LocationType = locationType,
            };

            dbFiles.Locations.Add(location);
            await dbFiles.SaveChangesAsync();
        }

        public async Task AddPerson(int imageId, string? personName, string? personIdentifier)
        {
            personName = personName?.Trim();
            personIdentifier = personIdentifier?.Trim();

            var person = new Person
            {
                ImageId = imageId,
                PersonName = personName,
                PersonIdentifier = personIdentifier,
            };

            dbFiles.Persons.Add(person);
            await dbFiles.SaveChangesAsync();
        }
    }
}

