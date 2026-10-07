using System.Diagnostics;
using System.IO;

namespace PointCloudViz.App.Infrastructure;

/// <summary>
/// 轻量文件日志，按天滚动写入 %LocalAppData%\PointCloudViz\logs。
/// 旧版把日志写在当前工作目录，既可能没有写权限，也会被误提交到仓库。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PointCloudViz", "logs");

    public static string CurrentFile => Path.Combine(Directory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Debug.WriteLine(line);
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(CurrentFile, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // 日志失败不能影响主流程
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
