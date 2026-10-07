using System.Numerics;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Processing;

/// <summary>
/// 体素网格下采样：每个体素内的点合并为一个点（坐标、强度、颜色取平均，分类取首点）。
/// <para>
/// 体素键使用 3×21 位无碰撞编码（旧版用 h*31+x 的哈希会让不同体素互相"串门"），
/// 每轴最多 2,097,152 个体素。
/// </para>
/// </summary>
public sealed class VoxelGridFilter : IPointFilter
{
    private const int MaxCellsPerAxis = 1 << 21;

    public VoxelGridFilter(float voxelSize)
    {
        if (!(voxelSize > 0)) throw new ArgumentOutOfRangeException(nameof(voxelSize), "体素大小必须大于 0。");
        VoxelSize = voxelSize;
    }

    public float VoxelSize { get; }

    public string Description => $"体素下采样 ({VoxelSize:G4})";

    public PointCloud Apply(PointCloud input, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.IsEmpty) return input;

        var bounds = input.Bounds;
        var cells = bounds.Size / VoxelSize;
        if (cells.X >= MaxCellsPerAxis || cells.Y >= MaxCellsPerAxis || cells.Z >= MaxCellsPerAxis)
            throw new ArgumentException($"体素过小：点云范围 {bounds.MaxExtent:F1} 需要超过 {MaxCellsPerAxis:N0} 个体素/轴，请增大体素尺寸。");

        var pts = input.Points;
        var map = new Dictionary<long, int>(Math.Min(pts.Length, 1 << 20));
        var acc = new List<Accumulator>();
        float inv = 1f / VoxelSize;

        for (int i = 0; i < pts.Length; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(i / (double)pts.Length);
            }
            ref readonly var p = ref pts[i];
            var c = (p.Position - bounds.Min) * inv;
            long key = ((long)(int)c.X << 42) | ((long)(int)c.Y << 21) | (long)(int)c.Z;

            ref int slot = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(map, key, out bool exists);
            if (!exists)
            {
                slot = acc.Count;
                acc.Add(new Accumulator { Classification = p.Classification });
            }
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(acc);
            ref var a = ref span[slot];
            a.Sum += new Vector3D(p.Position);
            a.Intensity += p.Intensity;
            a.R += p.Color.R;
            a.G += p.Color.G;
            a.B += p.Color.B;
            a.Count++;
        }

        var result = new PointRecord[acc.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var a = acc[i];
            double n = a.Count;
            result[i] = new PointRecord(
                a.Sum.ToVector3(1 / n),
                (float)(a.Intensity / n),
                new Rgb24((byte)Math.Round(a.R / n), (byte)Math.Round(a.G / n), (byte)Math.Round(a.B / n)),
                a.Classification);
        }
        progress?.Report(1);
        return input.WithPoints(result);
    }

    private struct Accumulator
    {
        public Vector3D Sum;
        public double Intensity;
        public long R, G, B;
        public int Count;
        public byte Classification;
    }

    /// <summary>双精度累加，避免大量 float 求和的舍入误差。</summary>
    private struct Vector3D(Vector3 v)
    {
        public double X = v.X, Y = v.Y, Z = v.Z;

        public static Vector3D operator +(Vector3D a, Vector3D b) => new() { X = a.X + b.X, Y = a.Y + b.Y, Z = a.Z + b.Z };

        public readonly Vector3 ToVector3(double scale) => new((float)(X * scale), (float)(Y * scale), (float)(Z * scale));
    }
}
