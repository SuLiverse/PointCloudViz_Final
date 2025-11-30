using System.Collections.Generic;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Filters
{
    public class RadiusOutlierFilter : IPointFilter
    {
        public float Radius { get; }
        public int MinNeighbors { get; }
        public string Name => $"RadiusOutlier[r={Radius},k={MinNeighbors}]";

        public RadiusOutlierFilter(float radius, int minNeighbors)
        {
            Radius = radius; MinNeighbors = minNeighbors;
        }

        public IEnumerable<PointRecord> Apply(IEnumerable<PointRecord> input, BoundingBox bbox)
        {
            var pts = new List<PointRecord>(input);
            float cell = Radius;
            var grid = new Dictionary<(int,int,int), List<int>>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                int ix = (int)System.Math.Floor((p.X - bbox.MinX)/cell);
                int iy = (int)System.Math.Floor((p.Y - bbox.MinY)/cell);
                int iz = (int)System.Math.Floor((p.Z - bbox.MinZ)/cell);
                var key = (ix,iy,iz);
                if (!grid.TryGetValue(key, out var list)) { list = new List<int>(); grid[key]=list; }
                list.Add(i);
            }
            float r2 = Radius * Radius;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                int ix = (int)System.Math.Floor((p.X - bbox.MinX)/cell);
                int iy = (int)System.Math.Floor((p.Y - bbox.MinY)/cell);
                int iz = (int)System.Math.Floor((p.Z - bbox.MinZ)/cell);
                int cnt = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (grid.TryGetValue((ix+dx, iy+dy, iz+dz), out var inds))
                    {
                        foreach (var j in inds)
                        {
                            if (j == i) continue;
                            var q = pts[j];
                            var d2 = (p.X-q.X)*(p.X-q.X) + (p.Y-q.Y)*(p.Y-q.Y) + (p.Z-q.Z)*(p.Z-q.Z);
                            if (d2 <= r2) cnt++;
                            if (cnt >= MinNeighbors) break;
                        }
                    }
                    if (cnt >= MinNeighbors) break;
                }
                if (cnt >= MinNeighbors) yield return p;
            }
        }
    }
}
