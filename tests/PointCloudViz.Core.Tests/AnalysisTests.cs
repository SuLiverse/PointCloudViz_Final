using PointCloudViz.Core.Analysis;
using PointCloudViz.Core.Coloring;
using PointCloudViz.Core.Measurements;

namespace PointCloudViz.Core.Tests;

public class StatisticsTests
{
    [Fact]
    public void Compute_ReportsWorldStatistics()
    {
        var builder = new PointCloudBuilder();
        builder.Add(1000, 2000, 10, 1, default, 2);
        builder.Add(1002, 2004, 20, 3, default, 2);
        builder.Add(1004, 2008, 30, 5, default, 6);
        var stats = CloudStatistics.Compute(builder.Build());

        Assert.Equal(3, stats.Count);
        Assert.Equal(1000, stats.X.Min, 3);
        Assert.Equal(1004, stats.X.Max, 3);
        Assert.Equal(20, stats.Z.Mean, 3);
        Assert.Equal(10, stats.Z.StdDev, 3);
        Assert.Equal(3, stats.Intensity.Mean, 5);
        Assert.Equal(2, stats.ClassCounts[2]);
        Assert.Equal([(2, 2), (6, 1)], stats.PresentClasses.Select(c => ((int)c.Class, c.Count)));
        Assert.Equal(3, stats.ElevationHistogram.Total);
        Assert.Equal(3 / 32.0, stats.PlanarDensity, 6);
    }

    [Fact]
    public void Histogram_Quantile()
    {
        var pts = Enumerable.Range(0, 1000).Select(i => new PointRecord(new Vector3(0, 0, i))).ToArray();
        var h = Histogram.Build(pts, 100, p => p.Position.Z, 0, 1000);
        Assert.Equal(500, h.Quantile(0.5), 0);
        Assert.Equal(20, h.Quantile(0.02), 0);
        Assert.Equal(10, h.Peak);
    }

    [Fact]
    public void PlaneFit_RecoversNormalOfTiltedPlane()
    {
        var rng = new Random(1);
        var pts = Enumerable.Range(0, 200).Select(_ =>
        {
            double x = rng.NextDouble() * 10, y = rng.NextDouble() * 10;
            return new Double3(x, y, 0.5 * x + 3);
        }).ToList();
        var plane = FittedPlane.Fit(pts);
        var expected = new Double3(-0.5, 0, 1) / Math.Sqrt(1.25);
        Assert.Equal(expected.X, plane.Normal.X, 6);
        Assert.Equal(expected.Z, plane.Normal.Z, 6);
        Assert.True(plane.Rmse < 1e-9);
        Assert.Equal(Math.Atan(0.5) * 180 / Math.PI, plane.SlopeDegrees, 6);
    }
}

public class MeasurementTests
{
    [Fact]
    public void Distance_ComputesSlopeHorizontalAndHeight()
    {
        var m = new Measurement(1, MeasurementKind.Distance, [new Double3(0, 0, 0), new Double3(3, 4, 12)]);
        Assert.Equal(13, m.Length, 9);
        Assert.Equal(5, m.HorizontalLength, 9);
        Assert.Equal(12, m.HeightDifference, 9);
        Assert.Equal(240, m.SlopePercent, 9);
        Assert.True(m.IsComplete);
        Assert.Contains("斜距 13.000", m.Summary);
    }

    [Fact]
    public void Area_OfTiltedSquare_DiffersFromHorizontalArea()
    {
        // 与水平面成 60° 的 1×1 正方形：空间面积 1，水平投影 0.5
        double c = Math.Cos(Math.PI / 3), s = Math.Sin(Math.PI / 3);
        var m = new Measurement(1, MeasurementKind.Area,
            [new Double3(0, 0, 0), new Double3(1, 0, 0), new Double3(1, c, s), new Double3(0, c, s)]);
        Assert.Equal(1, m.Area, 9);
        Assert.Equal(0.5, m.HorizontalArea, 9);
        Assert.Equal(4, m.Length, 9);
    }

    [Fact]
    public void Area_OfConcavePolygon_KeepsClickOrder()
    {
        // L 形（面积 3）。旧版按极角重新排序顶点，会把凹多边形算错。
        var pts = new[] { (0, 0), (2, 0), (2, 1), (1, 1), (1, 2), (0, 2) }
            .Select(p => new Double3(670_000 + p.Item1, 4_860_000 + p.Item2, 100)).ToArray();
        var m = new Measurement(1, MeasurementKind.Area, pts);
        Assert.Equal(3, m.Area, 6);
        Assert.Equal(3, m.HorizontalArea, 6);
    }

    [Fact]
    public void Area_OfNearlyCoplanarPoints_IsStillComputed()
    {
        // 真实点云选点总有几厘米起伏，旧版会报"选点不共面"拒绝计算
        var m = new Measurement(1, MeasurementKind.Area,
            [new Double3(0, 0, 0.02), new Double3(10, 0, -0.03), new Double3(10, 10, 0.01), new Double3(0, 10, 0)]);
        Assert.Equal(100, m.HorizontalArea, 6);
        Assert.InRange(m.Area, 99.9, 100.1);
    }

    [Fact]
    public void Csv_ContainsHeaderAndRows()
    {
        var csv = MeasurementExport.ToCsv([
            new Measurement(1, MeasurementKind.Point, [new Double3(1, 2, 3)]),
            new Measurement(2, MeasurementKind.Distance, [new Double3(0, 0, 0), new Double3(1, 0, 0)]),
        ]);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("编号,类型", lines[0]);
        Assert.Contains("1.000 2.000 3.000", lines[1]);
        Assert.StartsWith("2,距离,2,1.000", lines[2]);
    }
}

public class ColoringTests
{
    [Fact]
    public void Palettes_HaveDistinctEndpoints()
    {
        foreach (var palette in Palettes.All)
        {
            Assert.NotEqual(palette.Sample(0), palette.Sample(1));
            Assert.Equal(palette.Sample(-5), palette.Sample(0));
            Assert.Equal(palette.Sample(float.NaN), palette.Sample(0));
        }
        Assert.Same(Palettes.Turbo, Palettes.Get("TURBO"));
        Assert.Same(Palettes.Viridis, Palettes.Get("unknown"));
    }

    [Fact]
    public void Elevation_UsesExplicitRange()
    {
        var cloud = TestData.Cloud(new Vector3(0, 0, 0), new Vector3(0, 0, 5), new Vector3(0, 0, 10));
        var colors = PointColorizer.Colorize(cloud, new ColorSettings
        {
            Mode = ColorMode.Elevation, PaletteId = "gray", RangeMin = 0, RangeMax = 10,
        });
        Assert.Equal(new Rgb24(0, 0, 0), colors[0]);
        Assert.Equal(new Rgb24(255, 255, 255), colors[2]);
        Assert.InRange(colors[1].R, 126, 129);
    }

    [Fact]
    public void AutoRange_IgnoresOutliers()
    {
        var pts = Enumerable.Range(0, 1000).Select(i => new Vector3(0, 0, i % 100 / 10f)).ToList();
        pts.Add(new Vector3(0, 0, 1000));
        var (min, max) = PointColorizer.AutoRange(TestData.Cloud(pts.ToArray()), ColorMode.Elevation);
        Assert.InRange(min, -0.1, 0.5);
        Assert.InRange(max, 9, 10.5);
    }

    [Fact]
    public void UnavailableMode_FallsBackToElevation()
    {
        var cloud = TestData.Cloud(new Vector3(0, 0, 0), new Vector3(0, 0, 1));
        Assert.False(PointColorizer.IsAvailable(cloud, ColorMode.Rgb));
        Assert.Equal(ColorMode.Elevation, PointColorizer.DefaultMode(cloud));
        var colors = PointColorizer.Colorize(cloud, new ColorSettings { Mode = ColorMode.Rgb });
        Assert.NotEqual(colors[0], colors[1]);
    }

    [Fact]
    public void Classification_UsesAsprsColors()
    {
        Assert.Equal("地面", ClassificationColors.GetName(2));
        Assert.Equal("车辆", ClassificationColors.GetName(64));
        Assert.StartsWith("自定义", ClassificationColors.GetName(200));
        Assert.NotEqual(ClassificationColors.GetColor(2), ClassificationColors.GetColor(6));
        Assert.NotEqual(ClassificationColors.GetColor(100), ClassificationColors.GetColor(101));
    }
}
