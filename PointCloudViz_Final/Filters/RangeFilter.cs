using System.Collections.Generic;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Filters
{
    public class RangeFilter : IPointFilter
    {
        public float MinZ { get; }
        public float MaxZ { get; }
        public string Name => $"ZRange[{MinZ},{MaxZ}]";

        public RangeFilter(float minZ, float maxZ)
        {
            if (!float.IsFinite(minZ) || !float.IsFinite(maxZ) || minZ > maxZ)
                throw new System.ArgumentException("Z range must be finite and minimum must not exceed maximum.");
            MinZ = minZ; MaxZ = maxZ;
        }

        public IEnumerable<PointRecord> Apply(IEnumerable<PointRecord> input, BoundingBox bbox, System.Threading.CancellationToken token = default)
        {
            foreach (var p in input)
            {
                token.ThrowIfCancellationRequested();
                if (p.Z >= MinZ && p.Z <= MaxZ) yield return p;
            }
        }
    }
}
