using PointCloudViz.Core.Processing;
using PointCloudViz.Core.Spatial;

namespace PointCloudViz.Core.Tests;

public class KdTreeTests
{
    public static TheoryData<int, bool> Cases => new()
    {
        { 1, false }, { 13, false }, { 2_000, false }, { 20_000, false },
        { 5_000, true }, // 大量重复坐标的网格数据
    };

    private static Vector3[] MakePoints(int n, bool grid)
    {
        var rng = new Random(n);
        return Enumerable.Range(0, n).Select(_ => grid
                ? new Vector3(rng.Next(10), rng.Next(10), rng.Next(3))
                : new Vector3((float)rng.NextDouble() * 50, (float)rng.NextDouble() * 50, (float)rng.NextDouble() * 5))
            .ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RadiusSearch_MatchesBruteForce(int n, bool grid)
    {
        var pts = MakePoints(n, grid);
        var tree = new KdTree(pts);
        var rng = new Random(42);
        var results = new List<int>();
        for (int q = 0; q < 50; q++)
        {
            var center = pts[rng.Next(n)] + new Vector3(0.3f, -0.2f, 0.1f);
            float r = 0.5f + (float)rng.NextDouble() * 3;
            tree.RadiusSearch(center, r, results);
            var expected = Enumerable.Range(0, n).Where(i => Vector3.DistanceSquared(pts[i], center) <= r * r).OrderBy(i => i);
            Assert.Equal(expected, results.OrderBy(i => i));
            Assert.Equal(results.Count, tree.CountWithin(center, r));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void KNearest_MatchesBruteForce(int n, bool grid)
    {
        var pts = MakePoints(n, grid);
        var tree = new KdTree(pts);
        var rng = new Random(7);
        Span<int> idx = stackalloc int[8];
        Span<float> d2 = stackalloc float[8];
        for (int q = 0; q < 50; q++)
        {
            var center = new Vector3((float)rng.NextDouble() * 50, (float)rng.NextDouble() * 50, (float)rng.NextDouble() * 5);
            int found = tree.KNearest(center, idx, d2);
            var expected = pts.Select(p => Vector3.DistanceSquared(p, center)).OrderBy(d => d).Take(8).ToArray();
            Assert.Equal(Math.Min(8, n), found);
            for (int i = 0; i < found; i++)
            {
                Assert.Equal(expected[i], d2[i], 4);
                Assert.Equal(d2[i], Vector3.DistanceSquared(pts[idx[i]], center), 4);
            }
        }
    }

    [Fact]
    public void CountWithin_StopsEarly()
    {
        var pts = Enumerable.Repeat(Vector3.Zero, 1000).ToArray();
        var tree = new KdTree(pts);
        Assert.Equal(5, tree.CountWithin(Vector3.Zero, 1, stopAt: 5));
    }

    [Fact]
    public void EmptyTree_ReturnsNothing()
    {
        var tree = new KdTree(ReadOnlySpan<Vector3>.Empty);
        Assert.Equal(-1, tree.Nearest(Vector3.Zero, out _));
        Assert.Equal(0, tree.CountWithin(Vector3.Zero, 10));
    }
}

public class FilterTests
{
    [Fact]
    public void ElevationRange_UsesWorldCoordinates()
    {
        var cloud = TestData.UtmCloud(2000);
        var filtered = new ElevationRangeFilter(222, 225).Apply(cloud);
        Assert.InRange(filtered.Count, 1, cloud.Count - 1);
        foreach (ref readonly var p in filtered.Points)
            Assert.InRange(filtered.ToWorld(p.Position).Z, 221.999, 225.001);
        Assert.Equal(cloud.Origin, filtered.Origin);
    }

    [Fact]
    public void MaskFilter_ReturnsSameInstanceWhenNothingRemoved()
    {
        var cloud = TestData.RandomCloud(100);
        Assert.Same(cloud, new ElevationRangeFilter(-1000, 1000).Apply(cloud));
    }

    [Fact]
    public void Voxel_MergesPointsAndAveragesAttributes()
    {
        var pts = new[]
        {
            new PointRecord(new Vector3(0.1f, 0.1f, 0.1f), 1, new Rgb24(0, 0, 0)),
            new PointRecord(new Vector3(0.3f, 0.3f, 0.3f), 3, new Rgb24(200, 100, 50)),
            new PointRecord(new Vector3(5.5f, 5.5f, 5.5f), 7, new Rgb24(10, 10, 10)),
        };
        var cloud = new PointCloud(pts, Double3.Zero, PointAttributes.Color | PointAttributes.Intensity);
        var result = new VoxelGridFilter(1f).Apply(cloud);

        Assert.Equal(2, result.Count);
        var merged = result.Points.ToArray().Single(p => p.Position.X < 1);
        Assert.Equal(0.2f, merged.Position.X, 4);
        Assert.Equal(2f, merged.Intensity);
        Assert.Equal(new Rgb24(100, 50, 25), merged.Color);
    }

    [Fact]
    public void Voxel_DoesNotMergeCellsThatCollidedUnderOldHash()
    {
        // 旧版哈希 h = ((17*31 + x)*31 + y)*31 + z：(0,1,0) 与 (0,0,31)、(1,0,0) 与 (0,31,0) 结果相同。
        // 新版使用位编码，不同体素绝不会合并。
        var cloud = TestData.Cloud(new Vector3(0, 31, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 31), new Vector3(0, 1, 0));
        var result = new VoxelGridFilter(1f).Apply(cloud);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Voxel_RejectsTooSmallVoxel()
    {
        var cloud = TestData.Cloud(Vector3.Zero, new Vector3(1e6f, 0, 0));
        Assert.Throws<ArgumentException>(() => new VoxelGridFilter(0.1f).Apply(cloud));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelGridFilter(0));
    }

    [Fact]
    public void RadiusOutlier_RemovesIsolatedPoints()
    {
        var dense = Enumerable.Range(0, 200).Select(i => new Vector3(i % 10 * 0.1f, i / 10 * 0.1f, 0));
        var cloud = TestData.Cloud(dense.Append(new Vector3(50, 50, 50)).Append(new Vector3(-20, 0, 0)).ToArray());
        var result = new RadiusOutlierFilter(0.25f, 3).Apply(cloud);
        Assert.Equal(200, result.Count);
    }

    [Fact]
    public void StatisticalOutlier_RemovesFarPoints()
    {
        var rng = new Random(3);
        var pts = Enumerable.Range(0, 2000).Select(_ => new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble() * 0.1f)).ToList();
        pts.Add(new Vector3(10, 10, 10));
        pts.Add(new Vector3(-8, 3, 5));
        var cloud = TestData.Cloud(pts.ToArray());

        var result = new StatisticalOutlierFilter(8, 2.0).Apply(cloud);
        Assert.True(result.Count < cloud.Count);
        Assert.All(result.Points.ToArray(), p => Assert.True(p.Position.X <= 1.0001f && p.Position.X >= 0));
        Assert.True(result.Count > 1900, $"误删过多：{result.Count}");
    }

    [Fact]
    public void Classification_KeepsSelectedClasses()
    {
        var cloud = TestData.RandomCloud(1000, attributes: PointAttributes.Classification);
        var result = new ClassificationFilter([2, 6]).Apply(cloud);
        Assert.All(result.Points.ToArray(), p => Assert.Contains(p.Classification, new byte[] { 2, 6 }));
        Assert.Equal(cloud.Points.ToArray().Count(p => p.Classification is 2 or 6), result.Count);
    }

    [Fact]
    public void RandomSubsample_IsDeterministicAndExact()
    {
        var cloud = TestData.RandomCloud(10_000);
        var a = new RandomSubsampleFilter(1234).Apply(cloud);
        var b = new RandomSubsampleFilter(1234).Apply(cloud);
        Assert.Equal(1234, a.Count);
        Assert.Equal(a.Points.ToArray().Select(p => p.Position), b.Points.ToArray().Select(p => p.Position));

        var indices = RandomSubsampleFilter.SamplingIndices(100, 50);
        Assert.Equal(50, indices.Distinct().Count());
        Assert.True(indices.SequenceEqual(indices.Order()));
    }

    [Fact]
    public void CropBox_KeepsOrRemovesInsidePoints()
    {
        var cloud = TestData.Cloud(new Vector3(0, 0, 0), new Vector3(5, 5, 5), new Vector3(10, 10, 10));
        var inside = new CropBoxFilter(new Double3(1, 1, 1), new Double3(6, 6, 6)).Apply(cloud);
        var outside = new CropBoxFilter(new Double3(1, 1, 1), new Double3(6, 6, 6), invert: true).Apply(cloud);
        Assert.Equal(1, inside.Count);
        Assert.Equal(2, outside.Count);
    }

    [Fact]
    public void Filters_HonourCancellation()
    {
        var cloud = TestData.RandomCloud(50_000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new StatisticalOutlierFilter(8, 1).Apply(cloud, cancellationToken: cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => new VoxelGridFilter(0.5f).Apply(cloud, cancellationToken: cts.Token));
    }
}
