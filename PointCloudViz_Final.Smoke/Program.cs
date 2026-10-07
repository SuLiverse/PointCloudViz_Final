using PointCloudViz_Final;
using PointCloudViz_Final.Filters;
using PointCloudViz_Final.Services;
using PointCloudViz_Final.Tools;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    private static int _exitCode;
    private static readonly List<object> Results = new();

    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/smoke");
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("POINTCLOUD_STUDIO_HOME", Path.Combine(output, "state"));
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow { SuppressDialogs = true };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.Exception);
            _exitCode = 1;
            e.Handled = true;
            window.CloseAfterSmokeTest();
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (window.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(100);
                Require(!window.IsBusy && window.LastError == null, "Initial scene loads", window.LastError);
                Require(window.CurrentCloud?.Count > 50_000, "Real demo has more than 50,000 points");
                await Frame();
                SaveWindow(window, Path.Combine(output, "studio-desktop.png"));
                var first = window.CaptureViewport();
                Save(first, Path.Combine(output, "viewport-height.png"));
                Require(ColoredPixels(first) > 1000, "GPU viewport contains colored point pixels");

                var viewport = (Viewport3DX)window.FindName("Viewport");
                var cloudBounds = window.CurrentCloud!.BBox;
                var highest = window.CurrentCloud.Points.MaxBy(p => p.Z);
                var renderPoint = new SharpDX.Vector3(highest.X - (cloudBounds.MinX + cloudBounds.MaxX) / 2,
                    highest.Y - (cloudBounds.MinY + cloudBounds.MaxY) / 2, highest.Z - (cloudBounds.MinZ + cloudBounds.MaxZ) / 2);
                var screenPoint = viewport.Project(renderPoint);
                window.Measurements.Mode = MeasurementMode.Distance;
                window.Measurements.IsActive = true;
                window.Pick(new Point(screenPoint.X, screenPoint.Y));
                Require(window.Measurements.SelectedPoints.Count == 1, "Screen picking matches the real renderer camera");
                window.Measurements.ClearAll();
                window.Measurements.IsActive = false;

                var color = (ComboBox)window.FindName("ColorMode");
                color.SelectedIndex = 1;
                await Frame();
                var rgb = window.CaptureViewport();
                Require(Hash(first) != Hash(rgb), "RGB changes rendered pixels");
                Save(rgb, Path.Combine(output, "viewport-rgb.png"));
                SaveWindow(window, Path.Combine(output, "studio-rgb.png"));

                var preset = (ComboBox)window.FindName("ViewPreset");
                preset.SelectedIndex = 1;
                await Frame();
                Require(Hash(rgb) != Hash(window.CaptureViewport()), "Top view changes camera pixels");
                preset.SelectedIndex = 0;
                color.SelectedIndex = 0;

                int originalCount = window.CurrentCloud!.Count;
                await window.ApplyFilterAsync(new VoxelGridFilter(.5f));
                int filteredCount = window.CurrentCloud!.Count;
                Require(filteredCount > 0 && filteredCount < originalCount, "Voxel changes actual data");
                Click(window, "UndoButton");
                Require(window.CurrentCloud.Count == originalCount, "Undo restores full dataset");
                Click(window, "RedoButton");
                Require(window.CurrentCloud.Count == filteredCount, "Redo restores filtered dataset");

                var beforeFailure = window.CurrentCloud;
                var corrupt = Path.Combine(output, "corrupt.xyz");
                await File.WriteAllTextAsync(corrupt, "NaN 0 0");
                await window.LoadPathAsync(corrupt);
                Require(window.LastError != null && ReferenceEquals(beforeFailure, window.CurrentCloud), "Failed import preserves active data");
                Require(((Button)window.FindName("UndoButton")).IsEnabled, "Failed import preserves undo history");

                window.Measurements.Mode = MeasurementMode.Distance;
                window.Measurements.IsActive = true;
                window.Measurements.AddPoint(new(0, 0, 0));
                window.Measurements.AddPoint(new(3, 4, 0));
                Require(window.Measurements.Measurements.Single().Value == 5, "Measurement creates 3D distance");
                color.SelectedIndex = 2;
                ((Slider)window.FindName("PointSizeSlider")).Value = 5;
                var projectPath = Path.Combine(output, "smoke-project.json");
                await window.RunOperationAsync("snapshot", token => window.SaveSnapshotAsync(projectPath, token));
                Require(window.LastError == null && File.Exists(projectPath), "Snapshot saved");
                var settings = await ProjectIO.LoadAsync(projectPath);
                Require(File.Exists(settings.DataFile), "Snapshot data companion exists");
                await window.LoadPathAsync(projectPath);
                Require(window.LastError == null && window.CurrentCloud!.Count == filteredCount, "Project reload restores processed data");
                Require(window.Measurements.Measurements.Count == 1, "Project reload restores measurements");
                Require(color.SelectedIndex == 2 && ((Slider)window.FindName("PointSizeSlider")).Value == 5, "Project restores color and point size");
                var camera = (PerspectiveCamera)window.FindName("SceneCamera");
                double distance = camera.LookDirection.Length;
                Require(Math.Abs(distance - settings.CameraDistance) < .01, "Project reload restores camera");
                Require(!((Button)window.FindName("UndoButton")).IsEnabled, "Loading another document clears undo history");

                window.Measurements.ClearAll();
                await window.ApplyFilterAsync(new RangeFilter(10000, 10001));
                Require(window.CurrentCloud!.Count == 0, "Empty filter result is represented");
                var geometry = ((PointGeometryModel3D)window.FindName("PointModel")).Geometry as PointGeometry3D;
                Require(geometry?.Positions.Count == 0, "Empty result clears old GPU geometry");
                Click(window, "UndoButton");
                Require(window.CurrentCloud.Count == filteredCount, "Empty result can be undone");

                var beforeCancellation = window.CurrentCloud;
                await window.RunOperationAsync("cancel", token =>
                {
                    ((Button)((StackPanel)((Border)((Border)window.FindName("LoadingOverlay")).Child).Child).Children[2])
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });
                Require(ReferenceEquals(beforeCancellation, window.CurrentCloud), "Cancellation preserves current dataset");
                color.SelectedIndex = 0;
                ((Slider)window.FindName("PointSizeSlider")).Value = 2;
                await window.LoadDemoAsync();
                window.Width = 1024; window.Height = 700;
                await Frame();
                window.FitScene();
                await Frame();
                SaveWindow(window, Path.Combine(output, "studio-compact.png"));
                Require(ColoredPixels(window.CaptureViewport()) > 500, "Compact viewport remains nonblank");
                Require(((FrameworkElement)window.FindName("ViewportHost")).ActualWidth >= 500, "Compact layout preserves viewport width");
                Require(((FrameworkElement)window.FindName("ColorMode")).ActualWidth >= 180, "Inspector remains usable");
                window.Width = 1440; window.Height = 900;
                await Frame();
                window.FitScene();
                await Frame();
                SaveWindow(window, Path.Combine(output, "studio-desktop.png"));
                Save(window.CaptureViewport(), Path.Combine(output, "viewport-height.png"));
            }
            catch (Exception ex)
            {
                _exitCode = 1;
                Results.Add(new { name = "Unhandled smoke failure", passed = false, detail = ex.ToString() });
                Console.Error.WriteLine(ex);
            }
            finally
            {
                await File.WriteAllTextAsync(Path.Combine(output, "results.json"),
                    JsonSerializer.Serialize(new { passed = _exitCode == 0, checks = Results }, new JsonSerializerOptions { WriteIndented = true }));
                window.CloseAfterSmokeTest();
            }
        };
        app.Run(window);
        return _exitCode;
    }

    private static Task Frame() => Task.Delay(750);
    private static void Click(MainWindow window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string name, string? detail = null)
    {
        Results.Add(new { name, passed = condition, detail });
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
        if (!condition) throw new InvalidOperationException(name + ": " + detail);
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
    private static int ColoredPixels(BitmapSource bitmap)
    {
        var pixels = Pixels(bitmap);
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int min = Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
            int max = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
            if (max - min > 35 && max > 80) count++;
        }
        return count;
    }
    private static string Hash(BitmapSource bitmap) => Convert.ToHexString(SHA256.HashData(Pixels(bitmap)));
    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private static void SaveWindow(MainWindow window, string path)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, path);
    }
}
