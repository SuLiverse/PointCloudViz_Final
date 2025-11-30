using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO
{
    public class PlyReader : IPointReader
    {
        public string Name => "PLY ASCII Reader";
        public bool CanRead(string extension) => extension.ToLowerInvariant() == ".ply";

        public async Task<PointCloud> ReadAsync(string path, CancellationToken token)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536);
            using var sr = new StreamReader(fs, System.Text.Encoding.UTF8, true, 65536);
            string? line = await sr.ReadLineAsync();
            if (line == null || !line.StartsWith("ply")) throw new IOException("Not a PLY file");
            bool ascii = false;
            int vertexCount = 0;
            var props = new List<string>();
            while ((line = await sr.ReadLineAsync()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("format ascii")) ascii = true;
                if (line.StartsWith("element vertex"))
                {
                    var sp = line.Split(' ');
                    if (sp.Length >= 3 && int.TryParse(sp[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                        vertexCount = count;
                }
                if (line.StartsWith("property"))
                {
                    var sp = line.Split(' ');
                    if (sp.Length > 0) props.Add(sp[^1]);
                }
                if (line.StartsWith("end_header")) break;
            }
            if (!ascii) throw new IOException("Only ASCII PLY supported");
            var pts = new List<PointRecord>(vertexCount > 0 ? vertexCount : 100_000);
            var ci = CultureInfo.InvariantCulture;
            for (int i = 0; i < vertexCount; i++)
            {
                if (i % 10000 == 0) token.ThrowIfCancellationRequested();
                
                line = await sr.ReadLineAsync();
                if (line == null) break;
                var sp = line.Split(new[]{' ', '\t'}, System.StringSplitOptions.RemoveEmptyEntries);
                float x=0,y=0,z=0,intensity=0;
                byte r=255,g=255,b=255;
                for (int k = 0; k < props.Count && k < sp.Length; k++)
                {
                    var name = props[k].ToLowerInvariant();
                    var v = sp[k];
                    switch (name)
                    {
                        case "x": float.TryParse(v, NumberStyles.Float, ci, out x); break;
                        case "y": float.TryParse(v, NumberStyles.Float, ci, out y); break;
                        case "z": float.TryParse(v, NumberStyles.Float, ci, out z); break;
                        case "intensity": float.TryParse(v, NumberStyles.Float, ci, out intensity); break;
                        case "red": byte.TryParse(v, NumberStyles.Integer, ci, out r); break;
                        case "green": byte.TryParse(v, NumberStyles.Integer, ci, out g); break;
                        case "blue": byte.TryParse(v, NumberStyles.Integer, ci, out b); break;
                    }
                }
                pts.Add(new PointRecord(x,y,z,intensity, Color.FromRgb(r,g,b)));
            }
            return new PointCloud(pts);
        }
    }
}
