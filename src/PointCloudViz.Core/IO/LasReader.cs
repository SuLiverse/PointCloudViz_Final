using System.Buffers.Binary;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>
/// LAS 读取器：支持 LAS 1.0 ~ 1.4、点数据格式 0 ~ 10，按块批量解析。
/// <para>
/// 相比旧版实现的改进：支持 1.4 扩展点数与 6~10 格式；保留分类码（不再静默丢弃噪声点）；
/// 自动识别"8 位颜色写在 16 位字段里"的非标准文件；以头部最小值作为局部原点，保证坐标精度。
/// </para>
/// </summary>
public sealed class LasReader : IPointCloudReader
{
    public string FormatName => "LAS 点云";

    public IReadOnlyList<string> Extensions { get; } = [".las"];

    public PointCloud Read(Stream stream, PointCloudReadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= PointCloudReadOptions.Default;
        var header = LasHeader.Read(stream);
        if (header.IsCompressed)
            throw new NotSupportedException("暂不支持 LAZ 压缩格式，请先使用 LAStools / PDAL 解压为 LAS。");

        byte format = header.PointFormat;
        int minLength = LasHeader.MinRecordLength(format);
        int recordLength = header.PointRecordLength;
        if (recordLength < minLength)
            throw new InvalidDataException($"点记录长度 {recordLength} 小于格式 {format} 的最小长度 {minLength}。");

        long count = (long)Math.Min(header.PointCount, long.MaxValue);
        long available = stream.CanSeek ? (stream.Length - header.OffsetToPointData) / recordLength : count;
        count = Math.Min(count, available);

        stream.Seek(header.OffsetToPointData, SeekOrigin.Begin);

        int step = options.MaxPoints is int max && count > max ? (int)Math.Ceiling(count / (double)max) : 1;
        long kept = (count + step - 1) / step;
        // 以头部最小值为原点；部分软件不填写 Min/Max，此时让构建器以首点为原点
        Double3? origin = header.Min == header.Max
            ? null
            : new Double3(Math.Round(header.Min.X), Math.Round(header.Min.Y), Math.Round(header.Min.Z));
        var builder = new PointCloudBuilder((int)Math.Min(kept, int.MaxValue - 64), origin);

        int colorOffset = LasHeader.ColorOffset(format);
        bool extended = format >= 6;
        builder.AddAttributes(PointAttributes.Intensity | PointAttributes.Classification |
                              (colorOffset >= 0 ? PointAttributes.Color : 0));

        // 先保存 16 位原始颜色，读完再判断是否需要右移 8 位
        var rawColors = colorOffset >= 0 ? new ushort[kept * 3] : null;
        ushort maxColor = 0;

        var scale = header.Scale;
        var offset = header.Offset;
        var reporter = new ProgressReporter(progress);
        const int batch = 16384;
        var buffer = new byte[recordLength * batch];
        long keptIndex = 0;

        for (long i = 0; i < count;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.Report(i, count);
            int n = (int)Math.Min(batch, count - i);
            stream.ReadExactly(buffer, 0, n * recordLength);

            for (int k = 0; k < n; k++, i++)
            {
                if (i % step != 0) continue;
                var r = buffer.AsSpan(k * recordLength, recordLength);

                double x = BinaryPrimitives.ReadInt32LittleEndian(r) * scale.X + offset.X;
                double y = BinaryPrimitives.ReadInt32LittleEndian(r[4..]) * scale.Y + offset.Y;
                double z = BinaryPrimitives.ReadInt32LittleEndian(r[8..]) * scale.Z + offset.Z;
                ushort intensity = BinaryPrimitives.ReadUInt16LittleEndian(r[12..]);
                byte classification = extended ? r[16] : (byte)(r[15] & 0x1F);

                if (rawColors is not null)
                {
                    var c = r[colorOffset..];
                    ushort red = BinaryPrimitives.ReadUInt16LittleEndian(c);
                    ushort green = BinaryPrimitives.ReadUInt16LittleEndian(c[2..]);
                    ushort blue = BinaryPrimitives.ReadUInt16LittleEndian(c[4..]);
                    rawColors[keptIndex * 3] = red;
                    rawColors[keptIndex * 3 + 1] = green;
                    rawColors[keptIndex * 3 + 2] = blue;
                    maxColor = Math.Max(maxColor, Math.Max(red, Math.Max(green, blue)));
                }

                builder.Add(x, y, z, intensity, default, classification);
                keptIndex++;
            }
        }

        if (rawColors is not null)
        {
            int shift = maxColor > 255 ? 8 : 0;
            var points = builder.Points;
            for (int i = 0; i < points.Length; i++)
            {
                points[i].Color = new Rgb24(
                    (byte)(rawColors[i * 3] >> shift),
                    (byte)(rawColors[i * 3 + 1] >> shift),
                    (byte)(rawColors[i * 3 + 2] >> shift));
            }
        }

        reporter.Report(1);
        return builder.Build();
    }

    /// <summary>只读取头部信息（用于 CLI 的 info 命令等）。</summary>
    public static LasHeader ReadHeader(string path)
    {
        using var stream = File.OpenRead(path);
        return LasHeader.Read(stream);
    }
}
