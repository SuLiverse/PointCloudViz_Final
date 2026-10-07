using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Analysis;

/// <summary>单个标量通道的统计量。</summary>
public readonly record struct ScalarStats(double Min, double Max, double Mean, double StdDev)
{
    public double Range => Max - Min;
}

/// <summary>点云统计信息（世界坐标）。</summary>
public sealed class CloudStatistics
{
    private CloudStatistics() { }

    public int Count { get; private init; }
    public ScalarStats X { get; private init; }
    public ScalarStats Y { get; private init; }
    public ScalarStats Z { get; private init; }
    public ScalarStats Intensity { get; private init; }

    /// <summary>各分类码的点数（长度 256）。</summary>
    public IReadOnlyList<int> ClassCounts { get; private init; } = [];

    /// <summary>高程直方图。</summary>
    public Histogram ElevationHistogram { get; private init; } = Histogram.Empty;

    /// <summary>平面点密度（点/平方米，按 XY 包围盒面积估算）。</summary>
    public double PlanarDensity
    {
        get
        {
            double area = X.Range * Y.Range;
            return area > 1e-9 ? Count / area : 0;
        }
    }

    public IEnumerable<(byte Class, int Count)> PresentClasses =>
        ClassCounts.Select((c, i) => ((byte)i, c)).Where(t => t.Item2 > 0);

    public static CloudStatistics Compute(PointCloud cloud, int histogramBins = 48)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        var pts = cloud.Points;
        var classCounts = new int[256];
        if (pts.IsEmpty)
        {
            return new CloudStatistics { ClassCounts = classCounts };
        }

        // Welford 在线算法，数值稳定
        var x = new Welford();
        var y = new Welford();
        var z = new Welford();
        var intensity = new Welford();
        foreach (ref readonly var p in pts)
        {
            x.Add(p.Position.X);
            y.Add(p.Position.Y);
            z.Add(p.Position.Z);
            intensity.Add(p.Intensity);
            classCounts[p.Classification]++;
        }

        var o = cloud.Origin;
        var zStats = z.ToStats(o.Z);
        return new CloudStatistics
        {
            Count = pts.Length,
            X = x.ToStats(o.X),
            Y = y.ToStats(o.Y),
            Z = zStats,
            Intensity = intensity.ToStats(0),
            ClassCounts = classCounts,
            ElevationHistogram = Histogram.Build(pts, histogramBins, p => p.Position.Z + o.Z, zStats.Min, zStats.Max),
        };
    }

    private struct Welford
    {
        private long _n;
        private double _mean, _m2, _min, _max;

        public void Add(double v)
        {
            if (_n == 0) { _min = _max = v; }
            else { if (v < _min) _min = v; if (v > _max) _max = v; }
            _n++;
            double d = v - _mean;
            _mean += d / _n;
            _m2 += d * (v - _mean);
        }

        public readonly ScalarStats ToStats(double offset) =>
            new(_min + offset, _max + offset, _mean + offset, _n > 1 ? Math.Sqrt(_m2 / (_n - 1)) : 0);
    }
}
