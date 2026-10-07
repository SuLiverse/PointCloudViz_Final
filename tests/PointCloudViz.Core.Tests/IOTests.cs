using System.Buffers.Binary;
using System.Text;
using PointCloudViz.Core.IO;

namespace PointCloudViz.Core.Tests;

public class XyzTests
{
    private static PointCloud ReadText(string text, PointCloudReadOptions? options = null) =>
        new XyzReader().Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), options);

    [Fact]
    public void Reads_ThreeColumns_WithHeaderAndComments()
    {
        var cloud = ReadText("X Y Z\n# comment\n1 2 3\n\n4,5,6\n");
        Assert.Equal(2, cloud.Count);
        Assert.Equal(PointAttributes.None, cloud.Attributes);
        Assert.Equal(new Double3(4, 5, 6), cloud.ToWorld(cloud[1].Position));
    }

    [Fact]
    public void Reads_FourColumns_AsIntensity()
    {
        var cloud = ReadText("1 2 3 0.5\n4 5 6 0.25\n");
        Assert.True(cloud.Has(PointAttributes.Intensity));
        Assert.False(cloud.Has(PointAttributes.Color));
        Assert.Equal(0.25f, cloud[1].Intensity);
    }

    [Fact]
    public void Reads_SixColumns_AsRgbNotIntensity()
    {
        // 旧版会把第 4 列（R）误当作强度
        var cloud = ReadText("1 2 3 10 20 30\n");
        Assert.True(cloud.Has(PointAttributes.Color));
        Assert.False(cloud.Has(PointAttributes.Intensity));
        Assert.Equal(new Rgb24(10, 20, 30), cloud[0].Color);
    }

    [Fact]
    public void Reads_SevenColumns_AsIntensityAndRgb()
    {
        var cloud = ReadText("1;2;3;0.7;255;128;0\n");
        Assert.Equal(0.7f, cloud[0].Intensity, 5);
        Assert.Equal(new Rgb24(255, 128, 0), cloud[0].Color);
    }

    [Fact]
    public void MaxPoints_DecimatesInput()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 10_000; i++) sb.Append(i).Append(" 0 0\n");
        var cloud = ReadText(sb.ToString(), new PointCloudReadOptions { MaxPoints = 1000 });
        Assert.InRange(cloud.Count, 500, 1000);
    }

    [Fact]
    public void Writer_RoundTripsWorldCoordinates()
    {
        var original = TestData.UtmCloud(200);
        var ms = new MemoryStream();
        new XyzWriter().Write(original, ms);
        ms.Position = 0;
        var loaded = new XyzReader().Read(ms);

        Assert.Equal(original.Count, loaded.Count);
        for (int i = 0; i < original.Count; i++)
        {
            var a = original.ToWorld(original[i].Position);
            var b = loaded.ToWorld(loaded[i].Position);
            Assert.True(Double3.Distance(a, b) < 2e-3, $"点 {i} 偏差过大：{a} vs {b}");
            Assert.Equal(original[i].Color, loaded[i].Color);
        }
    }
}

public class PlyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writer_RoundTrips(bool ascii)
    {
        var original = TestData.UtmCloud(500);
        var ms = new MemoryStream();
        new PlyWriter(ascii).Write(original, ms);
        ms.Position = 0;
        var loaded = new PlyReader().Read(ms);

        Assert.Equal(original.Count, loaded.Count);
        Assert.Equal(original.Attributes, loaded.Attributes);
        for (int i = 0; i < original.Count; i += 37)
        {
            Assert.True(Double3.Distance(original.ToWorld(original[i].Position), loaded.ToWorld(loaded[i].Position)) < 1e-3);
            Assert.Equal(original[i].Color, loaded[i].Color);
            Assert.Equal(original[i].Classification, loaded[i].Classification);
            Assert.Equal(original[i].Intensity, loaded[i].Intensity);
        }
    }

    [Fact]
    public void Reads_AsciiSampleShippedWithRepository()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "sample_final.ply");
        var cloud = PointCloudIO.Load(path);
        Assert.Equal(4000, cloud.Count);
        Assert.True(cloud.Has(PointAttributes.Color));
    }

    [Fact]
    public void Reads_BigEndianBinary_WithFloatAndUShortColors()
    {
        var header = "ply\nformat binary_big_endian 1.0\nelement vertex 2\n" +
                     "property float x\nproperty float y\nproperty float z\n" +
                     "property ushort red\nproperty ushort green\nproperty ushort blue\n" +
                     "element face 1\nproperty list uchar int vertex_indices\nend_header\n";
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(header));
        Span<byte> b = stackalloc byte[4];
        foreach (var (x, y, z, c) in new[] { (1f, 2f, 3f, (ushort)65535), (4f, 5f, 6f, (ushort)0) })
        {
            foreach (var v in new[] { x, y, z }) { BinaryPrimitives.WriteSingleBigEndian(b, v); ms.Write(b); }
            for (int k = 0; k < 3; k++) { BinaryPrimitives.WriteUInt16BigEndian(b, c); ms.Write(b[..2]); }
        }
        ms.Position = 0;

        var cloud = new PlyReader().Read(ms);
        Assert.Equal(2, cloud.Count);
        Assert.Equal(new Double3(4, 5, 6), cloud.ToWorld(cloud[1].Position));
        Assert.Equal(new Rgb24(255, 255, 255), cloud[0].Color);
    }

    [Fact]
    public void Rejects_InvalidMagic()
    {
        var ms = new MemoryStream(Encoding.ASCII.GetBytes("plx\nformat ascii 1.0\nend_header\n"));
        Assert.Throws<InvalidDataException>(() => new PlyReader().Read(ms));
    }
}

public class LasTests
{
    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)7)]
    public void Writer_RoundTrips(byte pointFormat)
    {
        var original = TestData.UtmCloud(3000);
        var ms = new MemoryStream();
        new LasWriter(pointFormat).Write(original, ms);
        ms.Position = 0;
        var header = LasHeader.Read(ms);
        Assert.Equal(pointFormat, header.PointFormat);
        Assert.Equal(pointFormat == 7 ? "1.4" : "1.2", header.Version);
        Assert.Equal((ulong)original.Count, header.PointCount);

        ms.Position = 0;
        var loaded = new LasReader().Read(ms);
        Assert.Equal(original.Count, loaded.Count);
        Assert.True(loaded.Has(PointAttributes.Color | PointAttributes.Classification | PointAttributes.Intensity));
        for (int i = 0; i < original.Count; i += 101)
        {
            var a = original.ToWorld(original[i].Position);
            var b = loaded.ToWorld(loaded[i].Position);
            Assert.True(Double3.Distance(a, b) < 1.5e-3, $"{a} vs {b}");
            Assert.Equal(original[i].Color, loaded[i].Color);
            Assert.Equal(original[i].Classification, loaded[i].Classification);
            Assert.Equal(original[i].Intensity, loaded[i].Intensity);
        }
    }

    [Fact]
    public void Detects_EightBitColorsStoredInSixteenBitFields()
    {
        var cloud = new PointCloud([new PointRecord(Vector3.Zero, 0, new Rgb24(200, 100, 50)), new PointRecord(Vector3.One)],
            Double3.Zero, PointAttributes.Color);
        var ms = new MemoryStream();
        new LasWriter().Write(cloud, ms);
        var bytes = ms.ToArray();
        // 改写第一条记录的颜色为 8 位数值（部分软件的非标准写法）
        int record = LasHeader.Size12;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(record + 20), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(record + 22), 100);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(record + 24), 50);

        var loaded = new LasReader().Read(new MemoryStream(bytes));
        Assert.Equal(new Rgb24(200, 100, 50), loaded[0].Color);
    }

    [Fact]
    public void MaxPoints_DecimatesUsingKnownCount()
    {
        var original = TestData.RandomCloud(10_000);
        var ms = new MemoryStream();
        new LasWriter().Write(original, ms);
        ms.Position = 0;
        var loaded = new LasReader().Read(ms, new PointCloudReadOptions { MaxPoints = 2500 });
        Assert.Equal(2500, loaded.Count);
    }

    [Fact]
    public void Rejects_LazAndBadSignature()
    {
        var ms = new MemoryStream();
        new LasWriter().Write(TestData.RandomCloud(10), ms);
        var bytes = ms.ToArray();
        bytes[104] |= 0x80; // LASzip 压缩标志
        Assert.Throws<NotSupportedException>(() => new LasReader().Read(new MemoryStream(bytes)));

        bytes[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => new LasReader().Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void PointCloudIO_SavesAndLoadsByExtension()
    {
        var cloud = TestData.UtmCloud(100);
        foreach (var ext in new[] { ".las", ".ply", ".xyz" })
        {
            var path = TestData.TempFile(ext);
            try
            {
                PointCloudIO.Save(cloud, path);
                var loaded = PointCloudIO.Load(path);
                Assert.Equal(cloud.Count, loaded.Count);
                Assert.Equal(Path.GetFileName(path), loaded.Name);
                Assert.False(File.Exists(path + ".tmp"));
            }
            finally
            {
                File.Delete(path);
            }
        }
        Assert.Throws<NotSupportedException>(() => PointCloudIO.Load("cloud.e57"));
    }

    [Fact]
    public void OpenFileFilter_ListsAllFormats()
    {
        var filter = PointCloudIO.OpenFileFilter;
        Assert.Contains("*.las", filter);
        Assert.Contains("*.ply", filter);
        Assert.Contains("*.xyz", filter);
        Assert.Equal(0, filter.Split('|').Length % 2);
    }
}
