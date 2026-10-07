namespace PointCloudViz.Core.Tests;

internal static class TestData
{
    /// <summary>构造一个原点为零的点云（局部坐标即世界坐标）。</summary>
    public static PointCloud Cloud(params Vector3[] positions) =>
        new(positions.Select(p => new PointRecord(p)).ToArray(), Double3.Zero, PointAttributes.None);

    public static PointCloud RandomCloud(int count, int seed = 1, float extent = 10f, PointAttributes attributes = PointAttributes.None)
    {
        var rng = new Random(seed);
        var pts = new PointRecord[count];
        for (int i = 0; i < count; i++)
        {
            pts[i] = new PointRecord(
                new Vector3((float)rng.NextDouble() * extent, (float)rng.NextDouble() * extent, (float)rng.NextDouble() * extent),
                (float)rng.NextDouble(),
                new Rgb24((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)),
                (byte)rng.Next(1, 7));
        }
        return new PointCloud(pts, Double3.Zero, attributes);
    }

    /// <summary>大坐标点云（模拟 UTM 坐标）。</summary>
    public static PointCloud UtmCloud(int count = 1000, int seed = 7)
    {
        var rng = new Random(seed);
        var builder = new PointCloudBuilder(count);
        builder.AddAttributes(PointAttributes.Color | PointAttributes.Intensity | PointAttributes.Classification);
        for (int i = 0; i < count; i++)
        {
            builder.Add(
                670_000 + Math.Round(rng.NextDouble() * 100, 3),
                4_860_000 + Math.Round(rng.NextDouble() * 100, 3),
                220 + Math.Round(rng.NextDouble() * 10, 3),
                rng.Next(0, 65535),
                new Rgb24((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)),
                (byte)rng.Next(1, 7));
        }
        return builder.Build("utm");
    }

    public static string TempFile(string extension) =>
        Path.Combine(Path.GetTempPath(), $"pcv_test_{Guid.NewGuid():N}{extension}");
}
