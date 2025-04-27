using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{    public class ImageFile
    {
        public string FilePath { get; set; }
        public string FileModifiedDate { get; set; }
        public string FileExtension { get; set; }
        public string FileName { get; set; }
        public string FileSize { get; set; }
        public string FileCreatedDate { get; set; }

        public ImageFile(string filePath, string fileModifiedDate, string fileExtension, string fileName, string fileSize, string fileCreatedDate)
        {
            FilePath = filePath;
            FileModifiedDate = fileModifiedDate;
            FileExtension = fileExtension;
            FileName = fileName;
            FileSize = fileSize;
            FileCreatedDate = fileCreatedDate;
        }
    }
}
