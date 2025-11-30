using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Services
{
    public class ProjectSettings
    {
        public string? DataFile { get; set; }
        public string ColorMap { get; set; } = "Height";
        public int PointSize { get; set; } = 3;
        public string Background { get; set; } = "Black";
        public float CameraYaw { get; set; }
        public float CameraPitch { get; set; }
        public float CameraDistance { get; set; }
        public float CameraTargetX { get; set; }
        public float CameraTargetY { get; set; }
        public float CameraTargetZ { get; set; }
    }

    public static class ProjectIO
    {
        public static async Task SaveAsync(string path, ProjectSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public static async Task<ProjectSettings?> LoadAsync(string path)
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<ProjectSettings>(json);
        }
    }
}
