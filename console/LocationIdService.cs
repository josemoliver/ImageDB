using ImageDB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    public class LocationIdService
    {
        private readonly CDatabaseImageDBsqliteContext dbFiles;

        public LocationIdService(CDatabaseImageDBsqliteContext context)
        {
            dbFiles = context;
        }

        public async Task AddLocationId(string locationIdentifier, string locationName, int imageId)
        {
            // Check if the person already exists in the PeopleTag table
            var existingTag = dbFiles.Locations
                .FirstOrDefault(tag => tag.LocationIdentifier == locationIdentifier);

            int locationId;

            if (existingTag == null)
            {
                // Person does not exist, add new entry to PeopleTag
                var newTag = new Location
                {
                    LocationIdentifier = locationIdentifier,
                    LocationName = locationName
                };

                dbFiles.Locations.Add(newTag);
                await dbFiles.SaveChangesAsync();

                locationId = newTag.LocationId; // Get the newly created PeopleTagId
            }
            else
            {
                // Person exists, use the existing PeopleTagId
                locationId = existingTag.LocationId;
            }

            // Add an entry to relationPeopleTag with the PeopleTagId and ImageId
            var relationEntry = new RelationLocation
            {
                LocationId = locationId,
                ImageId = imageId
            };


            dbFiles.RelationLocations.Add(relationEntry);
            await dbFiles.SaveChangesAsync();
        }
    }
}
