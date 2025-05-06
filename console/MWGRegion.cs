using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    internal class MWGRegion
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
        }

        public class RegionInfo
        {
            public AppliedToDimensions AppliedToDimensions { get; set; }
            public List<RegionList> RegionList { get; set; }
        }

        public class RegionList
        {
            public Area Area { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
        }

        public class Region
        {
            public string SourceFile { get; set; }
            public RegionInfo RegionInfo { get; set; }
        }
    }
}
