using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using PointCloudViz.Core.Analysis;
using PointCloudViz.Core.Coloring;
using PointCloudViz.Core.Data;
using PointCloudViz.Core.IO;
using PointCloudViz.Core.Processing;
using PointCloudViz.Core.Rendering;
using PointCloudViz.Core.Synthetic;
using PointCloudViz.Core.Viewing;

namespace PointCloudViz.Cli;

/// <summary>
/// pcv —— PointCloudViz 命令行工具。跨平台，可用于批处理、格式转换和生成预览图。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        var root = new RootCommand("PointCloudViz 命令行工具：查看信息、格式转换与滤波、离屏渲染、生成合成数据。")
        {
            InfoCommand(),
            ConvertCommand(),
            RenderCommand(),
            GenerateCommand(),
        };
        return root.Parse(args).Invoke();
    }

    private static Command InfoCommand()
    {
        var input = new Argument<FileInfo>("input") { Description = "点云文件（.las/.ply/.xyz/.txt 等）" };
        var command = new Command("info", "显示点云的头信息与统计量") { input };
        command.SetAction(parse => Run(() =>
        {
            var file = parse.GetRequiredValue(input);
            if (file.Extension.Equals(".las", StringComparison.OrdinalIgnoreCase))
            {
                var h = LasReader.ReadHeader(file.FullName);
                Console.WriteLine($"LAS 版本      {h.Version}，点格式 {h.PointFormat}，记录长度 {h.PointRecordLength} B");
                Console.WriteLine($"生成软件      {h.GeneratingSoftware}");
                Console.WriteLine($"比例/偏移     {h.Scale} / {h.Offset}");
            }

            var cloud = Load(file.FullName, null);
            var stats = CloudStatistics.Compute(cloud);
            Console.WriteLine($"点数          {cloud.Count:N0}");
            Console.WriteLine($"属性          {cloud.Attributes}");
            Console.WriteLine($"X 范围        {stats.X.Min:F3} ~ {stats.X.Max:F3}");
            Console.WriteLine($"Y 范围        {stats.Y.Min:F3} ~ {stats.Y.Max:F3}");
            Console.WriteLine($"Z 范围        {stats.Z.Min:F3} ~ {stats.Z.Max:F3}（均值 {stats.Z.Mean:F3}，标准差 {stats.Z.StdDev:F3}）");
            Console.WriteLine($"平面密度      {stats.PlanarDensity:F2} 点/m²");
            if (cloud.Has(PointAttributes.Intensity))
                Console.WriteLine($"强度          {stats.Intensity.Min:G6} ~ {stats.Intensity.Max:G6}");
            if (cloud.Has(PointAttributes.Classification))
            {
                Console.WriteLine("分类统计");
                foreach (var (cls, count) in stats.PresentClasses)
                    Console.WriteLine($"  {cls,3} {ClassificationColors.GetName(cls),-10} {count,12:N0}  {count * 100.0 / cloud.Count,6:F2}%");
            }
        }));
        return command;
    }

    private static Command ConvertCommand()
    {
        var input = new Argument<FileInfo>("input") { Description = "输入文件" };
        var output = new Argument<FileInfo>("output") { Description = "输出文件，格式由扩展名决定（.las/.ply/.xyz）" };
        var zMin = new Option<double?>("--z-min") { Description = "保留高程 ≥ 该值的点" };
        var zMax = new Option<double?>("--z-max") { Description = "保留高程 ≤ 该值的点" };
        var classes = new Option<string?>("--classes") { Description = "仅保留指定分类，逗号分隔，如 2,6" };
        var sor = new Option<int?>("--sor") { Description = "统计离群点剔除的近邻数 k（如 16）" };
        var sorStd = new Option<double>("--sor-std") { Description = "统计离群点剔除的标准差倍数", DefaultValueFactory = _ => 2.0 };
        var ror = new Option<float?>("--ror-radius") { Description = "半径离群点剔除的半径" };
        var rorMin = new Option<int>("--ror-min") { Description = "半径离群点剔除的最少邻居数", DefaultValueFactory = _ => 4 };
        var voxel = new Option<float?>("--voxel") { Description = "体素下采样尺寸（米）" };
        var max = new Option<int?>("--max-points") { Description = "读取时最多保留的点数" };

        var command = new Command("convert", "格式转换，可按顺序串联滤波：高程 → 分类 → SOR → ROR → 体素")
        {
            input, output, zMin, zMax, classes, sor, sorStd, ror, rorMin, voxel, max,
        };
        command.SetAction(parse => Run(() =>
        {
            var cloud = Load(parse.GetRequiredValue(input).FullName, parse.GetValue(max));
            var filters = new List<IPointFilter>();
            if (parse.GetValue(zMin) is not null || parse.GetValue(zMax) is not null)
                filters.Add(new ElevationRangeFilter(parse.GetValue(zMin) ?? double.MinValue, parse.GetValue(zMax) ?? double.MaxValue));
            if (parse.GetValue(classes) is { } list)
                filters.Add(new ClassificationFilter(list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(byte.Parse)));
            if (parse.GetValue(sor) is int k)
                filters.Add(new StatisticalOutlierFilter(k, parse.GetValue(sorStd)));
            if (parse.GetValue(ror) is float r)
                filters.Add(new RadiusOutlierFilter(r, parse.GetValue(rorMin)));
            if (parse.GetValue(voxel) is float v)
                filters.Add(new VoxelGridFilter(v));

            foreach (var filter in filters)
            {
                int before = cloud.Count;
                cloud = Timed(filter.Description, p => filter.Apply(cloud, p));
                Console.WriteLine($"  {before:N0} → {cloud.Count:N0} 点");
            }

            var path = parse.GetRequiredValue(output).FullName;
            Timed($"写出 {Path.GetFileName(path)}", p => { PointCloudIO.Save(cloud, path, p); return 0; });
        }));
        return command;
    }

    private static Command RenderCommand()
    {
        var input = new Argument<FileInfo>("input") { Description = "输入文件" };
        var output = new Option<FileInfo>("--output", "-o") { Description = "输出 PNG 路径", Required = true };
        var width = new Option<int>("--width") { DefaultValueFactory = _ => 1600, Description = "图像宽度" };
        var height = new Option<int>("--height") { DefaultValueFactory = _ => 900, Description = "图像高度" };
        var mode = new Option<ColorMode?>("--color", "-c") { Description = "着色模式：Rgb/Elevation/Intensity/Classification/Uniform（默认自动）" };
        var palette = new Option<string>("--palette") { DefaultValueFactory = _ => "viridis", Description = $"色带：{string.Join("/", Palettes.All.Select(p => p.Id))}" };
        var view = new Option<ViewPreset>("--view") { DefaultValueFactory = _ => ViewPreset.Isometric, Description = "视角：Top/Front/Back/Left/Right/Isometric" };
        var yaw = new Option<float?>("--yaw") { Description = "自定义方位角（度），覆盖 --view" };
        var pitch = new Option<float?>("--pitch") { Description = "自定义仰角（度）" };
        var zoom = new Option<float>("--zoom") { DefaultValueFactory = _ => 1f, Description = "缩放倍数（>1 拉近）" };
        var size = new Option<int>("--point-size") { DefaultValueFactory = _ => 2, Description = "点大小（像素）" };
        var background = new Option<string>("--background") { DefaultValueFactory = _ => "#18191E", Description = "背景色 #RRGGBB" };
        var noEdl = new Option<bool>("--no-edl") { Description = "关闭 Eye-Dome Lighting" };

        var command = new Command("render", "离屏渲染点云为 PNG（无需显卡）")
        {
            input, output, width, height, mode, palette, view, yaw, pitch, zoom, size, background, noEdl,
        };
        command.SetAction(parse => Run(() =>
        {
            var cloud = Load(parse.GetRequiredValue(input).FullName, null);
            var settings = new ColorSettings
            {
                Mode = parse.GetValue(mode) ?? PointColorizer.DefaultMode(cloud),
                PaletteId = parse.GetValue(palette)!,
            };
            var colors = PointColorizer.Colorize(cloud, settings);

            int w = parse.GetValue(width), h = parse.GetValue(height);
            var camera = new OrbitCamera();
            camera.SetPreset(parse.GetValue(view));
            if (parse.GetValue(yaw) is float y) camera.Yaw = y;
            if (parse.GetValue(pitch) is float p) camera.Pitch = p;
            camera.Fit(cloud.Bounds, w / (float)h);
            camera.Distance /= Math.Max(0.01f, parse.GetValue(zoom));

            var image = Timed("渲染", _ => SoftwareRenderer.Render(cloud, colors, camera, new RenderOptions
            {
                Width = w,
                Height = h,
                PointSize = parse.GetValue(size),
                Background = Rgb24.FromHex(parse.GetValue(background)!),
                EyeDomeLighting = !parse.GetValue(noEdl),
            }));
            var path = parse.GetRequiredValue(output).FullName;
            image.SavePng(path);
            Console.WriteLine($"已保存 {path}（{settings.Mode}）");
        }));
        return command;
    }

    private static Command GenerateCommand()
    {
        var output = new Argument<FileInfo>("output") { Description = "输出文件（.las/.ply/.xyz）" };
        var spacing = new Option<double>("--spacing") { DefaultValueFactory = _ => 0.08, Description = "地面采样间距（米）" };
        var length = new Option<double>("--length") { DefaultValueFactory = _ => 80, Description = "道路长度（米）" };
        var seed = new Option<int>("--seed") { DefaultValueFactory = _ => 2025, Description = "随机种子" };
        var noise = new Option<double>("--noise") { DefaultValueFactory = _ => 0.003, Description = "离群噪声比例" };
        var local = new Option<bool>("--local") { Description = "使用局部坐标（不加 UTM 偏移）" };

        var command = new Command("generate", "生成带分类的合成街景点云") { output, spacing, length, seed, noise, local };
        command.SetAction(parse => Run(() =>
        {
            var options = new StreetSceneOptions
            {
                Spacing = parse.GetValue(spacing),
                RoadLength = parse.GetValue(length),
                Seed = parse.GetValue(seed),
                NoiseRatio = parse.GetValue(noise),
            };
            if (parse.GetValue(local)) options = options with { Offset = Double3.Zero };

            var cloud = Timed("生成街景", p => StreetSceneGenerator.Generate(options, p));
            var path = parse.GetRequiredValue(output).FullName;
            Timed($"写出 {Path.GetFileName(path)}", p => { PointCloudIO.Save(cloud, path, p); return 0; });
            Console.WriteLine($"共 {cloud.Count:N0} 点");
        }));
        return command;
    }

    private static PointCloud Load(string path, int? maxPoints) =>
        Timed($"读取 {Path.GetFileName(path)}", p => PointCloudIO.Load(path, new PointCloudReadOptions { MaxPoints = maxPoints }, p));

    /// <summary>执行一个步骤并打印耗时；交互式终端中显示百分比进度。</summary>
    private static T Timed<T>(string title, Func<IProgress<double>, T> action)
    {
        var sw = Stopwatch.StartNew();
        bool interactive = !Console.IsOutputRedirected;
        var progress = new SyncProgress(p =>
        {
            if (interactive) Console.Write($"\r{title} {p * 100,5:F1}%");
        });
        var result = action(progress);
        if (interactive) Console.Write('\r');
        Console.WriteLine($"{title} 完成，用时 {sw.Elapsed.TotalSeconds:F2} s");
        return result;
    }

    private static int Run(Action action)
    {
        try
        {
            action();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException or FormatException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
