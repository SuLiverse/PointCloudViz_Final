using System.Buffers.Binary;
using System.Text;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>
/// LAS 写出器：LAS 1.2 / 点格式 2（含 RGB，兼容性最好）或 LAS 1.4 / 点格式 7。
/// 默认自动选择——点格式 0~5 的分类字段只有 5 位，若存在大于 31 的分类码则自动升级为 1.4 / 格式 7，
/// 避免分类被截断。坐标以 0.001（毫米）比例量化，偏移量取包围盒最小值取整。
/// </summary>
public sealed class LasWriter(byte? pointFormat = null) : IPointCloudWriter
{
    /// <summary>指定的点格式；<c>null</c> 表示自动选择。</summary>
    public byte? PointFormat { get; } = pointFormat is null or 2 or 7
        ? pointFormat
        : throw new ArgumentOutOfRangeException(nameof(pointFormat), "仅支持写出点格式 2 或 7。");

    /// <summary>根据点云内容决定实际使用的点格式。</summary>
    public byte ResolveFormat(PointCloud cloud)
    {
        if (PointFormat is { } f) return f;
        foreach (ref readonly var p in cloud.Points)
        {
            if (p.Classification > 31) return 7;
        }
        return 2;
    }

    public string FormatName => "LAS 点云";

    public IReadOnlyList<string> Extensions { get; } = [".las"];

    public void Write(PointCloud cloud, Stream stream, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        byte format = ResolveFormat(cloud);
        bool v14 = format == 7;
        int headerSize = v14 ? LasHeader.Size14 : LasHeader.Size12;
        int recordLength = LasHeader.MinRecordLength(format);
        var reporter = new ProgressReporter(progress);

        var min = cloud.WorldMin;
        var max = cloud.WorldMax;
        var offset = new Double3(Math.Floor(min.X), Math.Floor(min.Y), Math.Floor(min.Z));
        double extent = Math.Max(max.X - offset.X, Math.Max(max.Y - offset.Y, max.Z - offset.Z));
        double s = 0.001;
        while (extent / s > int.MaxValue * 0.95) s *= 10;
        var scale = new Double3(s, s, s);

        // 文本格式读入的强度常为 0~1 浮点，写入 LAS 时映射到 16 位整数
        float maxIntensity = 0;
        foreach (ref readonly var p in cloud.Points) maxIntensity = Math.Max(maxIntensity, p.Intensity);
        float intensityScale = maxIntensity is > 0 and <= 1 ? 65535f : 1f;

        var header = new byte[headerSize];
        var h = header.AsSpan();
        "LASF"u8.CopyTo(h);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], (ushort)(v14 ? 0x10 : 0)); // 1.4 + PDRF7 要求 WKT 位
        h[24] = 1;
        h[25] = (byte)(v14 ? 4 : 2);
        WriteString(h.Slice(26, 32), "PointCloudViz");
        WriteString(h.Slice(58, 32), "PointCloudViz 2.0");
        var now = DateTime.UtcNow;
        BinaryPrimitives.WriteUInt16LittleEndian(h[90..], (ushort)now.DayOfYear);
        BinaryPrimitives.WriteUInt16LittleEndian(h[92..], (ushort)now.Year);
        BinaryPrimitives.WriteUInt16LittleEndian(h[94..], (ushort)headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[96..], (uint)headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[100..], 0);
        h[104] = format;
        BinaryPrimitives.WriteUInt16LittleEndian(h[105..], (ushort)recordLength);
        if (!v14)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(h[107..], checked((uint)cloud.Count));
            BinaryPrimitives.WriteUInt32LittleEndian(h[111..], (uint)cloud.Count); // 全部视为第一次回波
        }
        WriteDouble3(h[131..], scale);
        WriteDouble3(h[155..], offset);
        BinaryPrimitives.WriteDoubleLittleEndian(h[179..], max.X);
        BinaryPrimitives.WriteDoubleLittleEndian(h[187..], min.X);
        BinaryPrimitives.WriteDoubleLittleEndian(h[195..], max.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(h[203..], min.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(h[211..], max.Z);
        BinaryPrimitives.WriteDoubleLittleEndian(h[219..], min.Z);
        if (v14)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(h[247..], (ulong)cloud.Count);
            BinaryPrimitives.WriteUInt64LittleEndian(h[255..], (ulong)cloud.Count);
        }
        stream.Write(header);

        const int batch = 16384;
        var buffer = new byte[recordLength * batch];
        var points = cloud.Points;
        int colorOffset = LasHeader.ColorOffset(format);
        for (int i = 0; i < points.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.Report(i, points.Length);
            int n = Math.Min(batch, points.Length - i);
            Array.Clear(buffer, 0, n * recordLength);
            for (int k = 0; k < n; k++, i++)
            {
                ref readonly var p = ref points[i];
                var w = cloud.ToWorld(p.Position);
                var r = buffer.AsSpan(k * recordLength, recordLength);
                BinaryPrimitives.WriteInt32LittleEndian(r, Quantize(w.X, offset.X, s));
                BinaryPrimitives.WriteInt32LittleEndian(r[4..], Quantize(w.Y, offset.Y, s));
                BinaryPrimitives.WriteInt32LittleEndian(r[8..], Quantize(w.Z, offset.Z, s));
                BinaryPrimitives.WriteUInt16LittleEndian(r[12..], (ushort)Math.Clamp(MathF.Round(p.Intensity * intensityScale), 0, 65535));
                if (v14)
                {
                    r[14] = 0x11;              // 回波号 1 / 回波数 1
                    r[16] = p.Classification;
                }
                else
                {
                    r[14] = 0x09;              // 回波号 1 / 回波数 1
                    r[15] = (byte)(p.Classification & 0x1F);
                }
                var c = r[colorOffset..];
                BinaryPrimitives.WriteUInt16LittleEndian(c, (ushort)(p.Color.R * 257));
                BinaryPrimitives.WriteUInt16LittleEndian(c[2..], (ushort)(p.Color.G * 257));
                BinaryPrimitives.WriteUInt16LittleEndian(c[4..], (ushort)(p.Color.B * 257));
            }
            stream.Write(buffer, 0, n * recordLength);
        }
        reporter.Report(1);
    }

    private static int Quantize(double value, double offset, double scale) =>
        (int)Math.Round((value - offset) / scale);

    private static void WriteDouble3(Span<byte> s, Double3 v)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(s, v.X);
        BinaryPrimitives.WriteDoubleLittleEndian(s[8..], v.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(s[16..], v.Z);
    }

    private static void WriteString(Span<byte> target, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        bytes.AsSpan(0, Math.Min(bytes.Length, target.Length)).CopyTo(target);
    }
}
