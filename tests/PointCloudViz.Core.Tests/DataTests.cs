namespace PointCloudViz.Core.Tests;

public class DataTests
{
    [Fact]
    public void Rgb24_ParsesAndFormatsHex()
    {
        var c = Rgb24.FromHex("#1E1F24");
        Assert.Equal(new Rgb24(0x1E, 0x1F, 0x24), c);
        Assert.Equal("#1E1F24", c.ToHex());
        Assert.False(Rgb24.TryParseHex("#12", out _));
        Assert.Throws<FormatException>(() => Rgb24.FromHex("zzzzzz"));
    }

    [Fact]
    public void Builder_PreservesMillimetrePrecisionForUtmCoordinates()
    {
        // float 单独存 4,860,000.123 只能精确到 0.5 m；去中心化后应保持毫米级
        var builder = new PointCloudBuilder();
        builder.Add(670_000.123, 4_860_000.456, 220.789);
        builder.Add(670_100.001, 4_860_050.002, 230.003);
        var cloud = builder.Build();

        var w0 = cloud.ToWorld(cloud[0].Position);
        var w1 = cloud.ToWorld(cloud[1].Position);
        Assert.Equal(670_000.123, w0.X, 3);
        Assert.Equal(4_860_000.456, w0.Y, 3);
        Assert.Equal(220.789, w0.Z, 3);
        Assert.Equal(4_860_050.002, w1.Y, 3);
    }

    [Fact]
    public void Builder_RecentersCloudOnBoundingBox()
    {
        var builder = new PointCloudBuilder();
        builder.Add(10, 20, 30);
        builder.Add(20, 40, 50);
        var cloud = builder.Build();

        Assert.Equal(new Double3(15, 30, 40), cloud.Origin);
        Assert.Equal(new Vector3(-5, -10, -10), cloud.Bounds.Min);
        Assert.Equal(new Vector3(5, 10, 10), cloud.Bounds.Max);
        Assert.Equal(new Double3(10, 20, 30), cloud.WorldMin);
    }

    [Fact]
    public void Builder_DropsColorAttributeWhenAllColorsAreEmpty()
    {
        var builder = new PointCloudBuilder();
        builder.AddAttributes(PointAttributes.Color | PointAttributes.Intensity);
        builder.Add(0, 0, 0, 1f);
        var cloud = builder.Build();
        Assert.False(cloud.Has(PointAttributes.Color));
        Assert.True(cloud.Has(PointAttributes.Intensity));
    }

    [Fact]
    public void BoundingBox_EmptyAndInclude()
    {
        var box = BoundingBox.Empty;
        Assert.True(box.IsEmpty);
        box = box.Include(new Vector3(1, 2, 3)).Include(new Vector3(-1, 0, 5));
        Assert.False(box.IsEmpty);
        Assert.Equal(new Vector3(2, 2, 2), box.Size);
        Assert.True(box.Contains(new Vector3(0, 1, 4)));
        Assert.False(box.Contains(new Vector3(0, 1, 6)));
    }

    [Fact]
    public void Recentered_KeepsWorldCoordinates()
    {
        var cloud = new PointCloud([new PointRecord(new Vector3(100, 100, 100)), new PointRecord(new Vector3(102, 104, 106))],
            new Double3(1000, 2000, 3000), PointAttributes.None);
        var centered = cloud.Recentered();
        Assert.Equal(cloud.ToWorld(cloud[1].Position), centered.ToWorld(centered[1].Position));
        Assert.Equal(Vector3.Zero, centered.Bounds.Center);
    }
}
