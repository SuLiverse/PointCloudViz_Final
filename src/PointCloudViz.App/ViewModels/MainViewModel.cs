using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PointCloudViz.App.Infrastructure;
using PointCloudViz.App.Services;
using PointCloudViz.App.Viewport;
using PointCloudViz.Core.Analysis;
using PointCloudViz.Core.Coloring;
using PointCloudViz.Core.Data;
using PointCloudViz.Core.History;
using PointCloudViz.Core.IO;
using PointCloudViz.Core.Measurements;
using PointCloudViz.Core.Processing;
using PointCloudViz.Core.Project;
using PointCloudViz.Core.Synthetic;
using PointCloudViz.Core.Viewing;

namespace PointCloudViz.App.ViewModels;

/// <summary>
/// 主窗口视图模型。界面状态全部通过绑定呈现，三维场景通过 <see cref="ISceneView"/> 操作，
/// 耗时操作统一走 <see cref="RunBusyAsync{T}"/>（遮罩 + 进度 + 可取消）。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string ProjectFilter = "PointCloudViz 项目 (*.pcvproj)|*.pcvproj|旧版项目 (*.json)|*.json";

    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly UndoRedoStack _history = new(30);
    private readonly List<Double3> _pending = [];
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private readonly Dictionary<ColorMode, (double Min, double Max)> _autoRangeCache = [];

    private ISceneView? _scene;
    private CancellationTokenSource? _busyCts;
    private PointCloud? _original;
    private PointCloud? _cloud;
    private PointCloud? _display;
    private CloudStatistics? _stats;
    private int _nextMeasurementId = 1;
    private int _displayVersion;
    /// <summary>大于 0 时暂停自动刷新（批量修改多个显示属性时使用）。</summary>
    private int _suppressRefresh;

    public MainViewModel(IDialogService dialogs, AppSettings settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        _history.Changed += (_, _) => OnHistoryChanged();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };

        Palettes = Core.Coloring.Palettes.All;
        _selectedPalette = Core.Coloring.Palettes.Get(settings.Palette);
        _pointSize = Math.Clamp(settings.PointSize, 1, 12);
        _showAxes = settings.ShowAxes;
        _selectedBackground = Backgrounds.FirstOrDefault(b => b.Color.ToHex() == settings.Background) ?? Backgrounds[0];
        _selectedDisplayBudget = DisplayBudgets.FirstOrDefault(b => b.Value == settings.DisplayBudget) ?? DisplayBudgets[2];
        _selectedLoadLimit = LoadLimits.FirstOrDefault(b => b.Value == settings.LoadLimit) ?? LoadLimits[0];
        foreach (var f in settings.RecentFiles) RecentFiles.Add(f);
        _hasRecentFiles = RecentFiles.Count > 0;
        RebuildColorModes();
    }

    #region 可绑定状态

    [ObservableProperty] private string _title = "PointCloudViz";
    [ObservableProperty] private string _statusText = "就绪 — 打开点云文件，或直接拖放到窗口";
    [ObservableProperty] private string? _hoverText;
    [ObservableProperty] private string? _toast;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private double _busyProgress;
    [ObservableProperty] private bool _busyIndeterminate = true;

    [ObservableProperty] private bool _hasCloud;
    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private int _pointCount;
    [ObservableProperty] private int _displayedCount;
    [ObservableProperty] private string? _attributesText;
    [ObservableProperty] private string? _extentXText;
    [ObservableProperty] private string? _extentYText;
    [ObservableProperty] private string? _extentZText;
    [ObservableProperty] private string? _sizeText;
    [ObservableProperty] private string? _densityText;
    [ObservableProperty] private string? _originText;

    public ObservableCollection<HistogramBar> HistogramBars { get; } = [];
    public ObservableCollection<ClassItem> Classes { get; } = [];
    [ObservableProperty] private bool _hasClasses;

    public ObservableCollection<ColorModeOption> ColorModes { get; } = [];
    [ObservableProperty] private ColorMode _selectedColorMode = ColorMode.Elevation;
    public IReadOnlyList<Palette> Palettes { get; }
    [ObservableProperty] private Palette _selectedPalette;
    [ObservableProperty] private bool _autoRange = true;
    [ObservableProperty] private string _rangeMinText = "";
    [ObservableProperty] private string _rangeMaxText = "";
    [ObservableProperty] private double _pointSize;
    [ObservableProperty] private bool _showAxes;

    public IReadOnlyList<BackgroundOption> Backgrounds { get; } =
    [
        new("深灰", Rgb24.FromHex("#1E1F24")),
        new("黑色", Rgb24.FromHex("#000000")),
        new("夜空蓝", Rgb24.FromHex("#0F1A2B")),
        new("中灰", Rgb24.FromHex("#6B6E76")),
        new("白色", Rgb24.FromHex("#FFFFFF")),
    ];
    [ObservableProperty] private BackgroundOption _selectedBackground;

    public IReadOnlyList<CountOption> DisplayBudgets { get; } =
    [
        new("50 万", 500_000), new("100 万", 1_000_000), new("300 万", 3_000_000),
        new("500 万", 5_000_000), new("1000 万", 10_000_000), new("全部", int.MaxValue),
    ];
    [ObservableProperty] private CountOption _selectedDisplayBudget;

    public IReadOnlyList<CountOption> LoadLimits { get; } =
    [
        new("不限制", 0), new("500 万", 5_000_000), new("1000 万", 10_000_000), new("2000 万", 20_000_000),
    ];
    [ObservableProperty] private CountOption _selectedLoadLimit;

    [ObservableProperty] private bool _showGradientLegend;
    [ObservableProperty] private Brush? _legendBrush;
    [ObservableProperty] private string? _legendMin;
    [ObservableProperty] private string? _legendMax;
    [ObservableProperty] private string? _legendTitle;

    [ObservableProperty] private ToolMode _toolMode = ToolMode.Navigate;
    [ObservableProperty] private string? _toolHint;
    public ObservableCollection<Measurement> Measurements { get; } = [];
    [ObservableProperty] private Measurement? _selectedMeasurement;
    [ObservableProperty] private bool _hasPending;

    public ObservableCollection<string> HistoryItems { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];
    [ObservableProperty] private bool _hasRecentFiles;

    public string? UndoText => _history.UndoDescription is { } d ? $"撤销：{d}" : "撤销";
    public string? RedoText => _history.RedoDescription is { } d ? $"重做：{d}" : "重做";

    /// <summary>示例数据路径（随程序发布）。</summary>
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "samples", "street_scene.las");

    #endregion

    public void AttachScene(ISceneView scene)
    {
        _scene = scene;
        scene.SetPointSize(PointSize);
        scene.SetBackground(SelectedBackground.Color);
        scene.SetShowAxes(ShowAxes);
    }

    /// <summary>保存用户偏好（窗口关闭时调用）。</summary>
    public void SaveSettings()
    {
        _settings.Palette = SelectedPalette.Id;
        _settings.PointSize = PointSize;
        _settings.Background = SelectedBackground.Color.ToHex();
        _settings.DisplayBudget = SelectedDisplayBudget.Value;
        _settings.LoadLimit = SelectedLoadLimit.Value;
        _settings.ShowAxes = ShowAxes;
        _settings.RecentFiles = [.. RecentFiles];
        SettingsStore.Save(_settings);
    }

    #region 文件

    [RelayCommand]
    private async Task OpenAsync()
    {
        var path = _dialogs.OpenFile("打开点云", PointCloudIO.OpenFileFilter);
        if (path is not null) await OpenPathAsync(path);
    }

    [RelayCommand]
    private Task OpenRecentAsync(string path) => OpenPathAsync(path);

    [RelayCommand]
    private async Task OpenSampleAsync()
    {
        if (File.Exists(SamplePath)) await OpenPathAsync(SamplePath);
        else await GenerateStreetAsync();
    }

    /// <summary>打开点云或项目文件（菜单、最近文件、拖放、命令行共用）。</summary>
    public async Task<bool> OpenPathAsync(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".pcvproj" or ".json")
            return await OpenProjectPathAsync(path);

        if (!File.Exists(path))
        {
            _dialogs.ShowError($"文件不存在：\n{path}");
            RecentFiles.Remove(path);
            HasRecentFiles = RecentFiles.Count > 0;
            return false;
        }
        if (!PointCloudIO.CanRead(path))
        {
            _dialogs.ShowError($"不支持的文件格式：{Path.GetExtension(path)}\n支持：{PointCloudIO.ReadablePatterns}");
            return false;
        }

        var options = new PointCloudReadOptions { MaxPoints = SelectedLoadLimit.Value > 0 ? SelectedLoadLimit.Value : null };
        var sw = Stopwatch.StartNew();
        var cloud = await RunBusyAsync($"正在读取 {Path.GetFileName(path)}",
            (progress, ct) => PointCloudIO.LoadAsync(path, options, progress, ct));
        if (cloud is null) return false;
        if (cloud.IsEmpty)
        {
            _dialogs.ShowError("文件中没有可读取的点。");
            return false;
        }

        Log.Info($"已加载 {path}：{cloud.Count} 点，用时 {sw.ElapsedMilliseconds} ms");
        await LoadCloudAsync(cloud, path);
        AddRecent(path);
        Notify($"已加载 {cloud.Count:N0} 个点（{sw.Elapsed.TotalSeconds:F1} s）");
        return true;
    }

    private async Task LoadCloudAsync(PointCloud cloud, string? path)
    {
        _original = cloud;
        _history.Clear();
        ClearMeasurementsCore();
        FilePath = path;
        FileName = path is null ? cloud.Name : Path.GetFileName(path);
        Title = $"{FileName} — PointCloudViz";
        HasCloud = true;

        _suppressRefresh++;
        RebuildColorModes(cloud);
        SelectedColorMode = PointColorizer.DefaultMode(cloud);
        AutoRange = true;
        _suppressRefresh--;

        await SetCurrentCloudAsync(cloud, resetCamera: true);
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private async Task ExportCloudAsync()
    {
        if (_cloud is null) return;
        var name = Path.GetFileNameWithoutExtension(FileName ?? "cloud") + "_edited.las";
        var path = _dialogs.SaveFile("导出点云", PointCloudIO.SaveFileFilter, name);
        if (path is null) return;
        var cloud = _cloud;
        var ok = await RunBusyAsync($"正在写出 {Path.GetFileName(path)}", async (p, ct) =>
        {
            await PointCloudIO.SaveAsync(cloud, path, p, ct);
            return true;
        });
        if (ok) Notify($"已导出 {cloud.Count:N0} 个点到 {Path.GetFileName(path)}");
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private void Screenshot()
    {
        var path = _dialogs.SaveFile("保存截图", "PNG 图片 (*.png)|*.png", Path.GetFileNameWithoutExtension(FileName ?? "screenshot") + ".png");
        if (path is null || _scene is null) return;
        if (_scene.SaveScreenshot(path)) Notify($"截图已保存：{Path.GetFileName(path)}");
        else _dialogs.ShowError("截图失败，详情见日志。");
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private async Task SaveProjectAsync()
    {
        if (_cloud is null) return;
        string? dataFile = FilePath;
        if (_history.CanUndo || dataFile is null)
        {
            if (_dialogs.Confirm("当前点云包含尚未导出的编辑（过滤/下采样等）。\n项目文件只记录数据文件路径，是否先将编辑后的点云导出为新文件？"))
            {
                var path = _dialogs.SaveFile("导出编辑后的点云", PointCloudIO.SaveFileFilter, Path.GetFileNameWithoutExtension(FileName ?? "cloud") + "_edited.las");
                if (path is null) return;
                var cloud = _cloud;
                if (!await RunBusyAsync("正在导出点云", async (p, ct) => { await PointCloudIO.SaveAsync(cloud, path, p, ct); return true; }))
                    return;
                dataFile = path;
            }
        }

        var projectPath = _dialogs.SaveFile("保存项目", ProjectFilter, Path.GetFileNameWithoutExtension(FileName ?? "project") + ".pcvproj");
        if (projectPath is null) return;

        var document = new ProjectDocument
        {
            DataFile = dataFile,
            Display = new DisplaySettings
            {
                ColorMode = SelectedColorMode,
                Palette = SelectedPalette.Id,
                RangeMin = AutoRange ? null : ParseDouble(RangeMinText),
                RangeMax = AutoRange ? null : ParseDouble(RangeMaxText),
                PointSize = PointSize,
                Background = SelectedBackground.Color.ToHex(),
                ShowAxes = ShowAxes,
            },
            Camera = _scene?.GetCameraState(),
            Measurements = Measurements.Select(MeasurementRecord.From).ToList(),
        };
        try
        {
            await ProjectSerializer.SaveAsync(projectPath, document);
            AddRecent(projectPath);
            Notify($"项目已保存：{Path.GetFileName(projectPath)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"保存项目失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = _dialogs.OpenFile("打开项目", ProjectFilter);
        if (path is not null) await OpenProjectPathAsync(path);
    }

    private async Task<bool> OpenProjectPathAsync(string path)
    {
        ProjectDocument document;
        try
        {
            document = await ProjectSerializer.LoadAsync(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"无法读取项目文件：{ex.Message}");
            return false;
        }

        if (document.DataFile is null || !File.Exists(document.DataFile))
        {
            _dialogs.ShowError($"项目引用的数据文件不存在：\n{document.DataFile ?? "(未指定)"}");
            return false;
        }
        if (!await OpenPathAsync(document.DataFile)) return false;

        var d = document.Display;
        _suppressRefresh++;
        if (PointColorizer.IsAvailable(_cloud!, d.ColorMode)) SelectedColorMode = d.ColorMode;
        SelectedPalette = Core.Coloring.Palettes.Get(d.Palette);
        AutoRange = d.RangeMin is null || d.RangeMax is null;
        if (!AutoRange)
        {
            RangeMinText = d.RangeMin!.Value.ToString("F2", CultureInfo.InvariantCulture);
            RangeMaxText = d.RangeMax!.Value.ToString("F2", CultureInfo.InvariantCulture);
        }
        PointSize = d.PointSize;
        ShowAxes = d.ShowAxes;
        if (Rgb24.TryParseHex(d.Background, out var bg))
            SelectedBackground = Backgrounds.FirstOrDefault(b => b.Color == bg) ?? new BackgroundOption("项目背景", bg);
        _suppressRefresh--;
        await RecolorAsync();

        foreach (var record in document.Measurements)
        {
            var m = record.ToMeasurement(_nextMeasurementId++);
            if (m.IsComplete) Measurements.Add(m);
        }
        UpdateOverlay();
        if (document.Camera is { } camera) _scene?.SetCameraState(camera);
        AddRecent(path);
        Notify($"已打开项目 {Path.GetFileName(path)}");
        return true;
    }

    [RelayCommand]
    private async Task GenerateStreetAsync()
    {
        var fields = new List<ParameterField>
        {
            new("道路长度 (m)：", 80, 10, 1000, "沿 Y 方向延伸"),
            new("采样间距 (m)：", 0.08, 0.02, 2, "越小点越密；0.08 约 40 万点"),
            new("噪声比例：", 0.003, 0, 0.2, "随机离群点占比，便于演示离群点滤波"),
            new("随机种子：", 2025, 0, int.MaxValue) { IsInteger = true },
        };
        if (!_dialogs.EditParameters("生成合成街景", "生成包含道路、人行道、建筑、行道树、车辆与路灯的带分类点云，并使用 UTM 量级坐标。", fields))
            return;

        var path = _dialogs.SaveFile("保存合成点云", PointCloudIO.SaveFileFilter, "street_scene.las");
        if (path is null) return;

        var options = new StreetSceneOptions
        {
            RoadLength = fields[0].Value,
            Spacing = fields[1].Value,
            NoiseRatio = fields[2].Value,
            Seed = (int)fields[3].Value,
        };
        var ok = await RunBusyAsync("正在生成合成街景", async (p, ct) =>
        {
            var cloud = await Task.Run(() => StreetSceneGenerator.Generate(options, new ScaledProgress(p, 0, 0.6), ct), ct);
            await PointCloudIO.SaveAsync(cloud, path, new ScaledProgress(p, 0.6, 1), ct);
            return true;
        });
        if (ok) await OpenPathAsync(path);
    }

    private void AddRecent(string path)
    {
        _settings.AddRecentFile(path);
        RecentFiles.Clear();
        foreach (var f in _settings.RecentFiles) RecentFiles.Add(f);
        HasRecentFiles = RecentFiles.Count > 0;
    }

    #endregion

    #region 编辑（滤波 + 撤销/重做）

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private async Task FilterElevationAsync()
    {
        if (_cloud is null) return;
        var fields = new List<ParameterField>
        {
            new("最低高程 (m)：", Math.Floor(_cloud.WorldMin.Z * 100) / 100),
            new("最高高程 (m)：", Math.Ceiling(_cloud.WorldMax.Z * 100) / 100),
        };
        if (_dialogs.EditParameters("高程过滤", "保留高程位于区间内的点（世界坐标）。", fields))
            await ApplyFilterAsync(new ElevationRangeFilter(fields[0].Value, fields[1].Value));
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private async Task VoxelDownsampleAsync()
    {
        if (_cloud is null) return;
        double suggested = SuggestSpacing(_cloud) * 2;
        var fields = new List<ParameterField> { new("体素大小 (m)：", suggested, 1e-4, 1e4, $"当前平均点距约 {SuggestSpacing(_cloud):G3} m") };
        if (_dialogs.EditParameters("体素下采样", "每个体素内的点合并为一个点（坐标、颜色、强度取平均）。", fields))
            await ApplyFilterAsync(new VoxelGridFilter((float)fields[0].Value));
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private async Task StatisticalOutlierAsync()
    {
        var fields = new List<ParameterField>
        {
            new("近邻数 k：", 16, 1, 200) { IsInteger = true },
            new("标准差倍数 α：", 2.0, 0, 20, "平均近邻距离超过 μ + α·σ 的点被剔除；α 越小越严格"),
        };
        if (_dialogs.EditParameters("统计离群点剔除 (SOR)", "适合去除空中飞点、多路径噪声等稀疏离群点。", fields))
            await ApplyFilterAsync(new StatisticalOutlierFilter((int)fields[0].Value, fields[1].Value));
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private async Task RadiusOutlierAsync()
    {
        if (_cloud is null) return;
        var fields = new List<ParameterField>
        {
            new("搜索半径 (m)：", Math.Round(SuggestSpacing(_cloud) * 5, 3), 1e-4, 1e4),
            new("最少邻居数：", 4, 1, 1000) { IsInteger = true },
        };
        if (_dialogs.EditParameters("半径离群点剔除 (ROR)", "半径内邻居数不足的点被视为离群点。", fields))
            await ApplyFilterAsync(new RadiusOutlierFilter((float)fields[0].Value, (int)fields[1].Value));
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private async Task SubsampleAsync()
    {
        if (_cloud is null) return;
        var fields = new List<ParameterField> { new("目标点数：", Math.Max(1, _cloud.Count / 2), 1, _cloud.Count) { IsInteger = true } };
        if (_dialogs.EditParameters("随机抽稀", "均匀随机地保留指定数量的点（结果可复现）。", fields))
            await ApplyFilterAsync(new RandomSubsampleFilter((int)fields[0].Value));
    }

    [RelayCommand(CanExecute = nameof(HasClasses))]
    private async Task KeepVisibleClassesAsync()
    {
        var keep = Classes.Where(c => c.IsVisible).Select(c => c.Code).ToList();
        if (keep.Count == 0)
        {
            _dialogs.ShowInfo("请至少勾选一个分类。");
            return;
        }
        await ApplyFilterAsync(new ClassificationFilter(keep));
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private void RestoreOriginal()
    {
        if (_original is null || _cloud is null || ReferenceEquals(_original, _cloud)) return;
        var before = _cloud;
        var original = _original;
        _history.Execute(new DelegateCommand("恢复原始数据", () => SetCurrentCloudSafe(original), () => SetCurrentCloudSafe(before)));
    }

    [RelayCommand]
    private void Undo()
    {
        CancelPending();
        if (_history.Undo()) Notify($"已撤销");
    }

    [RelayCommand]
    private void Redo()
    {
        if (_history.Redo()) Notify($"已重做");
    }

    private async Task ApplyFilterAsync(IPointFilter filter)
    {
        if (_cloud is null) return;
        var before = _cloud;
        var sw = Stopwatch.StartNew();
        var after = await RunBusyAsync(filter.Description, (p, ct) => Task.Run(() => filter.Apply(before, p, ct), ct));
        if (after is null) return;
        if (after.IsEmpty)
        {
            _dialogs.ShowInfo("该操作会移除全部点，已取消。请调整参数后重试。");
            return;
        }
        if (after.Count == before.Count)
        {
            Notify($"{filter.Description}：没有点被移除");
            return;
        }

        _history.Execute(new DelegateCommand(filter.Description, () => SetCurrentCloudSafe(after), () => SetCurrentCloudSafe(before)));
        Log.Info($"{filter.Description}：{before.Count} → {after.Count}，{sw.ElapsedMilliseconds} ms");
        Notify($"{filter.Description}：{before.Count:N0} → {after.Count:N0} 点（{sw.Elapsed.TotalSeconds:F1} s）");
    }

    /// <summary>估算平均点距：按包围盒体积/面积与点数粗略推算，用作参数默认值。</summary>
    private static double SuggestSpacing(PointCloud cloud)
    {
        var s = cloud.Bounds.Size;
        double area = Math.Max(s.X * s.Y, Math.Max(s.X * s.Z, s.Y * s.Z));
        double spacing = Math.Sqrt(Math.Max(area, 1e-9) / Math.Max(1, cloud.Count));
        return Math.Round(Math.Max(spacing, 1e-3), 3);
    }

    private void OnHistoryChanged()
    {
        HistoryItems.Clear();
        foreach (var item in _history.History) HistoryItems.Add(item);
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(RedoText));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region 当前点云与显示

    private void SetCurrentCloudSafe(PointCloud cloud) => _ = SafeAsync(SetCurrentCloudAsync(cloud, resetCamera: false));

    private async Task SetCurrentCloudAsync(PointCloud cloud, bool resetCamera)
    {
        _cloud = cloud;
        _autoRangeCache.Clear();
        PointCount = cloud.Count;
        // 统计量是 O(n) 计算，千万级点云放到后台线程，避免界面卡顿
        var stats = await Task.Run(() => CloudStatistics.Compute(cloud));
        if (!ReferenceEquals(cloud, _cloud)) return;
        UpdateInfo(cloud, stats);
        await RefreshDisplayAsync(resetCamera);
    }

    private void UpdateInfo(PointCloud cloud, CloudStatistics stats)
    {
        var ci = CultureInfo.InvariantCulture;
        AttributesText = string.Join(" · ", new[]
        {
            cloud.Has(PointAttributes.Color) ? "RGB" : null,
            cloud.Has(PointAttributes.Intensity) ? "强度" : null,
            cloud.Has(PointAttributes.Classification) ? "分类" : null,
        }.Where(s => s is not null).DefaultIfEmpty("仅坐标"));
        ExtentXText = string.Create(ci, $"{stats.X.Min:F3} ~ {stats.X.Max:F3}");
        ExtentYText = string.Create(ci, $"{stats.Y.Min:F3} ~ {stats.Y.Max:F3}");
        ExtentZText = string.Create(ci, $"{stats.Z.Min:F3} ~ {stats.Z.Max:F3}");
        SizeText = string.Create(ci, $"{stats.X.Range:F2} × {stats.Y.Range:F2} × {stats.Z.Range:F2} m");
        DensityText = string.Create(ci, $"{stats.PlanarDensity:F1} 点/m²");
        OriginText = string.Create(ci, $"{cloud.Origin.X:F3}, {cloud.Origin.Y:F3}, {cloud.Origin.Z:F3}");

        _stats = stats;
        UpdateHistogram();

        // 保留用户之前隐藏的分类（例如撤销后）
        var hidden = Classes.Where(c => !c.IsVisible).Select(c => c.Code).ToHashSet();
        _suppressRefresh++;
        Classes.Clear();
        if (cloud.Has(PointAttributes.Classification))
        {
            foreach (var (code, count) in stats.PresentClasses)
                Classes.Add(new ClassItem(code, count, count * 100.0 / cloud.Count, OnClassVisibilityChanged) { IsVisible = !hidden.Contains(code) });
        }
        _suppressRefresh--;
        HasClasses = Classes.Count > 0;
    }

    /// <summary>直方图柱子按当前色带着色。</summary>
    private void UpdateHistogram()
    {
        HistogramBars.Clear();
        if (_stats is null) return;
        var ci = CultureInfo.InvariantCulture;
        var h = _stats.ElevationHistogram;
        int peak = Math.Max(1, h.Peak);
        for (int i = 0; i < h.BinCount; i++)
        {
            double lo = h.Min + i * h.BinWidth;
            var color = SelectedPalette.Sample((i + 0.5f) / h.BinCount);
            HistogramBars.Add(new HistogramBar(h.Counts[i] / (double)peak, color.ToBrush(),
                string.Create(ci, $"{lo:F2} ~ {lo + h.BinWidth:F2} m：{h.Counts[i]:N0} 点")));
        }
    }

    private void OnClassVisibilityChanged()
    {
        if (_suppressRefresh == 0) _ = SafeAsync(RefreshDisplayAsync(resetCamera: false));
    }

    [RelayCommand]
    private void ShowAllClasses() => SetAllClasses(true);

    [RelayCommand]
    private void HideAllClasses() => SetAllClasses(false);

    private void SetAllClasses(bool visible)
    {
        _suppressRefresh++;
        foreach (var c in Classes) c.IsVisible = visible;
        _suppressRefresh--;
        OnClassVisibilityChanged();
    }

    /// <summary>重新生成显示子集（隐藏分类 + 点数上限）并着色。</summary>
    private async Task RefreshDisplayAsync(bool resetCamera)
    {
        var cloud = _cloud;
        if (cloud is null || _scene is null) return;
        int version = ++_displayVersion;

        var visible = Classes.Count > 0 && Classes.Any(c => !c.IsVisible)
            ? Classes.Where(c => c.IsVisible).Select(c => c.Code).ToList()
            : null;
        int budget = SelectedDisplayBudget.Value;
        var settings = CurrentColorSettings(cloud);

        var (display, colors) = await Task.Run(() =>
        {
            var d = cloud;
            if (visible is not null) d = new ClassificationFilter(visible).Apply(d);
            if (d.Count > budget) d = new RandomSubsampleFilter(budget).Apply(d);
            return (d, PointColorizer.Colorize(d, settings));
        });
        if (version != _displayVersion) return;

        _display = display;
        DisplayedCount = display.Count;
        await _scene.SetCloudAsync(display, colors, resetCamera);
        UpdateOverlay();
        UpdateLegend(settings);
        StatusText = display.Count == cloud.Count
            ? $"共 {cloud.Count:N0} 个点"
            : $"共 {cloud.Count:N0} 个点，显示 {display.Count:N0} 个";
    }

    private async Task RecolorAsync()
    {
        if (_display is null || _cloud is null || _scene is null || _suppressRefresh > 0) return;
        var display = _display;
        var settings = CurrentColorSettings(_cloud);
        int version = ++_displayVersion;
        var colors = await Task.Run(() => PointColorizer.Colorize(display, settings));
        if (version != _displayVersion) return;
        await _scene.SetColorsAsync(colors);
        UpdateLegend(settings);
    }

    /// <summary>着色设置。自动范围基于完整点云计算（而非显示子集），并回填到范围输入框。</summary>
    private ColorSettings CurrentColorSettings(PointCloud cloud)
    {
        var mode = SelectedColorMode;
        double? min = null, max = null;
        if (mode is ColorMode.Elevation or ColorMode.Intensity)
        {
            if (AutoRange)
            {
                if (!_autoRangeCache.TryGetValue(mode, out var range))
                    _autoRangeCache[mode] = range = PointColorizer.AutoRange(cloud, mode);
                (min, max) = range;
                RangeMinText = range.Min.ToString("F2", CultureInfo.InvariantCulture);
                RangeMaxText = range.Max.ToString("F2", CultureInfo.InvariantCulture);
            }
            else
            {
                min = ParseDouble(RangeMinText);
                max = ParseDouble(RangeMaxText);
            }
        }
        return new ColorSettings
        {
            Mode = mode,
            PaletteId = SelectedPalette.Id,
            RangeMin = min,
            RangeMax = max,
            UniformColor = SelectedBackground.Color.Luminance > 0.55f ? new Rgb24(40, 40, 48) : new Rgb24(225, 225, 230),
        };
    }

    private void UpdateLegend(ColorSettings settings)
    {
        ShowGradientLegend = settings.Mode is ColorMode.Elevation or ColorMode.Intensity;
        if (!ShowGradientLegend) return;
        var palette = Core.Coloring.Palettes.Get(settings.PaletteId);
        LegendBrush = PaletteBrush(palette, vertical: true);
        LegendTitle = settings.Mode == ColorMode.Elevation ? "高程 (m)" : "强度";
        LegendMax = settings.RangeMax?.ToString("F2", CultureInfo.InvariantCulture);
        LegendMin = settings.RangeMin?.ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>色带转 WPF 渐变画刷（供图例与色带下拉框预览使用）。</summary>
    public static LinearGradientBrush PaletteBrush(Palette palette, bool vertical)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = vertical ? new System.Windows.Point(0, 1) : new System.Windows.Point(0, 0),
            EndPoint = vertical ? new System.Windows.Point(0, 0) : new System.Windows.Point(1, 0),
        };
        for (int i = 0; i <= 16; i++)
            brush.GradientStops.Add(new GradientStop(palette.Sample(i / 16f).ToWpf(), i / 16.0));
        brush.Freeze();
        return brush;
    }

    private void RebuildColorModes(PointCloud? cloud = null)
    {
        if (ColorModes.Count == 0)
        {
            ColorModes.Add(new ColorModeOption(ColorMode.Rgb, "真彩色 (RGB)"));
            ColorModes.Add(new ColorModeOption(ColorMode.Elevation, "高程"));
            ColorModes.Add(new ColorModeOption(ColorMode.Intensity, "强度"));
            ColorModes.Add(new ColorModeOption(ColorMode.Classification, "分类"));
            ColorModes.Add(new ColorModeOption(ColorMode.Uniform, "单色"));
        }
        foreach (var option in ColorModes)
            option.IsAvailable = cloud is null || PointColorizer.IsAvailable(cloud, option.Mode);
    }

    partial void OnSelectedColorModeChanged(ColorMode value) => _ = SafeAsync(RecolorAsync());

    partial void OnSelectedPaletteChanged(Palette value)
    {
        UpdateHistogram();
        _ = SafeAsync(RecolorAsync());
    }

    partial void OnAutoRangeChanged(bool value) => _ = SafeAsync(RecolorAsync());

    [RelayCommand]
    private Task ApplyRangeAsync()
    {
        AutoRange = false;
        return RecolorAsync();
    }

    partial void OnPointSizeChanged(double value) => _scene?.SetPointSize(value);

    partial void OnShowAxesChanged(bool value) => _scene?.SetShowAxes(value);

    partial void OnSelectedBackgroundChanged(BackgroundOption value)
    {
        _scene?.SetBackground(value.Color);
        if (SelectedColorMode == ColorMode.Uniform) _ = SafeAsync(RecolorAsync());
    }

    partial void OnSelectedDisplayBudgetChanged(CountOption value) => _ = SafeAsync(RefreshDisplayAsync(resetCamera: false));

    partial void OnHasCloudChanged(bool value)
    {
        foreach (var command in new IRelayCommand[]
                 {
                     ExportCloudCommand, ScreenshotCommand, SaveProjectCommand, FilterElevationCommand, VoxelDownsampleCommand,
                     StatisticalOutlierCommand, RadiusOutlierCommand, SubsampleCommand, RestoreOriginalCommand, FitViewCommand,
                     SetViewCommand, ExportMeasurementsCommand,
                 })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    partial void OnHasClassesChanged(bool value) => KeepVisibleClassesCommand.NotifyCanExecuteChanged();

    #endregion

    #region 视图

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private void FitView() => _scene?.FitView();

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private void SetView(ViewPreset preset) => _scene?.SetView(preset);

    public void OnHover(Double3? world) =>
        HoverText = world is { } w ? string.Create(CultureInfo.InvariantCulture, $"X {w.X:F3}   Y {w.Y:F3}   Z {w.Z:F3}") : null;

    #endregion

    #region 量测

    [RelayCommand]
    private void SetTool(ToolMode mode) => ToolMode = mode;

    partial void OnToolModeChanged(ToolMode value)
    {
        CancelPending();
        ToolHint = value switch
        {
            ToolMode.Point => "坐标拾取：左键单击点云",
            ToolMode.Distance => "距离量测：依次单击两个点",
            ToolMode.Polyline => "折线量测：单击添加顶点，双击 / 右键 / Enter 完成，Backspace 撤回顶点",
            ToolMode.Area => "面积量测：按顺序单击多边形顶点，双击 / 右键 / Enter 完成",
            _ => null,
        };
    }

    /// <summary>视口单击回调。</summary>
    public void OnPointClicked(Double3? world)
    {
        if (ToolMode == ToolMode.Navigate)
        {
            if (world is { } w) StatusText = string.Create(CultureInfo.InvariantCulture, $"点坐标：X {w.X:F3}  Y {w.Y:F3}  Z {w.Z:F3}");
            return;
        }
        if (world is not { } p)
        {
            Notify("此处没有点，请单击点云上的位置（可放大后再选）");
            return;
        }

        var kind = CurrentKind;
        _pending.Add(p);
        if (kind == MeasurementKind.Point || (kind == MeasurementKind.Distance && _pending.Count == 2))
        {
            CommitPending();
            return;
        }

        HasPending = true;
        if (_pending.Count >= Measurement.MinimumPoints(kind))
            StatusText = $"当前：{new Measurement(0, kind, [.. _pending]).Summary}（右键或 Enter 完成）";
        else
            StatusText = $"已选择 {_pending.Count} 个点";
        UpdateOverlay();
    }

    /// <summary>双击：折线/面积量测中表示"添加最后一个顶点并完成"，返回 true 表示已处理。</summary>
    public bool OnDoubleClicked()
    {
        if (ToolMode is not (ToolMode.Polyline or ToolMode.Area) || _pending.Count == 0) return false;
        FinishMeasurement();
        return true;
    }

    public void OnRightClicked()
    {
        if (ToolMode is ToolMode.Polyline or ToolMode.Area && _pending.Count > 0) FinishMeasurement();
    }

    [RelayCommand]
    private void FinishMeasurement()
    {
        if (_pending.Count == 0) return;
        if (_pending.Count < Measurement.MinimumPoints(CurrentKind))
        {
            Notify($"至少需要 {Measurement.MinimumPoints(CurrentKind)} 个点");
            return;
        }
        CommitPending();
    }

    [RelayCommand]
    private void CancelPending()
    {
        if (_pending.Count == 0) return;
        _pending.Clear();
        HasPending = false;
        UpdateOverlay();
    }

    [RelayCommand]
    private void RemoveLastPoint()
    {
        if (_pending.Count == 0) return;
        _pending.RemoveAt(_pending.Count - 1);
        HasPending = _pending.Count > 0;
        UpdateOverlay();
    }

    private MeasurementKind CurrentKind => ToolMode switch
    {
        ToolMode.Point => MeasurementKind.Point,
        ToolMode.Distance => MeasurementKind.Distance,
        ToolMode.Polyline => MeasurementKind.Polyline,
        _ => MeasurementKind.Area,
    };

    private void CommitPending()
    {
        var m = new Measurement(_nextMeasurementId++, CurrentKind, [.. _pending]);
        _pending.Clear();
        HasPending = false;
        _history.Execute(new DelegateCommand($"添加量测 #{m.Id}",
            () => { Measurements.Add(m); SelectedMeasurement = m; UpdateOverlay(); },
            () => { Measurements.Remove(m); UpdateOverlay(); }));
        StatusText = m.ToString();
        Notify(m.Summary);
    }

    [RelayCommand]
    private void DeleteMeasurement(Measurement? measurement)
    {
        if (measurement is null) return;
        int index = Measurements.IndexOf(measurement);
        if (index < 0) return;
        _history.Execute(new DelegateCommand($"删除量测 #{measurement.Id}",
            () => { Measurements.Remove(measurement); UpdateOverlay(); },
            () => { Measurements.Insert(Math.Min(index, Measurements.Count), measurement); UpdateOverlay(); }));
    }

    [RelayCommand]
    private void ClearMeasurements()
    {
        CancelPending();
        if (Measurements.Count == 0) return;
        var removed = Measurements.ToList();
        _history.Execute(new DelegateCommand("清空量测",
            () => { Measurements.Clear(); UpdateOverlay(); },
            () => { foreach (var m in removed) Measurements.Add(m); UpdateOverlay(); }));
    }

    [RelayCommand(CanExecute = nameof(HasCloud))]
    private void ExportMeasurements()
    {
        if (Measurements.Count == 0)
        {
            _dialogs.ShowInfo("还没有量测结果。");
            return;
        }
        var path = _dialogs.SaveFile("导出量测结果", "CSV 表格 (*.csv)|*.csv", "measurements.csv");
        if (path is null) return;
        try
        {
            MeasurementExport.WriteCsv(path, Measurements);
            Notify($"已导出 {Measurements.Count} 条量测结果");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"导出失败：{ex.Message}");
        }
    }

    partial void OnSelectedMeasurementChanged(Measurement? value) => UpdateOverlay();

    private void ClearMeasurementsCore()
    {
        _pending.Clear();
        HasPending = false;
        Measurements.Clear();
        _nextMeasurementId = 1;
    }

    private void UpdateOverlay() =>
        _scene?.SetOverlay(Measurements.ToList(), [.. _pending], CurrentKind, SelectedMeasurement?.Id);

    #endregion

    #region 忙碌状态与通知

    [RelayCommand]
    private void CancelBusy() => _busyCts?.Cancel();

    /// <summary>执行耗时操作：显示遮罩与进度，可取消；异常统一提示并记录日志。</summary>
    private async Task<T?> RunBusyAsync<T>(string message, Func<IProgress<double>, CancellationToken, Task<T>> work)
    {
        if (IsBusy) return default;
        using var cts = new CancellationTokenSource();
        _busyCts = cts;
        BusyMessage = message;
        BusyProgress = 0;
        BusyIndeterminate = true;
        IsBusy = true;
        var progress = new Progress<double>(p =>
        {
            BusyIndeterminate = false;
            BusyProgress = p * 100;
        });
        try
        {
            return await work(progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Notify("操作已取消");
            return default;
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            Notify("操作已取消");
            return default;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException
                                       or UnauthorizedAccessException or OutOfMemoryException or InvalidOperationException
                                       or FormatException or EndOfStreamException or AggregateException)
        {
            Log.Error(message, ex);
            if (ex is AggregateException aggregate) ex = aggregate.Flatten().InnerExceptions[0];
            _dialogs.ShowError(ex is OutOfMemoryException
                ? "内存不足。请在“显示”面板中设置读取上限，或先用命令行工具 pcv 抽稀后再打开。"
                : ex.Message, "操作失败");
            return default;
        }
        finally
        {
            IsBusy = false;
            _busyCts = null;
        }
    }

    public void Notify(string message)
    {
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private async Task SafeAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("后台任务失败", ex);
            Notify($"出错了：{ex.Message}");
        }
    }

    private static double? ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v) ? v : null;

    /// <summary>把子任务的 0~1 进度映射到总进度的某一段。</summary>
    private sealed class ScaledProgress(IProgress<double> inner, double from, double to) : IProgress<double>
    {
        public void Report(double value) => inner.Report(from + (to - from) * value);
    }

    #endregion
}
