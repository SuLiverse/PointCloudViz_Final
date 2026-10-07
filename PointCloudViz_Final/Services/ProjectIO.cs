using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using PointCloudViz_Final.IO;
using PointCloudViz_Final.Tools;

namespace PointCloudViz_Final.Services;

public class ProjectSettings
{
    public int Version { get; set; } = 1;
    public string? DataFile { get; set; }
    public string? DataSha256 { get; set; }
    public string ColorMap { get; set; } = "Height";
    public int PointSize { get; set; } = 3;
    public string Background { get; set; } = "#171B1E";
    public float CameraYaw { get; set; } = -55;
    public float CameraPitch { get; set; } = 32;
    public float CameraDistance { get; set; }
    public float CameraTargetX { get; set; }
    public float CameraTargetY { get; set; }
    public float CameraTargetZ { get; set; }
    public bool RenderBudget { get; set; } = true;
    public List<Measurement> Measurements { get; set; } = new();
}

public static class ProjectIO
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IncludeFields = true };

    public static Task SaveAsync(string path, ProjectSettings settings, CancellationToken token = default)
    {
        Validate(settings);
        return AtomicFile.WriteAsync(path,
            stream => JsonSerializer.SerializeAsync(stream, settings, Options, token), token);
    }

    public static async Task<ProjectSettings> LoadAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<ProjectSettings>(stream, Options, token)
            ?? throw new InvalidDataException("Empty project.");
        Validate(settings);
        if (!string.IsNullOrWhiteSpace(settings.DataFile))
            settings.DataFile = Path.GetFullPath(settings.DataFile, Path.GetDirectoryName(Path.GetFullPath(path))!);
        return settings;
    }

    public static async Task VerifyDataAsync(ProjectSettings settings, CancellationToken token = default)
    {
        if (settings.DataSha256 == null) return;
        await using var data = File.OpenRead(settings.DataFile ?? throw new InvalidDataException("Missing snapshot data."));
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(data, token));
        if (!hash.Equals(settings.DataSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Project data checksum mismatch. The snapshot companion file has changed.");
    }

    private static void Validate(ProjectSettings settings)
    {
        if (settings.Version is < 1 or > 2) throw new NotSupportedException("Unsupported project version.");
        if (settings.DataSha256 != null && (settings.DataSha256.Length != 64 || settings.DataSha256.Any(c => !Uri.IsHexDigit(c))))
            throw new InvalidDataException("Invalid snapshot checksum.");
        if (settings.PointSize is < 1 or > 8 || settings.ColorMap is not ("Height" or "Intensity" or "RGB"))
            throw new InvalidDataException("Invalid project display settings.");
        if (new[] { settings.CameraYaw, settings.CameraPitch, settings.CameraDistance,
            settings.CameraTargetX, settings.CameraTargetY, settings.CameraTargetZ }.Any(v => !float.IsFinite(v))
            || settings.CameraDistance < 0 || Math.Abs(settings.CameraPitch) > 89.9)
            throw new InvalidDataException("Invalid project camera.");
        if (settings.Measurements == null || settings.Measurements.Any(m => m == null || !Enum.IsDefined(m.Type) ||
            !float.IsFinite(m.Value) || m.Value < 0 || m.Points == null || m.Points.Count < (m.Type == MeasurementType.Distance ? 2 : 3) ||
            m.Points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))))
            throw new InvalidDataException("Invalid project measurements.");
    }
}
