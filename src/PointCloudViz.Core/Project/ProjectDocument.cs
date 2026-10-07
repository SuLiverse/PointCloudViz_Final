using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PointCloudViz.Core.Coloring;
using PointCloudViz.Core.Data;
using PointCloudViz.Core.Measurements;

namespace PointCloudViz.Core.Project;

/// <summary>
/// 项目文件（.pcvproj / .json）。第 2 版保存：数据文件（相对路径优先）、显示设置、相机与全部量测结果。
/// 可以读取旧版（第 1 版）只含路径与显示设置的 JSON。
/// </summary>
public sealed record ProjectDocument
{
    public const int CurrentVersion = 2;

    public int FormatVersion { get; init; } = CurrentVersion;

    /// <summary>点云文件路径；保存时若与项目文件同盘则写为相对路径。</summary>
    public string? DataFile { get; init; }

    public DisplaySettings Display { get; init; } = new();

    public CameraState? Camera { get; init; }

    public IReadOnlyList<MeasurementRecord> Measurements { get; init; } = [];
}

public sealed record DisplaySettings
{
    public ColorMode ColorMode { get; init; } = ColorMode.Elevation;
    public string Palette { get; init; } = Palettes.Viridis.Id;
    public double? RangeMin { get; init; }
    public double? RangeMax { get; init; }
    public double PointSize { get; init; } = 2;
    public string Background { get; init; } = "#1E1F24";
    public bool ShowAxes { get; init; } = true;
}

/// <summary>相机状态（目标点为世界坐标）。</summary>
public sealed record CameraState(double[] Target, float Yaw, float Pitch, float Distance);

public sealed record MeasurementRecord(MeasurementKind Kind, double[][] Points)
{
    public static MeasurementRecord From(Measurement m) =>
        new(m.Kind, m.Points.Select(p => p.ToArray()).ToArray());

    public Measurement ToMeasurement(int id) =>
        new(id, Kind, Points.Where(p => p.Length >= 3).Select(p => new Double3(p[0], p[1], p[2])).ToArray());
}

/// <summary>项目文件读写（含旧版迁移）。</summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ProjectDocument document, string? projectPath = null)
    {
        var doc = document;
        if (projectPath is not null && doc.DataFile is not null)
            doc = doc with { DataFile = MakeRelative(projectPath, doc.DataFile) };
        return JsonSerializer.Serialize(doc, Options);
    }

    /// <summary>反序列化；<paramref name="projectPath"/> 用于把相对路径还原为绝对路径。</summary>
    public static ProjectDocument Deserialize(string json, string? projectPath = null)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("项目文件格式错误。");
        var doc = GetInt(node, "formatVersion") is >= 2
            ? node.Deserialize<ProjectDocument>(Options) ?? throw new InvalidDataException("项目文件内容为空。")
            : MigrateV1(node);

        if (projectPath is not null && doc.DataFile is { } data && !Path.IsPathRooted(data))
        {
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
            doc = doc with { DataFile = Path.GetFullPath(Path.Combine(baseDir, data)) };
        }
        return doc;
    }

    public static async Task SaveAsync(string path, ProjectDocument document, CancellationToken cancellationToken = default) =>
        await File.WriteAllTextAsync(path, Serialize(document, path), cancellationToken);

    public static async Task<ProjectDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Deserialize(await File.ReadAllTextAsync(path, cancellationToken), path);

    /// <summary>旧版格式：{ DataFile, ColorMap: "Height"|"Intensity", PointSize, Background: "Black"|"White"|"Gray", Camera* }。</summary>
    private static ProjectDocument MigrateV1(JsonObject node)
    {
        string? colorMap = GetString(node, "ColorMap");
        string? background = GetString(node, "Background");
        return new ProjectDocument
        {
            DataFile = GetString(node, "DataFile"),
            Display = new DisplaySettings
            {
                ColorMode = string.Equals(colorMap, "Intensity", StringComparison.OrdinalIgnoreCase) ? ColorMode.Intensity : ColorMode.Elevation,
                Palette = Palettes.Rainbow.Id, // 旧版只有彩虹色带
                PointSize = GetInt(node, "PointSize") ?? 3,
                Background = background?.ToLowerInvariant() switch
                {
                    "white" => "#FFFFFF",
                    "gray" => "#808080",
                    _ => "#000000",
                },
            },
        };
    }

    private static JsonNode? Get(JsonObject node, string name) =>
        node.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? GetString(JsonObject node, string name) =>
        Get(node, name) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? GetInt(JsonObject node, string name) =>
        Get(node, name) is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    private static string MakeRelative(string projectPath, string dataFile)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
        var full = Path.GetFullPath(dataFile);
        var relative = Path.GetRelativePath(baseDir, full);
        // 不同盘符时 GetRelativePath 会返回绝对路径
        return Path.IsPathRooted(relative) ? full : relative;
    }
}
