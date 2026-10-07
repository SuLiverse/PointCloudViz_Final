using PointCloudViz.Core.Data;
using PointCloudViz.Core.Spatial;

namespace PointCloudViz.Core.Processing;

/// <summary>
/// 半径离群点剔除（ROR）：半径 r 内邻居数少于 k 的点视为离群点。
/// 基于 KD 树并行计算，达到 k 个邻居即提前终止。
/// </summary>
public sealed class RadiusOutlierFilter : MaskFilter
{
    public RadiusOutlierFilter(float radius, int minNeighbors)
    {
        if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius), "半径必须大于 0。");
        if (minNeighbors < 1) throw new ArgumentOutOfRangeException(nameof(minNeighbors), "最少邻居数必须 ≥ 1。");
        Radius = radius;
        MinNeighbors = minNeighbors;
    }

    public float Radius { get; }
    public int MinNeighbors { get; }

    public override string Description => $"半径离群点剔除 (r={Radius:G4}, k={MinNeighbors})";

    protected override void ComputeMask(PointCloud input, Span<bool> keep, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var tree = new KdTree(input.Points);
        var result = new bool[input.Count];
        int need = MinNeighbors + 1; // 计数包含点自身
        ParallelHelper.For(input.Count, progress, cancellationToken, i =>
        {
            result[i] = tree.CountWithin(tree.Position(i), Radius, need) >= need;
        });
        result.CopyTo(keep);
    }
}

/// <summary>
/// 统计离群点剔除（SOR，与 PCL StatisticalOutlierRemoval 一致）：
/// 计算每点到 k 个近邻的平均距离 d，剔除 d &gt; μ + α·σ 的点。
/// </summary>
public sealed class StatisticalOutlierFilter : MaskFilter
{
    public StatisticalOutlierFilter(int neighbors, double stdMultiplier)
    {
        if (neighbors < 1) throw new ArgumentOutOfRangeException(nameof(neighbors), "邻居数必须 ≥ 1。");
        Neighbors = neighbors;
        StdMultiplier = stdMultiplier;
    }

    public int Neighbors { get; }
    public double StdMultiplier { get; }

    public override string Description => $"统计离群点剔除 (k={Neighbors}, α={StdMultiplier:G3})";

    protected override void ComputeMask(PointCloud input, Span<bool> keep, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        int n = input.Count;
        if (n <= Neighbors)
        {
            keep.Fill(true);
            return;
        }

        var tree = new KdTree(input.Points);
        var meanDistance = new float[n];
        int k = Neighbors + 1; // 第一个近邻是点自身
        ParallelHelper.For(n, progress is null ? null : new Progress<double>(p => progress.Report(p * 0.95)), cancellationToken, i =>
        {
            Span<int> idx = stackalloc int[k];
            Span<float> d2 = stackalloc float[k];
            int found = tree.KNearest(tree.Position(i), idx, d2);
            double sum = 0;
            for (int j = 1; j < found; j++) sum += MathF.Sqrt(d2[j]);
            meanDistance[i] = found > 1 ? (float)(sum / (found - 1)) : 0f;
        });

        double mean = 0;
        foreach (var d in meanDistance) mean += d;
        mean /= n;
        double variance = 0;
        foreach (var d in meanDistance) variance += (d - mean) * (d - mean);
        double std = Math.Sqrt(variance / Math.Max(1, n - 1));
        double threshold = mean + StdMultiplier * std;

        for (int i = 0; i < n; i++) keep[i] = meanDistance[i] <= threshold;
    }
}

internal static class ParallelHelper
{
    /// <summary>分块并行循环，支持取消和节流的进度上报。</summary>
    public static void For(int count, IProgress<double>? progress, CancellationToken cancellationToken, Action<int> body)
    {
        const int chunk = 4096;
        int chunks = (count + chunk - 1) / chunk;
        int done = 0;
        Parallel.For(0, chunks, new ParallelOptions { CancellationToken = cancellationToken }, c =>
        {
            int start = c * chunk, end = Math.Min(count, start + chunk);
            for (int i = start; i < end; i++) body(i);
            int finished = Interlocked.Increment(ref done);
            if (progress is not null && finished % 16 == 0) progress.Report(finished / (double)chunks);
        });
    }
}
