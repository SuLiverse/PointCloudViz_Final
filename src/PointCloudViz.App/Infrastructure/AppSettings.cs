using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointCloudViz.Core.Coloring;

namespace PointCloudViz.App.Infrastructure;

/// <summary>用户偏好设置，保存在 %AppData%\PointCloudViz\settings.json。</summary>
public sealed class AppSettings
{
    public const int MaxRecentFiles = 10;

    public List<string> RecentFiles { get; set; } = [];
    public string Palette { get; set; } = Palettes.Viridis.Id;
    public double PointSize { get; set; } = 2;
    public string Background { get; set; } = "#1E1F24";

    /// <summary>显示点数上限（超过时随机抽稀显示，数据本身不受影响）。</summary>
    public int DisplayBudget { get; set; } = 3_000_000;

    /// <summary>读取时的点数上限，0 表示不限制。</summary>
    public int LoadLimit { get; set; }

    public bool ShowAxes { get; set; } = true;
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }
}

/// <summary>设置的加载与保存。读取失败时回退到默认值，绝不让损坏的配置文件阻止程序启动。</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PointCloudViz");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning($"读取设置失败，使用默认设置：{ex.Message}");
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"保存设置失败：{ex.Message}");
        }
    }
}
