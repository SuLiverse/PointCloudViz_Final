using System.IO;
using System.Text;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO;

public static class PlyWriter
{
    public static Task WriteAsync(PointCloud cloud, string path, CancellationToken token = default)
        => AtomicFile.WriteAsync(path, stream =>
        {
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes($"ply\nformat binary_little_endian 1.0\nelement vertex {cloud.Count}\n" +
                "property float x\nproperty float y\nproperty float z\nproperty float intensity\n" +
                "property uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n"));
            foreach (var p in cloud.Points)
            {
                token.ThrowIfCancellationRequested();
                writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); writer.Write(p.Intensity);
                writer.Write(p.Color.R); writer.Write(p.Color.G); writer.Write(p.Color.B);
            }
            writer.Flush();
            return Task.CompletedTask;
        }, token);
}
