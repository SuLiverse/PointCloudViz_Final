using System.Buffers.Binary;
using System.Text;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>LAS 公共头（覆盖 1.0 ~ 1.4 版本中读取所需的字段）。</summary>
public sealed record LasHeader
{
    public const int Size12 = 227;
    public const int Size13 = 235;
    public const int Size14 = 375;

    public byte VersionMajor { get; init; } = 1;
    public byte VersionMinor { get; init; } = 2;
    public ushort GlobalEncoding { get; init; }
    public string SystemIdentifier { get; init; } = "";
    public string GeneratingSoftware { get; init; } = "";
    public ushort CreationDayOfYear { get; init; }
    public ushort CreationYear { get; init; }
    public ushort HeaderSize { get; init; }
    public uint OffsetToPointData { get; init; }
    public uint NumberOfVlrs { get; init; }
    public byte PointFormat { get; init; }
    public ushort PointRecordLength { get; init; }
    public ulong PointCount { get; init; }
    public Double3 Scale { get; init; } = new(0.001, 0.001, 0.001);
    public Double3 Offset { get; init; }
    public Double3 Min { get; init; }
    public Double3 Max { get; init; }

    public string Version => $"{VersionMajor}.{VersionMinor}";

    /// <summary>LASzip 压缩的 LAZ 会在点格式字节的高位置位。</summary>
    public bool IsCompressed { get; init; }

    public static LasHeader Read(Stream stream)
    {
        Span<byte> h = stackalloc byte[Size14];
        stream.ReadExactly(h[..Size12]);
        if (!h[..4].SequenceEqual("LASF"u8))
            throw new InvalidDataException("不是有效的 LAS 文件（缺少 LASF 文件签名）。");

        byte major = h[24], minor = h[25];
        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(h[94..]);
        if (headerSize < Size12)
            throw new InvalidDataException($"LAS 头部长度异常：{headerSize}");

        int extra = Math.Min((int)headerSize, Size14) - Size12;
        if (extra > 0) stream.ReadExactly(h.Slice(Size12, extra));
        if (headerSize > Size14) stream.Seek(headerSize - Size14, SeekOrigin.Current);

        byte rawFormat = h[104];
        ulong count = BinaryPrimitives.ReadUInt32LittleEndian(h[107..]);
        if ((major > 1 || minor >= 4) && headerSize >= Size14)
        {
            ulong extended = BinaryPrimitives.ReadUInt64LittleEndian(h[247..]);
            if (extended > 0) count = extended;
        }

        return new LasHeader
        {
            VersionMajor = major,
            VersionMinor = minor,
            GlobalEncoding = BinaryPrimitives.ReadUInt16LittleEndian(h[6..]),
            SystemIdentifier = ReadString(h.Slice(26, 32)),
            GeneratingSoftware = ReadString(h.Slice(58, 32)),
            CreationDayOfYear = BinaryPrimitives.ReadUInt16LittleEndian(h[90..]),
            CreationYear = BinaryPrimitives.ReadUInt16LittleEndian(h[92..]),
            HeaderSize = headerSize,
            OffsetToPointData = BinaryPrimitives.ReadUInt32LittleEndian(h[96..]),
            NumberOfVlrs = BinaryPrimitives.ReadUInt32LittleEndian(h[100..]),
            PointFormat = (byte)(rawFormat & 0x3F),
            IsCompressed = (rawFormat & 0xC0) != 0,
            PointRecordLength = BinaryPrimitives.ReadUInt16LittleEndian(h[105..]),
            PointCount = count,
            Scale = ReadDouble3(h[131..]),
            Offset = ReadDouble3(h[155..]),
            Max = new Double3(ReadDouble(h[179..]), ReadDouble(h[195..]), ReadDouble(h[211..])),
            Min = new Double3(ReadDouble(h[187..]), ReadDouble(h[203..]), ReadDouble(h[219..])),
        };
    }

    /// <summary>每种点数据格式（PDRF）的最小记录长度。</summary>
    public static int MinRecordLength(byte format) => format switch
    {
        0 => 20, 1 => 28, 2 => 26, 3 => 34, 4 => 57, 5 => 63,
        6 => 30, 7 => 36, 8 => 38, 9 => 59, 10 => 67,
        _ => throw new NotSupportedException($"不支持的 LAS 点数据格式：{format}"),
    };

    /// <summary>RGB 字段在记录中的偏移；无颜色返回 -1。</summary>
    public static int ColorOffset(byte format) => format switch
    {
        2 => 20, 3 or 5 => 28, 7 or 8 or 10 => 30,
        _ => -1,
    };

    private static double ReadDouble(ReadOnlySpan<byte> s) => BinaryPrimitives.ReadDoubleLittleEndian(s);

    private static Double3 ReadDouble3(ReadOnlySpan<byte> s) => new(ReadDouble(s), ReadDouble(s[8..]), ReadDouble(s[16..]));

    private static string ReadString(ReadOnlySpan<byte> s)
    {
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]).Trim();
    }
}
