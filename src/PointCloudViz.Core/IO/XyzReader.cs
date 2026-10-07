using System.Buffers;
using System.Globalization;
using System.Text;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>
/// 文本点云读取器（.xyz / .txt / .csv / .pts / .asc）。
/// <para>列布局由第一行数据的列数决定：</para>
/// <list type="bullet">
/// <item>3 列：x y z</item>
/// <item>4 列：x y z intensity</item>
/// <item>6 列：x y z r g b</item>
/// <item>7 列及以上：x y z intensity r g b（其余列忽略）</item>
/// </list>
/// 分隔符支持空格、制表符、逗号、分号；<c>#</c>、<c>//</c> 开头的行和无法解析的表头行会被跳过。
/// </summary>
public sealed class XyzReader : IPointCloudReader
{
    private static readonly SearchValues<char> Separators = SearchValues.Create(" \t,;");

    public string FormatName => "文本点云 (XYZ)";

    public IReadOnlyList<string> Extensions { get; } = [".xyz", ".txt", ".csv", ".pts", ".asc"];

    public PointCloud Read(Stream stream, PointCloudReadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= PointCloudReadOptions.Default;
        var reporter = new ProgressReporter(progress);
        long totalBytes = stream.CanSeek ? stream.Length : 0;

        // 文本格式无法预知点数：采样文件开头估算平均行长，进而估算总行数与抽稀步长
        long estimatedLines = totalBytes > 0 ? totalBytes / EstimateLineLength(stream) : 0;
        int step = 1;
        if (options.MaxPoints is int max && estimatedLines > max)
            step = (int)Math.Ceiling(estimatedLines / (double)max);

        var builder = new PointCloudBuilder((int)Math.Clamp(estimatedLines / step, 1024, 16_000_000));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16, leaveOpen: true);

        Span<double> values = stackalloc double[7];
        int layout = 0;
        long lineNo = 0;
        long dataLines = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if ((lineNo & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (totalBytes > 0) reporter.Report(stream.Position, totalBytes);
            }

            var span = line.AsSpan().Trim();
            if (span.IsEmpty || span[0] == '#' || span.StartsWith("//")) continue;

            int columns = ParseColumns(span, values);
            if (columns < 3) continue; // 表头或无效行

            if (layout == 0)
            {
                layout = columns >= 7 ? 7 : columns >= 6 ? 6 : columns >= 4 ? 4 : 3;
                builder.AddAttributes(layout switch
                {
                    7 => PointAttributes.Intensity | PointAttributes.Color,
                    6 => PointAttributes.Color,
                    4 => PointAttributes.Intensity,
                    _ => PointAttributes.None,
                });
            }
            if (columns < layout) continue;

            if (dataLines++ % step != 0) continue;

            float intensity = layout is 4 or 7 ? (float)values[3] : 0f;
            Rgb24 color = layout switch
            {
                6 => new Rgb24(ToByte(values[3]), ToByte(values[4]), ToByte(values[5])),
                7 => new Rgb24(ToByte(values[4]), ToByte(values[5]), ToByte(values[6])),
                _ => default,
            };
            builder.Add(values[0], values[1], values[2], intensity, color);
        }

        reporter.Report(1);
        return builder.Build();
    }

    private static long EstimateLineLength(Stream stream)
    {
        if (!stream.CanSeek) return 32;
        long start = stream.Position;
        var buffer = new byte[64 * 1024];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        stream.Position = start;
        int lines = buffer.AsSpan(0, read).Count((byte)'\n');
        return lines == 0 ? Math.Max(1, read) : Math.Max(1, read / lines);
    }

    /// <summary>解析最多 7 个数值列，返回成功解析的连续列数（遇到非数字即停止）。</summary>
    private static int ParseColumns(ReadOnlySpan<char> line, Span<double> values)
    {
        int count = 0;
        while (!line.IsEmpty && count < values.Length)
        {
            int sep = line.IndexOfAny(Separators);
            var token = sep < 0 ? line : line[..sep];
            line = sep < 0 ? default : line[(sep + 1)..];
            if (token.IsEmpty) continue; // 连续分隔符

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out values[count]))
                break;
            count++;
        }
        return count;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
