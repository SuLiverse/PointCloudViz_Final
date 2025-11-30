using System.Collections.Generic;

namespace PointCloudViz_Final.Models
{
    public struct BoundingBox
    {
        public float MinX, MinY, MinZ;
        public float MaxX, MaxY, MaxZ;

        public BoundingBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }

        public override string ToString() => $"[{MinX:F2},{MinY:F2},{MinZ:F2}] - [{MaxX:F2},{MaxY:F2},{MaxZ:F2}]";

        public static BoundingBox FromPoints(IReadOnlyList<PointRecord> pts)
        {
            if (pts.Count == 0) return new BoundingBox();
            float minX = pts[0].X, minY = pts[0].Y, minZ = pts[0].Z;
            float maxX = pts[0].X, maxY = pts[0].Y, maxZ = pts[0].Z;
            for (int i = 1; i < pts.Count; i++)
            {
                var p = pts[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            return new BoundingBox(minX, minY, minZ, maxX, maxY, maxZ);
        }
    }
}
