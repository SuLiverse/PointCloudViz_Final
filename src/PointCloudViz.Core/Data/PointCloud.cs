using System.Numerics;

namespace PointCloudViz.Core.Data;

/// <summary>
/// 不可变点云。
/// <para>
/// 世界坐标 = <see cref="Origin"/>（double）+ 点的局部坐标（float）。
/// 加载后原点位于包围盒中心，局部坐标数值小，既保证 float 精度，也能直接交给 GPU 渲染，
/// 从根本上解决测绘大坐标导致的"黑屏/抖动"问题。
/// </para>
/// <para>所有滤波操作都返回新的 <see cref="PointCloud"/>，撤销/重做只需切换引用。</para>
/// </summary>
public sealed class PointCloud
{
    private readonly PointRecord[] _points;

    public PointCloud(PointRecord[] points, Double3 origin, PointAttributes attributes, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points;
        Origin = origin;
        Attributes = attributes;
        Name = name ?? "PointCloud";
        Bounds = BoundingBox.FromPoints(points);
    }

    /// <summary>显示名称（通常为文件名）。</summary>
    public string Name { get; }

    /// <summary>局部坐标原点在世界坐标系中的位置。</summary>
    public Double3 Origin { get; }

    public PointAttributes Attributes { get; }

    /// <summary>局部坐标包围盒。</summary>
    public BoundingBox Bounds { get; }

    public int Count => _points.Length;

    public bool IsEmpty => _points.Length == 0;

    public ReadOnlySpan<PointRecord> Points => _points;

    /// <summary>内部直接访问底层数组（供需要在 lambda 中并行访问的算法使用，调用方不得修改）。</summary>
    internal PointRecord[] RawPoints => _points;

    public ref readonly PointRecord this[int index] => ref _points[index];

    public bool Has(PointAttributes attribute) => (Attributes & attribute) == attribute;

    public Double3 WorldMin => Bounds.IsEmpty ? Origin : Origin + Bounds.Min;

    public Double3 WorldMax => Bounds.IsEmpty ? Origin : Origin + Bounds.Max;

    public Double3 ToWorld(Vector3 local) => Origin + local;

    public Vector3 ToLocal(Double3 world) => (world - Origin).ToVector3();

    /// <summary>用新的点集创建点云，保留原点、属性与名称。</summary>
    public PointCloud WithPoints(PointRecord[] points, string? name = null) =>
        new(points, Origin, Attributes, name ?? Name);

    public PointCloud WithName(string name) => new(_points, Origin, Attributes, name);

    /// <summary>
    /// 将原点移动到包围盒中心，使局部坐标关于原点对称（数值最小）。
    /// 返回新的点云；若已居中则返回自身。
    /// </summary>
    public PointCloud Recentered()
    {
        if (IsEmpty) return this;
        var c = Bounds.Center;
        if (c == Vector3.Zero) return this;
        var shifted = new PointRecord[_points.Length];
        for (int i = 0; i < shifted.Length; i++)
        {
            shifted[i] = _points[i];
            shifted[i].Position -= c;
        }
        return new PointCloud(shifted, Origin + c, Attributes, Name);
    }

    /// <summary>复制点数组（调用方可自由修改副本）。</summary>
    public PointRecord[] ToArray() => (PointRecord[])_points.Clone();

    public override string ToString() => $"{Name}: {Count:N0} points, origin {Origin}";
}
