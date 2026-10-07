using System.Numerics;

namespace PointCloudViz_Final.Models;

public class PointCloud
{
    private List<PointRecord> _points;
    private OctreeNode? _octree;
    public IReadOnlyList<PointRecord> Points => _points;
    public BoundingBox BBox { get; private set; }
    public int Count => _points.Count;
    public Rendering.ChunkedPointCloud? ChunkedCloud { get; private set; }

    public PointCloud(IEnumerable<PointRecord> points)
    {
        _points = Validate(points);
        BBox = BoundingBox.FromPoints(_points);
    }

    public void Replace(IEnumerable<PointRecord> points)
    {
        // Materialize first: Replace(Points) must not erase its own input.
        var replacement = Validate(points);
        _points = replacement;
        BBox = BoundingBox.FromPoints(_points);
        _octree = null;
        ChunkedCloud = null;
    }

    private static List<PointRecord> Validate(IEnumerable<PointRecord> points)
    {
        var list = points.ToList();
        if (list.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) ||
                          !float.IsFinite(p.Z) || !float.IsFinite(p.Intensity)))
            throw new ArgumentException("Point coordinates and intensity must be finite.", nameof(points));
        return list;
    }

    public List<PointRecord> GetVisiblePoints(Matrix4x4 viewProj)
    {
        if (Count == 0) return new();
        _octree ??= OctreeNode.Build(_points, BBox);
        var visible = new List<PointRecord>();
        _octree.CollectVisiblePoints(viewProj, visible);
        return visible;
    }

    public (int Count, float MeanZ, float MinZ, float MaxZ) StatsZ()
        => StatsZ(float.NegativeInfinity, float.PositiveInfinity);

    public (int Count, float MeanZ, float MinZ, float MaxZ) StatsZ(float minZ, float maxZ)
    {
        double sum = 0;
        int count = 0;
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (var p in _points)
        {
            if (p.Z < minZ || p.Z > maxZ) continue;
            count++;
            sum += p.Z;
            min = Math.Min(min, p.Z);
            max = Math.Max(max, p.Z);
        }
        return count == 0 ? (0, 0, 0, 0) : (count, (float)(sum / count), min, max);
    }
}
