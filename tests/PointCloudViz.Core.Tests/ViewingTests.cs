using PointCloudViz.Core.Coloring;
using PointCloudViz.Core.History;
using PointCloudViz.Core.Picking;
using PointCloudViz.Core.Project;
using PointCloudViz.Core.Rendering;
using PointCloudViz.Core.Synthetic;
using PointCloudViz.Core.Viewing;
using System.Buffers.Binary;
using System.IO.Compression;

namespace PointCloudViz.Core.Tests;

public class CameraTests
{
    [Fact]
    public void ScreenRay_PassesThroughProjectedPoint()
    {
        var cam = new OrbitCamera { Target = new Vector3(1, 2, 3), Distance = 20, Yaw = 33, Pitch = 25 };
        var p = new Vector3(4, -2, 5);
        Assert.True(cam.Project(p, 800, 600, out var screen, out var depth));
        var ray = cam.ScreenRay(screen.X, screen.Y, 800, 600);
        var closest = ray.PointAt(Vector3.Dot(p - ray.Origin, ray.Direction));
        Assert.True(Vector3.Distance(closest, p) < 1e-3f);
        Assert.True(depth > 0);
    }

    [Fact]
    public void Fit_MakesWholeBoundingBoxVisible()
    {
        var bounds = new BoundingBox(new Vector3(-50, -10, -2), new Vector3(50, 10, 8));
        var cam = new OrbitCamera();
        foreach (var preset in Enum.GetValues<ViewPreset>())
        {
            cam.SetPreset(preset);
            cam.Fit(bounds, 16 / 9f);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? bounds.Min.X : bounds.Max.X, (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                Assert.True(cam.Project(corner, 1600, 900, out var s, out _));
                Assert.InRange(s.X, 0, 1600);
                Assert.InRange(s.Y, 0, 900);
            }
        }
    }

    [Fact]
    public void TopView_LooksDownWithNorthUp()
    {
        var cam = new OrbitCamera();
        cam.SetPreset(ViewPreset.Top);
        Assert.True(cam.Forward.Z < -0.99f);
        Assert.True(cam.Up.Y > 0.99f);
        Assert.True(cam.Right.X > 0.99f);
    }

    [Fact]
    public void Pitch_IsClamped_AndOrbitKeepsZUp()
    {
        var cam = new OrbitCamera();
        cam.Orbit(0, 10_000);
        Assert.Equal(OrbitCamera.MaxPitch, cam.Pitch);
        cam.Orbit(1234, -300);
        Assert.True(cam.Up.Z > 0);
        Assert.InRange(cam.Yaw, -180, 180);
    }

    [Fact]
    public void ZoomTowards_KeepsFocusPointFixedOnScreen()
    {
        var cam = new OrbitCamera { Distance = 50 };
        var ray = cam.ScreenRay(600, 200, 800, 600);
        var n = cam.Forward;
        var focus = ray.PointAt(Vector3.Dot(cam.Target - ray.Origin, n) / Vector3.Dot(ray.Direction, n));
        cam.ZoomTowards(0.5f, ray);
        Assert.Equal(25, cam.Distance, 3);
        Assert.True(cam.Project(focus, 800, 600, out var s, out _));
        Assert.Equal(600, s.X, 1);
        Assert.Equal(200, s.Y, 1);
    }

    [Fact]
    public void Pan_MovesTargetInScreenPlane()
    {
        var cam = new OrbitCamera { Distance = 10 };
        var before = cam.Target;
        cam.Pan(100, 0, 600);
        var delta = cam.Target - before;
        Assert.True(Math.Abs(Vector3.Dot(delta, cam.Forward)) < 1e-4f);
        Assert.True(Vector3.Dot(delta, cam.Right) < 0);
    }
}

public class PickingTests
{
    [Fact]
    public void Pick_ReturnsFrontMostPointInsideCone()
    {
        var cloud = TestData.Cloud(new Vector3(0, 0, 10), new Vector3(0, 0, 5), new Vector3(0.01f, 0, 3), new Vector3(2, 0, 1));
        var ray = new Ray3(Vector3.Zero, Vector3.UnitZ);
        var hit = PointPicker.Pick(cloud, ray, 0.01f);
        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.Index);
        Assert.Equal(3, hit.Value.Depth, 4);
    }

    [Fact]
    public void Pick_ReturnsNullWhenNothingInCone()
    {
        var cloud = TestData.Cloud(new Vector3(5, 5, 5));
        Assert.Null(PointPicker.Pick(cloud, new Ray3(Vector3.Zero, -Vector3.UnitZ), 0.05f));
        Assert.Null(PointPicker.Pick(TestData.Cloud(), new Ray3(Vector3.Zero, Vector3.UnitZ), 0.05f));
    }

    [Fact]
    public void Pick_WorksOnLargeCloudAcrossChunks()
    {
        var cloud = TestData.RandomCloud(200_000, extent: 100);
        var target = cloud[123_456].Position;
        var origin = target + new Vector3(0, 0, 500);
        var hit = PointPicker.Pick(cloud, new Ray3(origin, target - origin), 1e-5f);
        Assert.NotNull(hit);
        Assert.True(Vector3.Distance(hit.Value.Position, target) < 0.05f || hit.Value.Position.Z > target.Z);
    }
}

public class RenderingTests
{
    [Fact]
    public void SoftwareRenderer_DrawsPointsAtCenter()
    {
        var cloud = TestData.RandomCloud(5000, extent: 1);
        cloud = cloud.Recentered();
        var cam = new OrbitCamera();
        cam.Fit(cloud.Bounds);
        var colors = Enumerable.Repeat(new Rgb24(255, 0, 0), cloud.Count).ToArray();
        var bg = new Rgb24(0, 0, 0);
        var image = SoftwareRenderer.Render(cloud, colors, cam, new RenderOptions { Width = 200, Height = 100, Background = bg, EyeDomeLighting = false });

        Assert.Equal(new Rgb24(255, 0, 0), image.GetPixel(100, 50));
        Assert.Equal(bg, image.GetPixel(0, 0));
    }

    [Fact]
    public void SoftwareRenderer_EdlDarkensButKeepsHue()
    {
        var cloud = StreetSceneGenerator.Generate(new StreetSceneOptions { Spacing = 0.5, TreeCount = 2, CarCount = 1, PoleCount = 1 });
        var cam = new OrbitCamera();
        cam.Fit(cloud.Bounds, 2);
        var colors = PointColorizer.Colorize(cloud, new ColorSettings { Mode = ColorMode.Uniform, UniformColor = new Rgb24(200, 200, 200) });
        var image = SoftwareRenderer.Render(cloud, colors, cam, new RenderOptions { Width = 160, Height = 80 });
        var lit = Enumerable.Range(0, 160 * 80).Select(i => image.GetPixel(i % 160, i / 160)).Where(c => c.R > 30).ToList();
        Assert.NotEmpty(lit);
        Assert.All(lit, c => Assert.True(c.R == c.G && c.G == c.B));
        Assert.Contains(lit, c => c.R < 200);
    }

    [Fact]
    public void PngEncoder_ProducesValidStream()
    {
        var image = new RgbaImage(7, 5);
        image.Fill(new Rgb24(10, 20, 30));
        image.SetPixel(3, 2, new Rgb24(255, 0, 0));
        var png = PngEncoder.Encode(image);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(5, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

        // 解出 IDAT 并还原第 2 行的红色像素
        int pos = 8;
        byte[]? idat = null;
        while (pos < png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT") idat = png.AsSpan(pos + 8, len).ToArray();
            pos += 12 + len;
        }
        Assert.NotNull(idat);
        using var z = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        Assert.Equal(5 * (1 + 7 * 4), raw.Length);
    }
}

public class HistoryTests
{
    [Fact]
    public void UndoRedo_WorksAndRespectsCapacity()
    {
        int value = 0;
        var stack = new UndoRedoStack(capacity: 3);
        int changes = 0;
        stack.Changed += (_, _) => changes++;

        for (int i = 1; i <= 5; i++)
        {
            int v = i, prev = value;
            stack.Execute(new DelegateCommand($"set {v}", () => value = v, () => value = prev));
        }
        Assert.Equal(5, value);
        Assert.Equal(3, stack.UndoCount);
        Assert.Equal("set 5", stack.UndoDescription);

        Assert.True(stack.Undo());
        Assert.True(stack.Undo());
        Assert.True(stack.Undo());
        Assert.False(stack.Undo());
        Assert.Equal(2, value);
        Assert.Equal("set 3", stack.RedoDescription);

        Assert.True(stack.Redo());
        Assert.Equal(3, value);
        stack.Execute(new DelegateCommand("x", () => value = 99, () => value = 3));
        Assert.False(stack.CanRedo);
        Assert.Equal(10, changes);
    }
}

public class ProjectTests
{
    [Fact]
    public void RoundTrip_UsesRelativeDataPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pcv_proj_" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(dir, "a", "demo.pcvproj");
        var data = Path.Combine(dir, "data", "cloud.las");
        var doc = new ProjectDocument
        {
            DataFile = data,
            Display = new DisplaySettings { ColorMode = ColorMode.Classification, Palette = "turbo", PointSize = 4 },
            Camera = new CameraState([1, 2, 3], 10, 20, 30),
            Measurements = [new MeasurementRecord(Measurements.MeasurementKind.Distance, [[0, 0, 0], [1, 1, 1]])],
        };

        var json = ProjectSerializer.Serialize(doc, project);
        Assert.Contains("\"dataFile\": \"../data/cloud.las\"".Replace('/', Path.DirectorySeparatorChar).Replace("\\", "\\\\"), json);
        Assert.Contains("\"Classification\"", json);

        var loaded = ProjectSerializer.Deserialize(json, project);
        Assert.Equal(Path.GetFullPath(data), loaded.DataFile);
        Assert.Equal(ColorMode.Classification, loaded.Display.ColorMode);
        Assert.Equal(4, loaded.Display.PointSize);
        Assert.Equal(20, loaded.Camera!.Pitch);
        var m = loaded.Measurements.Single().ToMeasurement(1);
        Assert.Equal(Math.Sqrt(3), m.Length, 9);
    }

    [Fact]
    public void Deserialize_MigratesVersion1Files()
    {
        const string v1 = """
        { "DataFile": "C:\\data\\a.ply", "ColorMap": "Intensity", "PointSize": 5, "Background": "White",
          "CameraYaw": 0, "CameraPitch": 0, "CameraDistance": 0 }
        """;
        var doc = ProjectSerializer.Deserialize(v1);
        Assert.Equal(2, doc.FormatVersion);
        Assert.Equal(ColorMode.Intensity, doc.Display.ColorMode);
        Assert.Equal(5, doc.Display.PointSize);
        Assert.Equal("#FFFFFF", doc.Display.Background);
        Assert.Equal("rainbow", doc.Display.Palette);
        Assert.Null(doc.Camera);
    }
}

public class SyntheticTests
{
    [Fact]
    public void Generator_IsDeterministicAndClassified()
    {
        var options = new StreetSceneOptions { Spacing = 0.3, RoadLength = 30 };
        var a = StreetSceneGenerator.Generate(options);
        var b = StreetSceneGenerator.Generate(options);

        Assert.Equal(a.Count, b.Count);
        Assert.Equal(a.Origin, b.Origin);
        Assert.True(a.Has(PointAttributes.Color | PointAttributes.Intensity | PointAttributes.Classification));
        var classes = a.Points.ToArray().Select(p => p.Classification).Distinct().ToHashSet();
        Assert.Superset(new HashSet<byte> { 2, 5, 6, 11, StreetSceneGenerator.ClassVehicle, StreetSceneGenerator.ClassPole }, classes);
        Assert.InRange(a.WorldMin.X, 669_900, 670_000);
        Assert.InRange(a.WorldMin.Y, 4_859_990, 4_860_000);
    }

    [Fact]
    public void Generator_ReportsProgressAndCancels()
    {
        var reports = new List<double>();
        StreetSceneGenerator.Generate(new StreetSceneOptions { Spacing = 0.5 }, new SyncProgress(reports.Add));
        Assert.Equal(1.0, reports[^1]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => StreetSceneGenerator.Generate(cancellationToken: cts.Token));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
