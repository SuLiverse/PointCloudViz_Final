using System.Globalization;
using System.Text;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>
/// 文本点云写出器。输出世界坐标（双精度，默认保留 3 位小数即毫米级），
/// 列布局与 <see cref="XyzReader"/> 对应，写出后可无损读回。
/// </summary>
public sealed class XyzWriter : IPointCloudWriter
{
    public XyzWriter(int decimals = 3)
    {
        Decimals = Math.Clamp(decimals, 0, 9);
    }

    public int Decimals { get; }

    public string FormatName => "文本点云 (XYZ)";

    public IReadOnlyList<string> Extensions { get; } = [".xyz", ".txt"];

    public void Write(PointCloud cloud, Stream stream, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var reporter = new ProgressReporter(progress);
        var ci = CultureInfo.InvariantCulture;
        var fmt = "F" + Decimals.ToString(ci);
        bool hasColor = cloud.Has(PointAttributes.Color);
        bool hasIntensity = cloud.Has(PointAttributes.Intensity);

        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true);
        writer.WriteLine(hasColor ? "# x y z intensity r g b" : hasIntensity ? "# x y z intensity" : "# x y z");

        Span<char> buffer = stackalloc char[128];
        var points = cloud.Points;
        for (int i = 0; i < points.Length; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reporter.Report(i, points.Length);
            }

            ref readonly var p = ref points[i];
            var w = cloud.ToWorld(p.Position);
            int n = 0;
            Append(buffer, ref n, w.X, fmt, ci);
            Append(buffer, ref n, w.Y, fmt, ci);
            Append(buffer, ref n, w.Z, fmt, ci);
            if (hasColor || hasIntensity) Append(buffer, ref n, p.Intensity, "G7", ci);
            if (hasColor)
            {
                Append(buffer, ref n, p.Color.R, "D", ci);
                Append(buffer, ref n, p.Color.G, "D", ci);
                Append(buffer, ref n, p.Color.B, "D", ci);
            }
            writer.Write(buffer[..(n - 1)]);
            writer.Write('\n');
        }
        reporter.Report(1);
    }

    private static void Append<T>(Span<char> buffer, ref int n, T value, string format, IFormatProvider ci) where T : ISpanFormattable
    {
        value.TryFormat(buffer[n..], out int written, format, ci);
        n += written;
        buffer[n++] = ' ';
    }
}
