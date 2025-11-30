using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO
{
    public class XyzReader : IPointReader
    {
        public string Name => "XYZ Reader";
        public bool CanRead(string extension) => extension.ToLowerInvariant() == ".xyz" || extension.ToLowerInvariant() == ".txt";

        public async Task<PointCloud> ReadAsync(string path, CancellationToken token)
        {
            // 优化：先估算文件大小以预分配容量
            var fileInfo = new FileInfo(path);
            long estimatedLines = fileInfo.Length / 50; // 粗略估算每行50字节
            var pts = new List<PointRecord>(capacity: (int)Math.Min(estimatedLines, 2_000_000));
            
            var ci = CultureInfo.InvariantCulture;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536);
            using var sr = new StreamReader(fs, System.Text.Encoding.UTF8, true, 65536);
            string? line;
            int lineCount = 0;
            
            while ((line = await sr.ReadLineAsync()) != null)
            {
                if (++lineCount % 10000 == 0) token.ThrowIfCancellationRequested();
                
                line = line.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                
                var sp = line.Split(new[]{' ', ',', '\t', ';'}, System.StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length < 3) continue;
                
                // 使用TryParse避免异常开销
                if (!float.TryParse(sp[0], NumberStyles.Float, ci, out float x)) continue;
                if (!float.TryParse(sp[1], NumberStyles.Float, ci, out float y)) continue;
                if (!float.TryParse(sp[2], NumberStyles.Float, ci, out float z)) continue;
                
                float intensity = 0;
                if (sp.Length >= 4) float.TryParse(sp[3], NumberStyles.Any, ci, out intensity);
                
                Color? color = null;
                if (sp.Length >= 6)
                {
                    if (byte.TryParse(sp[^3], NumberStyles.Integer, ci, out byte r) &&
                        byte.TryParse(sp[^2], NumberStyles.Integer, ci, out byte g) &&
                        byte.TryParse(sp[^1], NumberStyles.Integer, ci, out byte b))
                    {
                        color = Color.FromRgb(r, g, b);
                    }
                }
                pts.Add(new PointRecord(x, y, z, intensity, color));
            }
            return new PointCloud(pts);
        }
    }
}
