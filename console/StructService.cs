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
            // Delete all relations for the given imageId
            var relationsToDelete = dbFiles.Regions
                .Where(r => r.ImageId == imageId)
                .ToList();

            if (relationsToDelete.Count == 0)
            {
                return;
            }

            foreach (var relation in relationsToDelete)
            {
                dbFiles.Regions.Remove(relation);
            }

            dbFiles.SaveChanges();
        }

        public async Task DeleteCollections(int imageId)
        {
            // Delete all relations for the given imageId
            var relationsToDelete = dbFiles.Collections
                .Where(c => c.ImageId == imageId)
                .ToList();
            
            if (relationsToDelete.Count == 0)
            {
                return;
            }
            foreach (var relation in relationsToDelete)
            {
                dbFiles.Collections.Remove(relation);
            }
            dbFiles.SaveChanges();
        }

        public async Task AddRegion(int imageId, string? regionName, string? regionType, string? regionAreaUnit, string? regionAreaH, string? regionAreaW, string? regionAreaX, string? regionAreaY, string? regionAreaD)
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
    }
}

