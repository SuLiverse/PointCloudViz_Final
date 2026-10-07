using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Analysis;

/// <summary>等宽直方图。</summary>
public sealed class Histogram
{
    public static Histogram Empty { get; } = new(0, 0, []);

    public Histogram(double min, double max, int[] counts)
    {
        Min = min;
        Max = max;
        Counts = counts;
    }

    public double Min { get; }
    public double Max { get; }
    public IReadOnlyList<int> Counts { get; }
    public int BinCount => Counts.Count;
    public double BinWidth => BinCount == 0 ? 0 : (Max - Min) / BinCount;
    public int Total => Counts.Sum();
    public int Peak => Counts.Count == 0 ? 0 : Counts.Max();

    public static Histogram Build(ReadOnlySpan<PointRecord> points, int bins, Func<PointRecord, double> selector, double min, double max)
    {
        if (bins <= 0 || points.IsEmpty) return Empty;
        var counts = new int[bins];
        double width = max - min;
        foreach (ref readonly var p in points)
        {
            int b = width <= 0 ? 0 : (int)((selector(p) - min) * bins / width);
            counts[Math.Clamp(b, 0, bins - 1)]++;
        }
        return new Histogram(min, max, counts);
    }

    /// <summary>
    /// 近似分位数（线性插值所在桶）。用于着色时剔除极端值，
    /// 例如取 2%~98% 分位作为色带范围，避免少量噪点把颜色"压扁"。
    /// </summary>
    public double Quantile(double q)
    {
        if (BinCount == 0) return Min;
        q = Math.Clamp(q, 0, 1);
        long total = Total;
        double target = q * total;
        long cumulative = 0;
        for (int i = 0; i < BinCount; i++)
        {
            if (cumulative + Counts[i] >= target)
            {
                double within = Counts[i] == 0 ? 0 : (target - cumulative) / Counts[i];
                return Min + (i + within) * BinWidth;
            }
            cumulative += Counts[i];
        }
        return Max;
    }
}
