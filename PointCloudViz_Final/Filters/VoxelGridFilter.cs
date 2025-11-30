using System.Collections.Generic;
using System.Numerics;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Filters
{
    public class VoxelGridFilter : IPointFilter
    {
        public float VoxelSize { get; }
        public string Name => $"Voxel[{VoxelSize}]";
        public VoxelGridFilter(float voxelSize) { VoxelSize = voxelSize; }

        public IEnumerable<PointRecord> Apply(IEnumerable<PointRecord> input, BoundingBox bbox)
        {
            var dict = new Dictionary<long, Accum>();
            foreach (var p in input)
            {
                int ix = (int)System.Math.Floor((p.X - bbox.MinX) / VoxelSize);
                int iy = (int)System.Math.Floor((p.Y - bbox.MinY) / VoxelSize);
                int iz = (int)System.Math.Floor((p.Z - bbox.MinZ) / VoxelSize);

                long key = Hash(ix, iy, iz);
                if (!dict.TryGetValue(key, out var acc)) acc = new Accum();

                acc.SumX += p.X;
                acc.SumY += p.Y;
                acc.SumZ += p.Z;
                acc.SumI += p.Intensity;
                acc.SumR += p.Color.R;
                acc.SumG += p.Color.G;
                acc.SumB += p.Color.B;
                acc.Count++;

                dict[key] = acc;
            }

            foreach (var kv in dict)
            {
                var v = kv.Value;
                if (v.Count == 0) continue;
                float inv = 1f / v.Count;
                yield return new PointRecord(
                    (float)(v.SumX * inv),
                    (float)(v.SumY * inv),
                    (float)(v.SumZ * inv),
                    (float)(v.SumI * inv),
                    System.Windows.Media.Color.FromRgb(
                        (byte)(v.SumR * inv),
                        (byte)(v.SumG * inv),
                        (byte)(v.SumB * inv)
                    ));
            }
        }

        private struct Accum
        {
            public double SumX;
            public double SumY;
            public double SumZ;
            public double SumI;
            public double SumR;
            public double SumG;
            public double SumB;
            public int Count;
        }

        private long Hash(int x, int y, int z)
        {
            unchecked
            {
                long h = 17;
                h = h * 31 + x;
                h = h * 31 + y;
                h = h * 31 + z;
                return h;
            }
        }
    }
}
