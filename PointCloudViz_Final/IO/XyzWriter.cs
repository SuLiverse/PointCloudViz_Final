using System.Globalization;
using System.IO;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO;

public static class XyzWriter
{
    public static Task WriteAsync(PointCloud cloud, string path, CancellationToken token = default)
        => AtomicFile.WriteAsync(path, async stream =>
        {
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 65536, leaveOpen: true);
            await writer.WriteLineAsync("# X Y Z Intensity R G B");
            foreach (var p in cloud.Points)
            {
                token.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"{p.X:R} {p.Y:R} {p.Z:R} {p.Intensity:R} {p.Color.R} {p.Color.G} {p.Color.B}"));
            }
            await writer.FlushAsync(token);
        }, token);
}
