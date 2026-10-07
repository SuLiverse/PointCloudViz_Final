using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PointCloudViz.App.Infrastructure;
using PointCloudViz.App.Services;
using PointCloudViz.App.ViewModels;
using PointCloudViz.App.Views;
using PointCloudViz.Core.IO;
using PointCloudViz.Core.Viewing;

namespace PointCloudViz.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;
    private readonly string? _fileToOpen;
    private readonly SmokeTest? _smoke;

    public MainWindow(AppSettings settings, string? fileToOpen, SmokeTest? smoke)
    {
        InitializeComponent();
        _settings = settings;
        _fileToOpen = fileToOpen;
        _smoke = smoke;
        _vm = new MainViewModel(new DialogService(() => IsLoaded ? this : null), settings);
        DataContext = _vm;

        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        if (settings.WindowMaximized && smoke is null) WindowState = WindowState.Maximized;

        Viewport.PointClicked += (_, e) => _vm.OnPointClicked(e.World);
        Viewport.RightClicked += (_, _) => _vm.OnRightClicked();
        Viewport.DoubleClicked += (_, e) => e.Handled = _vm.OnDoubleClicked();
        Viewport.HoverChanged += (_, world) => _vm.OnHover(world);
        Viewport.PreviewKeyDown += Viewport_PreviewKeyDown;
        PreviewKeyDown += Window_PreviewKeyDown;

        DragEnter += OnDragEnter;
        DragOver += OnDragEnter;
        DragLeave += (_, _) => DropHint.Visibility = Visibility.Collapsed;
        Drop += OnDrop;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public bool IsSmokeTest => _smoke is not null;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.AttachScene(Viewport);
        if (Viewport.InitializationError is { } error && _smoke is null)
        {
            MessageBox.Show(this, $"三维渲染引擎初始化失败：{error}\n\n可能原因：显卡驱动过旧或不支持 DirectX 11。", "PointCloudViz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (_smoke is not null)
        {
            await RunSmokeTestAsync(_smoke);
            return;
        }
        if (_fileToOpen is not null) await _vm.OpenPathAsync(_fileToOpen);
    }

    /// <summary>CI 冒烟测试：加载 → 等待若干帧 → 截图 → 以退出码报告结果。</summary>
    private async Task RunSmokeTestAsync(SmokeTest smoke)
    {
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        timeout.Tick += (_, _) =>
        {
            Console.Error.WriteLine("smoke test timed out");
            Application.Current.Shutdown(2);
        };
        timeout.Start();
        try
        {
            if (Viewport.InitializationError is { } error) throw new InvalidOperationException(error);
            if (!await _vm.OpenPathAsync(smoke.Input)) throw new InvalidOperationException("failed to open input");
            _vm.SetViewCommand.Execute(ViewPreset.Isometric);
            await Task.Delay(TimeSpan.FromSeconds(3)); // 等待相机动画与若干帧渲染完成
            if (!Viewport.SaveScreenshot(smoke.Output)) throw new InvalidOperationException("screenshot failed");

            // 截图不能只有背景色，否则说明点云并未真正渲染出来
            double coverage = CoveredFraction(smoke.Output);
            Log.Info($"冒烟测试：{_vm.PointCount} 点，画面覆盖率 {coverage:P1}");
            if (coverage < 0.02) throw new InvalidOperationException($"rendered image is almost empty ({coverage:P2})");
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            Log.Error("冒烟测试失败", ex);
            Console.Error.WriteLine(ex);
            Application.Current.Shutdown(1);
        }
    }

    /// <summary>与左上角像素（背景）颜色明显不同的像素所占比例。</summary>
    private static double CoveredFraction(string pngPath)
    {
        var frame = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(pngPath), System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var bitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        var pixels = new byte[w * h * 4];
        bitmap.CopyPixels(pixels, w * 4, 0);
        int covered = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int diff = Math.Abs(pixels[i] - pixels[0]) + Math.Abs(pixels[i + 1] - pixels[1]) + Math.Abs(pixels[i + 2] - pixels[2]);
            if (diff > 24) covered++;
        }
        return covered / (double)(w * h);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_smoke is not null) return;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        _vm.SaveSettings();
    }

    #region 键盘

    /// <summary>视口获得焦点时的单键快捷键（不能用 KeyBinding，否则会吞掉文本框输入）。</summary>
    private void Viewport_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Viewport.HandleKey(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        ViewPreset? preset = e.Key switch
        {
            Key.D1 or Key.NumPad1 => ViewPreset.Top,
            Key.D2 or Key.NumPad2 => ViewPreset.Front,
            Key.D3 or Key.NumPad3 => ViewPreset.Left,
            Key.D4 or Key.NumPad4 => ViewPreset.Right,
            Key.D5 or Key.NumPad5 => ViewPreset.Back,
            Key.D6 or Key.NumPad6 => ViewPreset.Bottom,
            Key.D0 or Key.NumPad0 => ViewPreset.Isometric,
            _ => null,
        };
        if (preset is { } p)
        {
            _vm.SetViewCommand.Execute(p);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.R or Key.F:
                _vm.FitViewCommand.Execute(null);
                break;
            case Key.Enter:
                _vm.FinishMeasurementCommand.Execute(null);
                break;
            case Key.Back:
                _vm.RemoveLastPointCommand.Execute(null);
                break;
            case Key.Delete when _vm.SelectedMeasurement is { } m:
                _vm.DeleteMeasurementCommand.Execute(m);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            Shortcuts_Click(this, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox)
        {
            if (_vm.HasPending) _vm.CancelPendingCommand.Execute(null);
            else _vm.ToolMode = ToolMode.Navigate;
            e.Handled = true;
        }
    }

    #endregion

    #region 拖放

    private static string? GetDroppedFile(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files ? files[0] : null;

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var file = GetDroppedFile(e);
        bool ok = file is not null && (PointCloudIO.CanRead(file) || Path.GetExtension(file).ToLowerInvariant() is ".pcvproj" or ".json");
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (GetDroppedFile(e) is { } file) await _vm.OpenPathAsync(file);
    }

    #endregion

    #region 菜单

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void Shortcuts_Click(object sender, RoutedEventArgs e) => new ShortcutsWindow { Owner = this }.ShowDialog();

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Directory);
        Process.Start(new ProcessStartInfo(Log.Directory) { UseShellExecute = true });
    }

    #endregion
}
