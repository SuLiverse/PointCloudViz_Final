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
            if (!float.IsFinite(radius) || radius <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(radius));
            if (minNeighbors < 1) throw new System.ArgumentOutOfRangeException(nameof(minNeighbors));
            Radius = radius; MinNeighbors = minNeighbors;
        }

        public IEnumerable<PointRecord> Apply(IEnumerable<PointRecord> input, BoundingBox bbox, System.Threading.CancellationToken token = default)
        {
            var pts = new List<PointRecord>(input);
            float cell = Radius;
            var grid = new Dictionary<(long,long,long), List<int>>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                token.ThrowIfCancellationRequested();
                long ix = checked((long)System.Math.Floor(((double)p.X - bbox.MinX)/cell));
                long iy = checked((long)System.Math.Floor(((double)p.Y - bbox.MinY)/cell));
                long iz = checked((long)System.Math.Floor(((double)p.Z - bbox.MinZ)/cell));
                var key = (ix,iy,iz);
                if (!grid.TryGetValue(key, out var list)) { list = new List<int>(); grid[key]=list; }
                list.Add(i);
            }
            double r2 = (double)Radius * Radius;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                token.ThrowIfCancellationRequested();
                long ix = checked((long)System.Math.Floor(((double)p.X - bbox.MinX)/cell));
                long iy = checked((long)System.Math.Floor(((double)p.Y - bbox.MinY)/cell));
                long iz = checked((long)System.Math.Floor(((double)p.Z - bbox.MinZ)/cell));
                int cnt = 0;
                for (int dx = -1; dx <= 1 && cnt < MinNeighbors; dx++)
                for (int dy = -1; dy <= 1 && cnt < MinNeighbors; dy++)
                for (int dz = -1; dz <= 1 && cnt < MinNeighbors; dz++)
                {
                    if (grid.TryGetValue((ix+dx, iy+dy, iz+dz), out var inds))
                    {
                        foreach (var j in inds)
                        {
                            token.ThrowIfCancellationRequested();
                            if (j == i) continue;
                            var q = pts[j];
                            double x = (double)p.X - q.X, y = (double)p.Y - q.Y, z = (double)p.Z - q.Z;
                            var d2 = x*x + y*y + z*z;
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
