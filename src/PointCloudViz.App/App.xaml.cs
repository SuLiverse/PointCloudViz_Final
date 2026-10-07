using System.IO;
using System.Windows;
using System.Windows.Threading;
using PointCloudViz.App.Infrastructure;

namespace PointCloudViz.App;

/// <summary>
/// 应用入口。
/// <list type="bullet">
/// <item><c>PointCloudViz.exe 文件路径</c>：启动后直接打开点云或项目（支持文件关联/拖到 exe 上）。</item>
/// <item><c>PointCloudViz.exe --smoke-test 输入 输出.png</c>：自动化冒烟测试，加载数据、渲染并截图后退出（CI 使用）。</item>
/// </list>
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("未处理的异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("未观察到的任务异常", args.Exception);
            args.SetObserved();
        };

        Log.Info($"启动 PointCloudViz {typeof(App).Assembly.GetName().Version}，参数：{string.Join(' ', e.Args)}");
        var settings = SettingsStore.Load();

        SmokeTest? smoke = null;
        string? fileToOpen = null;
        if (e.Args.Length >= 3 && e.Args[0] == "--smoke-test")
            smoke = new SmokeTest(Path.GetFullPath(e.Args[1]), Path.GetFullPath(e.Args[2]));
        else if (e.Args.Length >= 1 && File.Exists(e.Args[0]))
            fileToOpen = Path.GetFullPath(e.Args[0]);

        var window = new MainWindow(settings, fileToOpen, smoke);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("界面线程未处理的异常", e.Exception);
        if (MainWindow is MainWindow { IsSmokeTest: true })
        {
            Console.Error.WriteLine(e.Exception);
            Shutdown(1);
            return;
        }
        MessageBox.Show($"发生了未预期的错误：\n{e.Exception.Message}\n\n日志位置：{Log.CurrentFile}", "PointCloudViz",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

/// <summary>冒烟测试参数。</summary>
public sealed record SmokeTest(string Input, string Output);
