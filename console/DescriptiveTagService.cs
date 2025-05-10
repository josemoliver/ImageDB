using ImageDB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    public class DescriptiveTagService
    {
        private readonly CDatabaseImageDBsqliteContext dbFiles;

        public DescriptiveTagService(CDatabaseImageDBsqliteContext context)
        {
            dbFiles = context;
        }

        public async Task DeleteAllRelations(int imageId)
        {
            // Delete all relations for the given imageId
            var relationsToDelete = dbFiles.RelationTags
                .Where(r => r.ImageId == imageId)
                .ToList();

            if (relationsToDelete.Count == 0)
            {
                return;
            }

            foreach (var relation in relationsToDelete)
            {
                dbFiles.RelationTags.Remove(relation);
            }

            dbFiles.SaveChanges();
        }
        public async Task AddTags(string tagName, int imageId)
        {
            // Check if the person already exists in the Tag table
            var existingTag = dbFiles.Tags
                .FirstOrDefault(tag => tag.TagName == tagName);

            int tagId;

            if (existingTag == null)
            {
                // Tag does not exist, add new entry to Tag
                var newTag = new Tag
                {
                    TagName = tagName
                };

                dbFiles.Tags.Add(newTag);
                await dbFiles.SaveChangesAsync();

                tagId = newTag.TagId; // Get the newly created Tag
            }
            else
            {
                // Person exists, use the existing TagId
                tagId = existingTag.TagId;
            }

            // Add an entry to relationTag with the TagId and ImageId
            var relationEntry = new RelationTag
            {
                TagId = tagId,
                ImageId = imageId
            };

            dbFiles.RelationTags.Add(relationEntry);
            await dbFiles.SaveChangesAsync();
        }
    }
}
