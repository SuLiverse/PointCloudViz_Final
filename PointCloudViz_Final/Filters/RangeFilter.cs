using System.Collections.Generic;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Filters
{
    public class RangeFilter : IPointFilter
    {
        public float MinZ { get; }
        public float MaxZ { get; }
        public string Name => $"ZRange[{MinZ},{MaxZ}]";

        public RangeFilter(float minZ, float maxZ) { MinZ = minZ; MaxZ = maxZ; }

        public IEnumerable<PointRecord> Apply(IEnumerable<PointRecord> input, BoundingBox bbox)
        {
            foreach (var p in input)
                if (p.Z >= MinZ && p.Z <= MaxZ) yield return p;
        }
    }
}
