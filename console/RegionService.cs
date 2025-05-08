using ImageDB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    internal class RegionService
    {
        private readonly CDatabaseImageDBsqliteContext dbFiles;

        public RegionService(CDatabaseImageDBsqliteContext context)
        {
            dbFiles = context;
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
                RegionId = Guid.NewGuid().ToString(),
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
    }
}

