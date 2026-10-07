namespace PointCloudViz.Core.Data;

/// <summary>
/// 读取器使用的点云构建器：接收双精度世界坐标，按"首点"或指定原点转换为局部 float 坐标，
/// 最后统一居中。支持按步长抽稀以控制内存。
/// </summary>
public sealed class PointCloudBuilder
{
    private PointRecord[] _buffer;
    private int _count;
    private Double3? _origin;
    private PointAttributes _attributes;

    public PointCloudBuilder(int initialCapacity = 1024, Double3? origin = null)
    {
        _buffer = new PointRecord[Math.Max(16, initialCapacity)];
        _origin = origin;
    }

    public int Count => _count;

    /// <summary>声明点云包含的属性（可多次调用，按位合并）。</summary>
    public void AddAttributes(PointAttributes attributes) => _attributes |= attributes;

    public void Add(double x, double y, double z, float intensity = 0f, Rgb24 color = default, byte classification = 0)
    {
        var origin = _origin ??= new Double3(Math.Round(x), Math.Round(y), Math.Round(z));
        if (_count == _buffer.Length)
            Array.Resize(ref _buffer, checked(_buffer.Length * 2));

        ref var p = ref _buffer[_count++];
        p.Position = new System.Numerics.Vector3((float)(x - origin.X), (float)(y - origin.Y), (float)(z - origin.Z));
        p.Intensity = intensity;
        p.Color = color;
        p.Classification = classification;
    }

    /// <summary>
    /// 后处理颜色：调用方可在构建前遍历/改写颜色（例如 LAS 判断 8 位还是 16 位颜色）。
    /// </summary>
    public Span<PointRecord> Points => _buffer.AsSpan(0, _count);

    public PointCloud Build(string? name = null)
    {
        var points = _count == _buffer.Length ? _buffer : _buffer.AsSpan(0, _count).ToArray();
        var attributes = _attributes;
        if (!HasNonDefaultColor(points)) attributes &= ~PointAttributes.Color;
        var origin = _origin ?? Double3.Zero;

        // 就地居中，避免再复制一份点数组
        var bounds = BoundingBox.FromPoints(points);
        if (!bounds.IsEmpty)
        {
            var c = bounds.Center;
            for (int i = 0; i < points.Length; i++) points[i].Position -= c;
            origin += c;
        }

        _buffer = [];
        _count = 0;
        return new PointCloud(points, origin, attributes, name);
    }

    private static bool HasNonDefaultColor(ReadOnlySpan<PointRecord> points)
    {
        foreach (ref readonly var p in points)
        {
            if (p.Color != default) return true;
        }
        return false;
    }
}
