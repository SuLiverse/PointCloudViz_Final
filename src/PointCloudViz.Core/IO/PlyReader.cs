using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>
/// PLY 读取器，支持 <c>ascii</c>、<c>binary_little_endian</c>、<c>binary_big_endian</c> 三种编码。
/// 只读取 <c>vertex</c> 元素；识别的属性：x/y/z、red/green/blue（含 r/g/b、diffuse_*）、
/// intensity（含 scalar_intensity）、classification（含 class、scalar_classification）。
/// </summary>
public sealed class PlyReader : IPointCloudReader
{
    public string FormatName => "PLY 点云";

    public IReadOnlyList<string> Extensions { get; } = [".ply"];

    public PointCloud Read(Stream stream, PointCloudReadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= PointCloudReadOptions.Default;
        var header = PlyHeader.Parse(stream);
        var vertex = header.Elements.FirstOrDefault(e => e.Name == "vertex")
            ?? throw new InvalidDataException("PLY 文件中没有 vertex 元素。");

        var map = new VertexMap(vertex);
        if (map.X < 0 || map.Y < 0 || map.Z < 0)
            throw new InvalidDataException("PLY vertex 元素缺少 x/y/z 属性。");

        long count = vertex.Count;
        int step = options.MaxPoints is int max && count > max ? (int)Math.Ceiling(count / (double)max) : 1;
        var builder = new PointCloudBuilder((int)Math.Min(count / step + 1, int.MaxValue - 64));
        builder.AddAttributes(map.Attributes);

        var reporter = new ProgressReporter(progress);
        Span<double> values = stackalloc double[vertex.Properties.Count];

        if (header.Format == PlyFormat.Ascii)
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1 << 16, leaveOpen: true);
            foreach (var element in header.Elements)
            {
                if (element == vertex) break;
                for (long i = 0; i < element.Count; i++) reader.ReadLine();
            }
            for (long i = 0; i < count; i++)
            {
                if ((i & 0x3FFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    reporter.Report(i, count);
                }
                var line = reader.ReadLine() ?? throw new EndOfStreamException($"PLY 数据不完整：期望 {count} 个点，实际 {i} 个。");
                if (i % step != 0) continue;
                ParseAsciiLine(line, values);
                map.Emit(builder, values);
            }
        }
        else
        {
            bool bigEndian = header.Format == PlyFormat.BinaryBigEndian;
            foreach (var element in header.Elements)
            {
                if (element == vertex) break;
                SkipBinaryElement(stream, element, bigEndian);
            }

            int recordSize = vertex.FixedRecordSize
                ?? throw new NotSupportedException("不支持 vertex 元素中包含 list 属性的二进制 PLY。");
            const int batch = 8192;
            var buffer = new byte[recordSize * batch];
            for (long i = 0; i < count;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reporter.Report(i, count);
                int n = (int)Math.Min(batch, count - i);
                stream.ReadExactly(buffer, 0, n * recordSize);
                for (int k = 0; k < n; k++, i++)
                {
                    if (i % step != 0) continue;
                    var record = buffer.AsSpan(k * recordSize, recordSize);
                    int offset = 0;
                    for (int p = 0; p < vertex.Properties.Count; p++)
                    {
                        var type = vertex.Properties[p].Type;
                        values[p] = ReadBinary(record[offset..], type, bigEndian);
                        offset += SizeOf(type);
                    }
                    map.Emit(builder, values);
                }
            }
        }

        reporter.Report(1);
        return builder.Build();
    }

    private static void ParseAsciiLine(string line, Span<double> values)
    {
        var span = line.AsSpan();
        int index = 0;
        foreach (var range in span.SplitAny(' ', '\t'))
        {
            var token = span[range];
            if (token.IsEmpty) continue;
            if (index >= values.Length) break;
            values[index++] = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (index < values.Length)
            throw new InvalidDataException($"PLY 数据行列数不足：{line}");
    }

    private static void SkipBinaryElement(Stream stream, PlyElement element, bool bigEndian)
    {
        if (element.FixedRecordSize is int size)
        {
            Skip(stream, element.Count * size);
            return;
        }
        Span<byte> scratch = stackalloc byte[8];
        for (long i = 0; i < element.Count; i++)
        {
            foreach (var prop in element.Properties)
            {
                if (prop.ListCountType is { } countType)
                {
                    stream.ReadExactly(scratch[..SizeOf(countType)]);
                    long n = (long)ReadBinary(scratch, countType, bigEndian);
                    Skip(stream, n * SizeOf(prop.Type));
                }
                else
                {
                    Skip(stream, SizeOf(prop.Type));
                }
            }
        }
    }

    private static void Skip(Stream stream, long bytes)
    {
        if (stream.CanSeek)
        {
            stream.Seek(bytes, SeekOrigin.Current);
            return;
        }
        var buffer = new byte[Math.Min(bytes, 1 << 16)];
        while (bytes > 0)
        {
            int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes));
            if (n == 0) throw new EndOfStreamException();
            bytes -= n;
        }
    }

    internal static int SizeOf(PlyType type) => type switch
    {
        PlyType.Int8 or PlyType.UInt8 => 1,
        PlyType.Int16 or PlyType.UInt16 => 2,
        PlyType.Int32 or PlyType.UInt32 or PlyType.Float32 => 4,
        PlyType.Float64 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static double ReadBinary(ReadOnlySpan<byte> s, PlyType type, bool bigEndian) => type switch
    {
        PlyType.Int8 => (sbyte)s[0],
        PlyType.UInt8 => s[0],
        PlyType.Int16 => bigEndian ? BinaryPrimitives.ReadInt16BigEndian(s) : BinaryPrimitives.ReadInt16LittleEndian(s),
        PlyType.UInt16 => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s),
        PlyType.Int32 => bigEndian ? BinaryPrimitives.ReadInt32BigEndian(s) : BinaryPrimitives.ReadInt32LittleEndian(s),
        PlyType.UInt32 => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s),
        PlyType.Float32 => bigEndian ? BinaryPrimitives.ReadSingleBigEndian(s) : BinaryPrimitives.ReadSingleLittleEndian(s),
        PlyType.Float64 => bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(s) : BinaryPrimitives.ReadDoubleLittleEndian(s),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>vertex 属性到点字段的映射。</summary>
    private sealed class VertexMap
    {
        public readonly int X, Y, Z, R = -1, G = -1, B = -1, Intensity = -1, Classification = -1;
        private readonly double _colorScale = 1;

        public VertexMap(PlyElement vertex)
        {
            X = Y = Z = -1;
            for (int i = 0; i < vertex.Properties.Count; i++)
            {
                var prop = vertex.Properties[i];
                if (prop.ListCountType is not null) continue;
                switch (prop.Name.ToLowerInvariant())
                {
                    case "x": X = i; break;
                    case "y": Y = i; break;
                    case "z": Z = i; break;
                    case "red" or "r" or "diffuse_red":
                        R = i;
                        _colorScale = prop.Type switch
                        {
                            PlyType.UInt16 or PlyType.Int16 => 1.0 / 257.0,
                            PlyType.Float32 or PlyType.Float64 => 255.0,
                            _ => 1.0,
                        };
                        break;
                    case "green" or "g" or "diffuse_green": G = i; break;
                    case "blue" or "b" or "diffuse_blue": B = i; break;
                    case "intensity" or "scalar_intensity" or "reflectance": Intensity = i; break;
                    case "classification" or "class" or "scalar_classification" or "label": Classification = i; break;
                }
            }
        }

        public PointAttributes Attributes =>
            (R >= 0 && G >= 0 && B >= 0 ? PointAttributes.Color : 0) |
            (Intensity >= 0 ? PointAttributes.Intensity : 0) |
            (Classification >= 0 ? PointAttributes.Classification : 0);

        public void Emit(PointCloudBuilder builder, ReadOnlySpan<double> v)
        {
            var color = R >= 0 && G >= 0 && B >= 0
                ? new Rgb24(ToByte(v[R] * _colorScale), ToByte(v[G] * _colorScale), ToByte(v[B] * _colorScale))
                : default;
            builder.Add(v[X], v[Y], v[Z],
                Intensity >= 0 ? (float)v[Intensity] : 0f,
                color,
                Classification >= 0 ? ToByte(v[Classification]) : (byte)0);
        }

        private static byte ToByte(double d) => (byte)Math.Clamp(Math.Round(d), 0, 255);
    }
}

internal enum PlyFormat { Ascii, BinaryLittleEndian, BinaryBigEndian }

internal enum PlyType { Int8, UInt8, Int16, UInt16, Int32, UInt32, Float32, Float64 }

internal sealed record PlyProperty(string Name, PlyType Type, PlyType? ListCountType = null);

internal sealed class PlyElement(string name, long count)
{
    public string Name { get; } = name;
    public long Count { get; } = count;
    public List<PlyProperty> Properties { get; } = [];

    /// <summary>不含 list 属性时的固定记录字节数。</summary>
    public int? FixedRecordSize =>
        Properties.Any(p => p.ListCountType is not null) ? null : Properties.Sum(p => PlyReader.SizeOf(p.Type));
}

internal sealed class PlyHeader
{
    public PlyFormat Format { get; private set; }
    public List<PlyElement> Elements { get; } = [];

    /// <summary>逐字节读取头部（不能用 StreamReader，否则会预读走二进制数据）。</summary>
    public static PlyHeader Parse(Stream stream)
    {
        var header = new PlyHeader();
        bool formatSeen = false;
        PlyElement? current = null;

        var magic = ReadLine(stream);
        if (magic != "ply") throw new InvalidDataException("不是有效的 PLY 文件（缺少 ply 标识）。");

        while (true)
        {
            var line = ReadLine(stream) ?? throw new InvalidDataException("PLY 头部不完整（缺少 end_header）。");
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            switch (parts[0])
            {
                case "format":
                    header.Format = parts.ElementAtOrDefault(1) switch
                    {
                        "ascii" => PlyFormat.Ascii,
                        "binary_little_endian" => PlyFormat.BinaryLittleEndian,
                        "binary_big_endian" => PlyFormat.BinaryBigEndian,
                        var f => throw new NotSupportedException($"不支持的 PLY 编码：{f}"),
                    };
                    formatSeen = true;
                    break;
                case "element" when parts.Length >= 3:
                    current = new PlyElement(parts[1], long.Parse(parts[2], CultureInfo.InvariantCulture));
                    header.Elements.Add(current);
                    break;
                case "property" when current is not null:
                    if (parts.Length >= 5 && parts[1] == "list")
                        current.Properties.Add(new PlyProperty(parts[4], ParseType(parts[3]), ParseType(parts[2])));
                    else if (parts.Length >= 3)
                        current.Properties.Add(new PlyProperty(parts[2], ParseType(parts[1])));
                    break;
                case "end_header":
                    if (!formatSeen) throw new InvalidDataException("PLY 头部缺少 format 声明。");
                    return header;
            }
        }
    }

    private static PlyType ParseType(string t) => t switch
    {
        "char" or "int8" => PlyType.Int8,
        "uchar" or "uint8" => PlyType.UInt8,
        "short" or "int16" => PlyType.Int16,
        "ushort" or "uint16" => PlyType.UInt16,
        "int" or "int32" => PlyType.Int32,
        "uint" or "uint32" => PlyType.UInt32,
        "float" or "float32" => PlyType.Float32,
        "double" or "float64" => PlyType.Float64,
        _ => throw new NotSupportedException($"未知的 PLY 数据类型：{t}"),
    };

    private static string? ReadLine(Stream stream)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0) return sb.Length > 0 ? sb.ToString() : null;
            if (b == '\n') return sb.ToString().TrimEnd('\r').Trim();
            if (sb.Length > 4096) throw new InvalidDataException("PLY 头部行过长，文件可能已损坏。");
            sb.Append((char)b);
        }
    }
}
