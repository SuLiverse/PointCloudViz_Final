using System.Numerics;

namespace PointCloudViz.Core.Data;

/// <summary>轴对齐包围盒（局部坐标）。</summary>
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    /// <summary>空包围盒：Min 为 +∞、Max 为 −∞，与任意点合并后即为该点。</summary>
    public static BoundingBox Empty => new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;

    public Vector3 Center => IsEmpty ? Vector3.Zero : (Min + Max) * 0.5f;

    /// <summary>对角线长度，常用于确定相机距离、拾取容差等尺度。</summary>
    public float Diagonal => Size.Length();

    public float MaxExtent => MathF.Max(Size.X, MathF.Max(Size.Y, Size.Z));

    public BoundingBox Include(Vector3 p) => new(Vector3.Min(Min, p), Vector3.Max(Max, p));

    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;

    public static BoundingBox FromPoints(ReadOnlySpan<PointRecord> points)
    {
        if (points.IsEmpty) return Empty;
        var min = points[0].Position;
        var max = min;
        for (int i = 1; i < points.Length; i++)
        {
            var p = points[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new BoundingBox(min, max);
    }

    public override string ToString() => IsEmpty
        ? "(empty)"
        : $"[{Min.X:F2}, {Min.Y:F2}, {Min.Z:F2}] - [{Max.X:F2}, {Max.Y:F2}, {Max.Z:F2}]";
}
