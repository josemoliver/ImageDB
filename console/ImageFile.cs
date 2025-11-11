using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    /// <summary>
    /// Represents metadata about a single image file in the ImageDB application.
    /// </summary>
    /// <remarks>
    /// This class stores common file-level metadata used by the application, including
    /// the file path, file name, extension, human-readable file size, and creation/modification timestamps.
    /// - <see cref="FilePath"/>: full path to the file on disk.
    /// - <see cref="FileName"/>: file name including.
    /// - <see cref="FileExtension"/>: file extension.
    /// - <see cref="FileSize"/>: file size in bytes.
    /// - <see cref="FileCreatedDate"/> and <see cref="FileModifiedDate"/>: timestamp strings (ISO 8601 recommended).
    /// Use this object for presenting file metadata in UIs, persisting simple indexes, or transferring lightweight file info
    /// between application layers. For binary access or advanced metadata (EXIF), use specialized types or services.
    /// </remarks>
    public class ImageFile
    {
        /// <summary>
        /// Gets or sets the full path to the image file on disk.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the file modification date as a string. (yyyy-MM-dd hh:mm:ss tt).
        /// </summary>
        public string FileModifiedDate { get; set; }

        /// <summary>
        /// Gets or sets the file extension.
        /// </summary>
        public string FileExtension { get; set; }

        /// <summary>
        /// Gets or sets the file name. Typically includes the extension.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the file size in bytes.
        /// </summary>
        public string FileSize { get; set; }

        /// <summary>
        /// Gets or sets the file creation date as a string. (yyyy-MM-dd hh:mm:ss tt).
        /// </summary>
        public string FileCreatedDate { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageFile"/> class with the provided metadata values.
        /// </summary>
        /// <param name="filePath">Full path to the file.</param>
        /// <param name="fileModifiedDate">Modification timestamp as a string (yyyy-MM-dd hh:mm:ss tt).</param>
        /// <param name="fileExtension">File extension, including the dot (e.g., ".png").</param>
        /// <param name="fileName">File name.</param>
        /// <param name="fileSize">File size in bytes.</param>
        /// <param name="fileCreatedDate">Creation timestamp as a string (yyyy-MM-dd hh:mm:ss tt).</param>
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
