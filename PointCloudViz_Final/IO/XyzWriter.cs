using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO
{
    public static class XyzWriter
    {
        public static async Task WriteAsync(PointCloud cloud, string path)
        {
            var ci = CultureInfo.InvariantCulture;
            using var sw = new StreamWriter(path);
            foreach (var p in cloud.Points)
            {
                await sw.WriteLineAsync(string.Format(ci, "{0} {1} {2} {3}", p.X, p.Y, p.Z, p.Intensity));
            }
        }
    }
}
