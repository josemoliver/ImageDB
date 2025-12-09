using ImageDB.Models;
using Microsoft.EntityFrameworkCore;
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

        public async Task<List<Region>> GetExistingRegions(int imageId)
        {
            // Retrieve all existing regions for comparison
            return dbFiles.Regions
                .Where(r => r.ImageId == imageId)
                .ToList();
        }

        public async Task DeleteRegionsExcept(int imageId, List<int> regionIdsToKeep)
        {
            // Delete regions that are not in the keep list
            var regionsToDelete = dbFiles.Regions
                .Where(r => r.ImageId == imageId && !regionIdsToKeep.Contains(r.RegionId))
                .ToList();

            if (regionsToDelete.Count > 0)
            {
                foreach (var region in regionsToDelete)
                {
                    dbFiles.Regions.Remove(region);
                }
                dbFiles.SaveChanges();
            }
        }

        /// <summary>
        /// Gets existing collections for an image (for comparison).
        /// Returns tuples of (CollectionName, CollectionUri).
        /// </summary>
        public async Task<HashSet<(string name, string uri)>> GetExistingCollections(int imageId)
        {
            var existingCollections = await dbFiles.Collections
                .Where(c => c.ImageId == imageId)
                .Select(c => new { c.CollectionName, c.CollectionUri })
                .ToListAsync();
            
            return existingCollections
                .Select(c => (c.CollectionName ?? string.Empty, c.CollectionUri ?? string.Empty))
                .ToHashSet();
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

        /// <summary>
        /// Gets existing persons for an image (for comparison).
        /// Returns tuples of (PersonName, PersonIdentifier).
        /// </summary>
        public async Task<HashSet<(string name, string identifier)>> GetExistingPersons(int imageId)
        {
            var existingPersons = await dbFiles.Persons
                .Where(p => p.ImageId == imageId)
                .Select(p => new { p.PersonName, p.PersonIdentifier })
                .ToListAsync();
            
            return existingPersons
                .Select(p => (p.PersonName ?? string.Empty, p.PersonIdentifier ?? string.Empty))
                .ToHashSet();
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

        public async Task<int> AddRegion(int imageId, string? regionName, string? regionType, string? regionAreaUnit, string? regionAreaH, string? regionAreaW, string? regionAreaX, string? regionAreaY, string? regionAreaD, byte[] regionThumbnail)
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
            return region.RegionId;
        }

        public static bool RegionCoordinatesMatch(Region existingRegion, decimal? h, decimal? w, decimal? x, decimal? y, decimal? d)
        {
            // Compare region coordinates with tolerance for floating point comparison
            const decimal tolerance = 0.000001m;
            
            return Math.Abs((existingRegion.RegionAreaH ?? 0) - (h ?? 0)) < tolerance &&
                   Math.Abs((existingRegion.RegionAreaW ?? 0) - (w ?? 0)) < tolerance &&
                   Math.Abs((existingRegion.RegionAreaX ?? 0) - (x ?? 0)) < tolerance &&
                   Math.Abs((existingRegion.RegionAreaY ?? 0) - (y ?? 0)) < tolerance &&
                   Math.Abs((existingRegion.RegionAreaD ?? 0) - (d ?? 0)) < tolerance;
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

