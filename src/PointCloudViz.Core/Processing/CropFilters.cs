using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Processing;

/// <summary>高程（Z）范围过滤。参数为世界坐标高程。</summary>
public sealed class ElevationRangeFilter(double minZ, double maxZ) : MaskFilter
{
    public double MinZ { get; } = Math.Min(minZ, maxZ);
    public double MaxZ { get; } = Math.Max(minZ, maxZ);

    public override string Description => $"高程过滤 [{MinZ:F2}, {MaxZ:F2}]";

    protected override void ComputeMask(PointCloud input, Span<bool> keep, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // 转换到局部坐标后比较，避免逐点做 double 运算
        float lo = (float)(MinZ - input.Origin.Z);
        float hi = (float)(MaxZ - input.Origin.Z);
        var pts = input.Points;
        for (int i = 0; i < pts.Length; i++)
        {
            float z = pts[i].Position.Z;
            keep[i] = z >= lo && z <= hi;
        }
    }
}

/// <summary>包围盒裁剪（世界坐标）。<paramref name="invert"/> 为 true 时删除盒内点。</summary>
public sealed class CropBoxFilter(Double3 min, Double3 max, bool invert = false) : MaskFilter
{
    public Double3 Min { get; } = new(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z));
    public Double3 Max { get; } = new(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z));
    public bool Invert { get; } = invert;

    public override string Description => Invert ? "删除盒内点" : "保留盒内点";

    protected override void ComputeMask(PointCloud input, Span<bool> keep, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var box = new BoundingBox(input.ToLocal(Min), input.ToLocal(Max));
        var pts = input.Points;
        for (int i = 0; i < pts.Length; i++)
            keep[i] = box.Contains(pts[i].Position) != Invert;
    }
}

/// <summary>按分类码筛选。</summary>
public sealed class ClassificationFilter(IEnumerable<byte> classesToKeep) : MaskFilter
{
    private readonly bool[] _keep = BuildLookup(classesToKeep);

    public IReadOnlyList<byte> Classes => Enumerable.Range(0, 256).Where(i => _keep[i]).Select(i => (byte)i).ToArray();

    public override string Description => $"分类筛选（保留 {string.Join(",", Classes)}）";

    protected override void ComputeMask(PointCloud input, Span<bool> keep, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var pts = input.Points;
        for (int i = 0; i < pts.Length; i++) keep[i] = _keep[pts[i].Classification];
    }

    private static bool[] BuildLookup(IEnumerable<byte> classes)
    {
        var lookup = new bool[256];
        foreach (var c in classes) lookup[c] = true;
        return lookup;
    }
}

/// <summary>
/// 均匀随机抽稀到目标点数。使用固定种子的哈希，结果可复现，且不会像"按步长抽稀"
/// 那样在扫描线数据上产生条纹。
/// </summary>
public sealed class RandomSubsampleFilter(int targetCount, int seed = 12345) : MaskFilter
{
    public int TargetCount { get; } = Math.Max(1, targetCount);

    public override string Description => $"随机抽稀至 {TargetCount:N0} 点";

    protected override void ComputeMask(PointCloud input, Span<bool> keep, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var indices = SamplingIndices(input.Count, TargetCount, seed);
        foreach (var i in indices) keep[i] = true;
    }

    /// <summary>从 n 个元素中选出 k 个的索引（升序），部分 Fisher-Yates 洗牌。</summary>
    public static int[] SamplingIndices(int n, int k, int seed = 12345)
    {
        if (k >= n) return Enumerable.Range(0, n).ToArray();
        var rng = new Random(seed);
        // 稀疏洗牌：只记录被交换过的位置，内存 O(k)
        var swapped = new Dictionary<int, int>(k);
        var result = new int[k];
        for (int i = 0; i < k; i++)
        {
            int j = rng.Next(i, n);
            int vi = swapped.TryGetValue(i, out var a) ? a : i;
            int vj = swapped.TryGetValue(j, out var b) ? b : j;
            swapped[j] = vi;
            result[i] = vj;
        }
        Array.Sort(result);
        return result;
    }
}
