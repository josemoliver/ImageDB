using ImageDB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    public class PeopleTagService
    {
        private readonly CDatabaseImageDBsqliteContext dbFiles;

        public PeopleTagService(CDatabaseImageDBsqliteContext context)
        {
            dbFiles = context;
        }

        public async Task DeleteRelations(int imageId)
        {
            // Delete all relations for the given imageId
            var relationsToDelete = dbFiles.RelationPeopleTags
                .Where(r => r.ImageId == imageId)
                .ToList();

            if (relationsToDelete.Count == 0)
            {
                return;
            }

            foreach (var relation in relationsToDelete)
            {
                dbFiles.RelationPeopleTags.Remove(relation);
            }

            dbFiles.SaveChanges();
        }
        public async Task AddPeopleTags(string personName, int imageId)
        {
            // Check if the person already exists in the PeopleTag table
            var existingTag = dbFiles.PeopleTags
                .FirstOrDefault(tag => tag.PersonName == personName);

            int peopleTagId;

            if (existingTag == null)
            {
                // Person does not exist, add new entry to PeopleTag
                var newTag = new PeopleTag
                {
                    PersonName = personName
                };

                dbFiles.PeopleTags.Add(newTag);
                await dbFiles.SaveChangesAsync();

                peopleTagId = newTag.PeopleTagId; // Get the newly created PeopleTagId
            }
            else
            {
                // Person exists, use the existing PeopleTagId
                peopleTagId = existingTag.PeopleTagId;
            }

            // Add an entry to relationPeopleTag with the PeopleTagId and ImageId
            var relationEntry = new RelationPeopleTag
            {
                PeopleTagId = peopleTagId,
                ImageId = imageId
            };

            dbFiles.RelationPeopleTags.Add(relationEntry);
            await dbFiles.SaveChangesAsync();
        }
    }
}
