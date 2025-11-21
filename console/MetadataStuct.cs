using ImageDB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{

    /// <summary>
    /// Represents the collection of Metadata Working Group (MWG) types used by the ImageDB project.
    /// This container class groups nested types that describe applied dimensions,
    /// rectangular areas, region information and lists, and collection metadata.
    /// These types are intended for serialization and data transfer of image
    /// metadata such as region coordinates, units, names, and collection URIs.
    /// Ref: https://web.archive.org/web/20120131102845/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf
    /// </summary>
    /// 
    internal class MetadataStuct
    {

        public class AppliedToDimensions
        {
            public string? H { get; set; }
            public string? Unit { get; set; }
            public string? W { get; set; }
        }

        public class Area
        {
            public string? H { get; set; }
            public string? Unit { get; set; }
            public string? W { get; set; }
            public string? X { get; set; }
            public string? Y { get; set; }
            public string? D { get; set; }
        }

        public class RegionInfo
        {
            public AppliedToDimensions AppliedToDimensions { get; set; } = new AppliedToDimensions();
            public List<RegionList> RegionList { get; set; } = new List<RegionList>();
        }

        public class RegionList
        {
            public Area Area { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
        }

        public class PersonInImageWDetails
        {
            public List<String> PersonId { get; set; } = new List<String>();
            public string PersonName { get; set; }
        }

        public class Collection
        {
            public string CollectionName { get; set; }
            public string CollectionURI { get; set; }
        }

        public class Struct
        {
            public string SourceFile { get; set; }
            public RegionInfo RegionInfo { get; set; }
            public List<Collection> Collections { get; set; }
            public List<PersonInImageWDetails> PersonInImageWDetails { get; set; }
        }
    }
}
