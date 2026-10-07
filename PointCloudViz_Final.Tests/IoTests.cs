using PointCloudViz_Final.IO;
using PointCloudViz_Final.Models;
using PointCloudViz_Final.Services;
using PointCloudViz_Final.Tools;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Media;
using Xunit;

namespace PointCloudViz_Final.Tests;

public sealed class IoTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PointCloudTests-" + Guid.NewGuid().ToString("N"));
    public IoTests() => Directory.CreateDirectory(_directory);
    private string FilePath(string name) => Path.Combine(_directory, name);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("1 2 3", 0, 255)]
    [InlineData("1 2 3 .5", .5f, 255)]
    [InlineData("1 2 3 10 20 30", 0, 10)]
    [InlineData("1 2 3 .5 10 20 30", .5f, 10)]
    [InlineData("1;2;3;.5;10;20;30", .5f, 10)]
    public async Task XyzColumnLayoutsAreUnambiguous(string row, float intensity, byte red)
    {
        var path = FilePath("input.xyz");
        await File.WriteAllTextAsync(path, "# sample\n\n" + row);
        var point = Assert.Single((await new XyzReader().ReadAsync(path, default)).Points);
        Assert.Equal(intensity, point.Intensity);
        Assert.Equal(red, point.Color.R);
    }

    [Theory]
    [InlineData("NaN 0 0")] [InlineData("0 Infinity 0")] [InlineData("1 2")]
    [InlineData("1 2 3 nope")] [InlineData("1 2 3 256 0 0")] [InlineData("1 2 3 4 5")]
    public async Task XyzRejectsCorruptionInsteadOfDroppingRows(string row)
    {
        var path = FilePath("bad.xyz");
        await File.WriteAllTextAsync(path, row);
        await Assert.ThrowsAsync<InvalidDataException>(() => new XyzReader().ReadAsync(path, default));
    }

    [Fact]
    public async Task XyzRoundTripPreservesRgbAndIntensityInAnyCulture()
    {
        var original = new PointCloud([new(1.125f, -2.875f, 3.5f, .42f, Color.FromRgb(12, 34, 56))]);
        var path = FilePath("roundtrip.xyz");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await XyzWriter.WriteAsync(original, path);
            var result = await new XyzReader().ReadAsync(path, default);
            Assert.Equal(original.Points[0], result.Points[0]);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public async Task CancelledExportPreservesExistingDestination()
    {
        var path = FilePath("cloud.xyz");
        await File.WriteAllTextAsync(path, "original");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            XyzWriter.WriteAsync(new PointCloud([new(1, 2, 3)]), path, new CancellationToken(true)));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task BinaryPlyExportRoundTripsAllAttributes()
    {
        var original = new PointCloud([new(1.125f, -2.875f, 3.5f, .42f, Color.FromRgb(12, 34, 56))]);
        string path = FilePath("export.ply");
        await PlyWriter.WriteAsync(original, path);
        var result = await new PlyReader().ReadAsync(path, default);
        Assert.Equal(original.Points[0], Assert.Single(result.Points));
    }

    [Fact]
    public async Task FailedAtomicWritePreservesDestinationAndCleansTemporary()
    {
        var path = FilePath("atomic.txt");
        await File.WriteAllTextAsync(path, "original");
        await Assert.ThrowsAsync<IOException>(() => AtomicFile.WriteAsync(path, _ => throw new IOException("disk failure")));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task PlySeparatesVertexAndFacePropertiesAndHonorsPropertyOrder()
    {
        var path = FilePath("faces.ply");
        await File.WriteAllTextAsync(path, """
            ply
            format ascii 1.0
            element vertex 3
            property uchar red
            property float z
            property float x
            property float y
            element face 1
            property list uchar int vertex_indices
            end_header
            10 3 1 2
            20 6 4 5
            30 9 7 8
            3 0 1 2
            """);
        var result = await new PlyReader().ReadAsync(path, default);
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result.Points[0].X);
        Assert.Equal(3, result.Points[0].Z);
        Assert.Equal(10, result.Points[0].Color.R);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BinaryPlySupportsBothByteOrders(bool bigEndian)
    {
        var path = FilePath("binary.ply");
        using (var stream = File.Create(path))
        {
            var header = Encoding.ASCII.GetBytes($"ply\nformat binary_{(bigEndian ? "big" : "little")}_endian 1.0\nelement vertex 1\nproperty double x\nproperty float y\nproperty short z\nproperty uchar red\nend_header\n");
            stream.Write(header);
            void Write(byte[] bytes) { if (bigEndian) Array.Reverse(bytes); stream.Write(bytes); }
            Write(BitConverter.GetBytes(1.25));
            Write(BitConverter.GetBytes(-2.5f));
            Write(BitConverter.GetBytes((short)3));
            stream.WriteByte(123);
        }
        var point = Assert.Single((await new PlyReader().ReadAsync(path, default)).Points);
        Assert.Equal(1.25f, point.X);
        Assert.Equal(-2.5f, point.Y);
        Assert.Equal(3, point.Z);
        Assert.Equal(123, point.Color.R);
    }

    [Theory]
    [InlineData("element vertex 2\nproperty float x\nproperty float y\nproperty float z\nend_header\n1 2 3\n")]
    [InlineData("element vertex 1\nproperty float x\nproperty float y\nend_header\n1 2\n")]
    [InlineData("element vertex 1\nproperty float x\nproperty float y\nproperty float z\nend_header\nNaN 2 3\n")]
    public async Task PlyRejectsMissingCoordinatesNonFiniteAndTruncatedData(string body)
    {
        var path = FilePath("invalid.ply");
        await File.WriteAllTextAsync(path, "ply\nformat ascii 1.0\n" + body);
        var error = await Record.ExceptionAsync(() => new PlyReader().ReadAsync(path, default));
        Assert.True(error is InvalidDataException or EndOfStreamException, error?.ToString());
    }

    private string CreateLas(byte minor, byte format, ushort? recordOverride = null)
    {
        var path = FilePath($"format-{format}.las");
        int header = minor == 4 ? 375 : 227;
        ushort recordLength = recordOverride ?? (ushort)(format switch { 0 => 20, 1 => 28, 2 => 26, 3 => 34, 6 => 30, 7 => 36, 8 => 38, _ => 20 });
        using var stream = File.Create(path);
        stream.SetLength(header + recordLength);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("LASF"));
        stream.Position = 24; writer.Write((byte)1); writer.Write(minor);
        stream.Position = 94; writer.Write((ushort)header); writer.Write((uint)header);
        writer.Write(0u); writer.Write(format); writer.Write(recordLength); writer.Write(minor == 4 ? 0u : 1u);
        stream.Position = 131;
        for (int i = 0; i < 3; i++) writer.Write(.01);
        writer.Write(100.0); writer.Write(200.0); writer.Write(300.0);
        if (minor == 4) { stream.Position = 247; writer.Write(1UL); }
        stream.Position = header;
        if (recordLength >= 20)
        {
            writer.Write(123); writer.Write(-456); writer.Write(789); writer.Write((ushort)32768);
            writer.Write((byte)0); writer.Write((byte)7); // Noise classification must not be silently removed.
        }
        int rgbOffset = format switch { 2 => 20, 3 => 28, 7 or 8 => 30, _ => -1 };
        if (rgbOffset >= 0 && recordLength >= rgbOffset + 6)
        {
            stream.Position = header + rgbOffset;
            writer.Write((ushort)65535); writer.Write((ushort)32768); writer.Write((ushort)0);
        }
        return path;
    }

    [Theory]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)] [InlineData(2, 3)]
    [InlineData(4, 6)] [InlineData(4, 7)] [InlineData(4, 8)]
    public async Task LasDecodesCommonFormatsAndDoesNotDiscardNoise(byte minor, byte format)
    {
        var cloud = await new StreamingLasReader().ReadAsync(CreateLas(minor, format), default);
        var point = Assert.Single(cloud.Points);
        Assert.Equal(101.23f, point.X, 3);
        Assert.Equal(195.44f, point.Y, 3);
        Assert.Equal(307.89f, point.Z, 3);
        Assert.InRange(point.Intensity, .49f, .51f);
        if (format is 2 or 3 or 7 or 8) Assert.Equal(Color.FromRgb(255, 128, 0), point.Color);
    }

    [Fact]
    public async Task LasRejectsShortRecordsAndTruncatedFiles()
    {
        var shortRecord = CreateLas(2, 3, 20);
        await Assert.ThrowsAsync<InvalidDataException>(() => new StreamingLasReader().ReadAsync(shortRecord, default));
        var truncated = CreateLas(2, 0);
        using (var stream = File.OpenWrite(truncated)) stream.SetLength(stream.Length - 1);
        await Assert.ThrowsAsync<EndOfStreamException>(() => new StreamingLasReader().ReadAsync(truncated, default));
    }

    [Fact]
    public async Task ReadersHonorPreCancelledTokens()
    {
        foreach (var reader in new IPointReader[] { new XyzReader(), new PlyReader(), new StreamingLasReader(), new BasicLasReader() })
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync("missing", new CancellationToken(true)));
    }

    [Fact]
    public async Task ProjectRoundTripResolvesRelativeDataAndRestoresMeasurements()
    {
        var settings = new ProjectSettings { Version = 2, DataFile = "cloud.xyz", ColorMap = "RGB", CameraYaw = 12,
            CameraPitch = 34, CameraDistance = 56, PointSize = 5,
            Measurements = [new() { Type = MeasurementType.Distance, Value = 5,
                Points = [Vector3.Zero, new(3, 4, 0)], Label = "Distance" }] };
        var path = FilePath("project.json");
        await ProjectIO.SaveAsync(path, settings);
        var restored = await ProjectIO.LoadAsync(path);
        Assert.Equal(FilePath("cloud.xyz"), restored.DataFile);
        Assert.Equal(56, restored.CameraDistance);
        Assert.Equal("RGB", restored.ColorMap);
        Assert.Equal(new Vector3(3, 4, 0), Assert.Single(restored.Measurements).Points[1]);
    }

    [Theory]
    [InlineData("{\"Version\":99}")] [InlineData("{\"PointSize\":0}")]
    [InlineData("{\"ColorMap\":\"Fake\"}")] [InlineData("{\"CameraDistance\":-1}")]
    public async Task InvalidProjectSettingsFailBeforeLoading(string json)
    {
        var path = FilePath("bad.json");
        await File.WriteAllTextAsync(path, json);
        await Assert.ThrowsAnyAsync<Exception>(() => ProjectIO.LoadAsync(path));
    }

    [Fact]
    public async Task SnapshotChecksumDetectsChangedCompanion()
    {
        var data = FilePath("data.xyz");
        await File.WriteAllTextAsync(data, "1 2 3");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(data)));
        var settings = new ProjectSettings { Version = 2, DataFile = data, DataSha256 = hash };
        await ProjectIO.VerifyDataAsync(settings);
        await File.WriteAllTextAsync(data, "4 5 6");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectIO.VerifyDataAsync(settings));
    }

    [Fact]
    public async Task MeasurementCsvIncludesVerticesAndInvariantNumbers()
    {
        var path = FilePath("measurements.csv");
        await MeasurementCsv.WriteAsync(path, [new() { Type = MeasurementType.Distance, Value = 1.5f,
            Points = [Vector3.Zero, new(1.5f, 0, 0)] }]);
        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(3, lines.Length);
        Assert.Equal("1,Distance,1.5,u,2,1.5,0,0", lines[2]);
    }
}
