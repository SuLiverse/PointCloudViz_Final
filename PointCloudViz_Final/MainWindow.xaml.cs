using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Core;
using Microsoft.Win32;
using PointCloudViz_Final.Filters;
using PointCloudViz_Final.IO;
using PointCloudViz_Final.Models;
using PointCloudViz_Final.Patterns;
using PointCloudViz_Final.Rendering;
using PointCloudViz_Final.Services;
using PointCloudViz_Final.Tools;
using PointCloudViz_Final.Utils;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vector3 = System.Numerics.Vector3;
using Cloud = PointCloudViz_Final.Models.PointCloud;

namespace PointCloudViz_Final;

public partial class MainWindow : Window
{
    private readonly IPointReader[] _readers = [new XyzReader(), new IO.PlyReader(), new StreamingLasReader()];
    private readonly Patterns.CommandManager _commands = new();
    private readonly MeasurementTool _measurements = new();
    private readonly ObservableCollection<HistoryEntry> _history = new();
    private readonly ObservableCollection<Measurement> _measurementRows = new();
    private readonly ObservableCollection<RecentFile> _recent = new();
    private Cloud? _cloud, _original;
    private IColorMap _colorMap = new HeightColorMap();
    private string? _dataPath;
    private string _background = "#171B1E";
    private CancellationTokenSource? _operation;
    private bool _ready, _closing;
    private bool _dirty;
    private readonly string _stateDirectory = Environment.GetEnvironmentVariable("POINTCLOUD_STUDIO_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PointCloudStudio");
    internal bool IsBusy => _operation != null;
    internal Cloud? CurrentCloud => _cloud;
    internal string? LastError { get; private set; }
    internal bool SuppressDialogs { get; set; }
    internal MeasurementTool Measurements => _measurements;
    internal record HistoryEntry(string Time, string Action, string Count);
    internal record RecentFile(string Path) { public string Name => System.IO.Path.GetFileName(Path); }

    public MainWindow()
    {
        InitializeComponent();
        HistoryGrid.ItemsSource = _history;
        MeasurementGrid.ItemsSource = _measurementRows;
        RecentFiles.ItemsSource = _recent;
        _commands.OnCommandExecuted += c => { Record(c.Description); UpdateCommands(); };
        _commands.OnCommandUndone += c => { Record("撤销: " + c.Description); UpdateCommands(); };
        _commands.OnCommandRedone += c => { Record("重做: " + c.Description); UpdateCommands(); };
        _measurements.OnMeasurementCreated += _ => { _dirty = true; RefreshMeasurements(); MeasurementTab.IsSelected = true; };
        _measurements.OnMeasurementMessage += message => StatusText.Text = message;
        Loaded += async (_, _) =>
        {
            try
            {
                Viewport.EffectsManager = new DefaultEffectsManager();
                Viewport.MSAA = MSAALevel.Two;
                _ready = true;
                LoadRecent();
                UpdateCommands();
                var path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
                if (path != null && File.Exists(path)) await LoadPathAsync(path);
                else await LoadDemoAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                RendererStatus.Text = "渲染器不可用";
                ReportError(ex);
            }
        };
        Closing += (_, e) =>
        {
            if (_closing) return;
            if (IsBusy) { _operation!.Cancel(); e.Cancel = true; StatusText.Text = "任务取消后可关闭窗口"; return; }
            if (!ConfirmDiscard()) e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            _closing = true;
            _operation?.Cancel();
            Viewport.EffectsManager?.Dispose();
        };
    }

    private bool ConfirmDiscard() => !_dirty || MessageBox.Show(this, "当前更改尚未保存为项目快照。继续并放弃未保存的更改？",
        "未保存的更改", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    internal async Task RunOperationAsync(string label, Func<CancellationToken, Task> action)
    {
        if (IsBusy || _closing) return;
        using var source = new CancellationTokenSource();
        _operation = source;
        LastError = null;
        Workspace.IsEnabled = false;
        LoadingText.Text = label;
        LoadingProgress.IsIndeterminate = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = label;
        try { await action(source.Token); }
        catch (OperationCanceledException) { StatusText.Text = "已取消，当前场景保持不变"; }
        catch (Exception ex) { ReportError(ex); }
        finally
        {
            _operation = null;
            Workspace.IsEnabled = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            UpdateCommands();
        }
    }

    private void ReportError(Exception ex)
    {
        LastError = ex.Message;
        Logger.Error("Workspace operation failed", ex);
        StatusText.Text = "失败: " + ex.Message;
        if (!_closing && !SuppressDialogs) MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private Task<Cloud> ReadCloudAsync(string path, CancellationToken token)
    {
        var reader = _readers.FirstOrDefault(r => r.CanRead(Path.GetExtension(path)))
            ?? throw new NotSupportedException("支持 XYZ、TXT、PLY、LAS 点云和 JSON 项目。");
        if (reader is StreamingLasReader las)
        {
            var progress = new Progress<double>(value =>
            {
                if (_operation?.Token != token || token.IsCancellationRequested) return;
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Value = value;
            });
            return las.ReadAsync(path, token, progress);
        }
        return reader.ReadAsync(path, token);
    }

    internal Task LoadPathAsync(string path) => RunOperationAsync("正在打开 " + Path.GetFileName(path), async token =>
    {
        path = Path.GetFullPath(path);
        ProjectSettings? project = null;
        string source = path;
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            project = await ProjectIO.LoadAsync(path, token);
            await ProjectIO.VerifyDataAsync(project, token);
            source = project.DataFile ?? throw new InvalidDataException("项目没有关联点云文件。");
            _ = (Color)ColorConverter.ConvertFromString(project.Background);
        }
        var cloud = await ReadCloudAsync(source, token);
        if (cloud.Count == 0) throw new InvalidDataException("文件不包含点。");
        token.ThrowIfCancellationRequested();
        SetDocument(cloud, source, Path.GetFileName(path));
        if (project != null)
        {
            SetColorMode(project.ColorMap);
            PointSizeSlider.Value = project.PointSize;
            _background = project.Background;
            Viewport.BackgroundColor = (Color)ColorConverter.ConvertFromString(_background);
            RenderBudget.IsChecked = project.RenderBudget;
            if (project.Version >= 2 && project.CameraDistance > 0)
            {
                _yaw = project.CameraYaw; _pitch = project.CameraPitch; _distance = project.CameraDistance;
                _target = new(project.CameraTargetX, project.CameraTargetY, project.CameraTargetZ);
                ApplyCamera();
            }
            _measurements.Restore(project.Measurements);
            RefreshMeasurements();
            RenderScene();
        }
        await RememberAsync(path);
        Record(project == null ? "打开点云" : "恢复项目快照");
        _dirty = false;
    });

    internal Task LoadDemoAsync() => RunOperationAsync("正在生成合成街区", async token =>
    {
        var cloud = await Task.Run(() => DemoSceneFactory.Create(2026, token), token);
        token.ThrowIfCancellationRequested();
        SetDocument(cloud, null, "合成街区");
        Record("生成示例 / Seed 2026");
        _dirty = false;
    });

    private void SetDocument(Cloud cloud, string? source, string title)
    {
        _original = cloud;
        _cloud = cloud;
        _dataPath = source;
        _commands.Clear();
        _history.Clear();
        ((TabControl)MeasurementTab.Parent).SelectedIndex = 0;
        _measurements.ClearAll();
        _offset = new Vector3((float)(((double)cloud.BBox.MinX + cloud.BBox.MaxX) / 2),
            (float)(((double)cloud.BBox.MinY + cloud.BBox.MaxY) / 2), (float)(((double)cloud.BBox.MinZ + cloud.BBox.MaxZ) / 2));
        CurrentFileText.Text = title;
        CurrentFileText.ToolTip = source ?? "合成街区";
        DocumentTitle.Text = title;
        SceneLabel.Text = "场景 / " + title;
        CloudVisible.IsChecked = true;
        RefreshMeasurements();
        PresentCloud(cloud);
        FitScene();
        UpdateCommands();
    }

    private void PresentCloud(Cloud cloud)
    {
        _cloud = cloud;
        PointCountText.Text = cloud.Count.ToString("N0");
        OriginalCountText.Text = $"原始 {_original?.Count:N0}  /  保留 {(cloud.Count * 100.0 / Math.Max(1, _original?.Count ?? 1)):F1}%";
        var b = cloud.BBox;
        ExtentText.Text = $"X  {(double)b.MaxX - b.MinX:N3} u\nY  {(double)b.MaxY - b.MinY:N3} u\nZ  {(double)b.MaxZ - b.MinZ:N3} u";
        OriginText.Text = $"X  {_offset.X:N3}\nY  {_offset.Y:N3}\nZ  {_offset.Z:N3}";
        MinZInput.Text = (Math.Floor(b.MinZ * 1000d) / 1000).ToString("G9", CultureInfo.InvariantCulture);
        MaxZInput.Text = (Math.Ceiling(b.MaxZ * 1000d) / 1000).ToString("G9", CultureInfo.InvariantCulture);
        var stats = CloudStatistics.Calculate(cloud);
        StatisticsText.Text = $"均值  {stats.MeanZ:N3} u\n标准差  {stats.StandardDeviationZ:N3} u";
        HeightMinText.Text = b.MinZ.ToString("F2");
        HeightMaxText.Text = b.MaxZ.ToString("F2");
        DrawHistogram(stats.HeightBins);
        RenderScene();
        _dirty = true;
    }

    internal Task ApplyFilterAsync(IPointFilter filter)
    {
        if (_cloud == null) return Task.CompletedTask;
        var before = _cloud;
        return RunOperationAsync("正在处理 " + filter.Name, async token =>
        {
            var after = await Task.Run(() => new Cloud(filter.Apply(before.Points, before.BBox, token)), token);
            token.ThrowIfCancellationRequested();
            _commands.Execute(new CloudChangeCommand(filter.Name, before, after, PresentCloud));
        });
    }

    private void UpdateCommands()
    {
        UndoButton.IsEnabled = UndoMenu.IsEnabled = _commands.CanUndo && !IsBusy;
        RedoButton.IsEnabled = RedoMenu.IsEnabled = _commands.CanRedo && !IsBusy;
    }

    private void Record(string action)
    {
        _history.Insert(0, new(DateTime.Now.ToString("HH:mm:ss"), action, (_cloud?.Count ?? 0).ToString("N0")));
        if (_history.Count > 200) _history.RemoveAt(_history.Count - 1);
        StatusText.Text = action + $"  /  {_cloud?.Count ?? 0:N0} 点";
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "点云和项目|*.xyz;*.txt;*.ply;*.las;*.json|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) await LoadPathAsync(dialog.FileName);
    }
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "PointCloud 项目|*.json" };
        if (dialog.ShowDialog(this) == true) await LoadPathAsync(dialog.FileName);
    }
    private async void Demo_Click(object sender, RoutedEventArgs e) { if (!IsBusy && ConfirmDiscard()) await LoadDemoAsync(); }
    private async void OriginalSample_Click(object sender, RoutedEventArgs e)
    {
        if (!IsBusy && ConfirmDiscard()) await LoadPathAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "sample_final.xyz"));
    }
    private async void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (!IsBusy && sender is Button { Tag: RecentFile file } && ConfirmDiscard()) await LoadPathAsync(file.Path);
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (IsBusy || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths || !ConfirmDiscard()) return;
        await LoadPathAsync(paths[0]);
    }
    private async void ExportCloud_Click(object sender, RoutedEventArgs e)
    {
        if (_cloud == null || IsBusy) return;
        var dialog = new SaveFileDialog { Filter = "XYZ 点云|*.xyz|二进制 PLY 点云|*.ply", FileName = "pointcloud" };
        if (dialog.ShowDialog(this) == true)
            await RunOperationAsync("正在导出点云", async token =>
            {
                await Task.Run(() => dialog.FilterIndex == 2
                    ? PlyWriter.WriteAsync(_cloud, dialog.FileName, token)
                    : XyzWriter.WriteAsync(_cloud, dialog.FileName, token), token);
                Record("导出 " + (dialog.FilterIndex == 2 ? "PLY" : "XYZ"));
            });
    }

    internal async Task SaveSnapshotAsync(string path, CancellationToken token)
    {
        if (_cloud == null) return;
        string snapshot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
            Path.GetFileNameWithoutExtension(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".xyz");
        bool committed = false;
        try
        {
            await Task.Run(() => XyzWriter.WriteAsync(_cloud, snapshot, token), token);
            string digest;
            await using (var stream = File.OpenRead(snapshot))
                digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token));
            var settings = new ProjectSettings
            {
                Version = 2, DataFile = Path.GetFileName(snapshot), DataSha256 = digest, ColorMap = _colorMap.Name,
                PointSize = (int)PointSizeSlider.Value, Background = _background,
                CameraYaw = (float)_yaw, CameraPitch = (float)_pitch, CameraDistance = (float)_distance,
                CameraTargetX = _target.X, CameraTargetY = _target.Y, CameraTargetZ = _target.Z,
                RenderBudget = RenderBudget.IsChecked == true, Measurements = _measurements.Measurements.ToList()
            };
            await ProjectIO.SaveAsync(path, settings, token);
            committed = true;
            _dirty = false;
            await RememberAsync(path);
            Record("保存项目快照");
        }
        finally { if (!committed && File.Exists(snapshot)) File.Delete(snapshot); }
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_cloud == null || IsBusy) return;
        var dialog = new SaveFileDialog { Filter = "PointCloud 项目|*.json", FileName = "scene.json" };
        if (dialog.ShowDialog(this) == true)
            await RunOperationAsync("正在保存项目快照", token => SaveSnapshotAsync(dialog.FileName, token));
    }

    private async void ExportMeasurements_Click(object sender, RoutedEventArgs e)
    {
        if (_measurements.Measurements.Count == 0 || IsBusy) return;
        var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "measurements.csv" };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync("正在导出测量", token => MeasurementCsv.WriteAsync(dialog.FileName, _measurements.Measurements, token));
        if (LastError == null) Record("导出测量 CSV");
    }

    private void Undo_Click(object sender, RoutedEventArgs e) { if (!IsBusy) _commands.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (!IsBusy) _commands.Redo(); }
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || _original == null || _cloud == null || ReferenceEquals(_cloud, _original)) return;
        _commands.Execute(new CloudChangeCommand("恢复原始点云", _cloud, _original, PresentCloud));
    }
    private async void Voxel_Click(object sender, RoutedEventArgs e)
    {
        try { await ApplyFilterAsync(new VoxelGridFilter(Parse(VoxelInput))); } catch (Exception ex) { ReportError(ex); }
    }
    private async void Range_Click(object sender, RoutedEventArgs e)
    {
        try { await ApplyFilterAsync(new RangeFilter(Parse(MinZInput), Parse(MaxZInput))); } catch (Exception ex) { ReportError(ex); }
    }
    private async void Outlier_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(NeighborsInput.Text, out int neighbors)) throw new FormatException("最少邻居必须为正整数。");
            await ApplyFilterAsync(new RadiusOutlierFilter(Parse(RadiusInput), neighbors));
        }
        catch (Exception ex) { ReportError(ex); }
    }
    private static float Parse(TextBox input) =>
        float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value : throw new FormatException("请输入有效数字，小数使用英文句点。");
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "PointCloud Studio 2.0\nSuLi / PointCloudViz_Final\nC# · WPF · HelixToolkit · DirectX 11",
        "关于", MessageBoxButton.OK, MessageBoxImage.Information);

    private void LoadRecent()
    {
        try
        {
            string path = Path.Combine(_stateDirectory, "recent.json");
            if (!File.Exists(path)) return;
            foreach (var file in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))?.Take(5) ?? [])
                if (File.Exists(file)) _recent.Add(new(file));
        }
        catch (Exception ex) { Logger.Warning("Recent files: " + ex.Message); }
    }
    private async Task RememberAsync(string path)
    {
        var previous = _recent.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (previous != null) _recent.Remove(previous);
        _recent.Insert(0, new(path));
        while (_recent.Count > 5) _recent.RemoveAt(_recent.Count - 1);
        try
        {
            Directory.CreateDirectory(_stateDirectory);
            await AtomicFile.WriteAsync(Path.Combine(_stateDirectory, "recent.json"),
                stream => JsonSerializer.SerializeAsync(stream, _recent.Select(r => r.Path).ToArray()));
        }
        catch (Exception ex) { Logger.Warning("Recent files: " + ex.Message); }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsBusy) { if (e.Key == Key.Escape) _operation!.Cancel(); return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.O: Open_Click(this, e); e.Handled = true; return;
                case Key.S: SaveProject_Click(this, e); e.Handled = true; return;
                case Key.Z: Undo_Click(this, e); e.Handled = true; return;
                case Key.Y: Redo_Click(this, e); e.Handled = true; return;
            }
        }
        if (e.OriginalSource is TextBoxBase || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.F) { FitScene(); e.Handled = true; }
        if (e.Key == Key.Escape) { NavigateMode.IsChecked = true; e.Handled = true; }
        if (e.Key == Key.Enter) { FinishMeasurement_Click(this, e); e.Handled = true; }
        if (Viewport.IsKeyboardFocusWithin && e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)
        {
            MoveCamera(e.Key);
            e.Handled = true;
        }
    }

    // The integration harness closes without interactive discard prompts.
    internal void CloseAfterSmokeTest() { _dirty = false; Close(); }
}
