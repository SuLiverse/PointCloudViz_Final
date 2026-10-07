using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PointCloudViz.Core.Rendering;

/// <summary>零依赖的 PNG 编码器（RGBA8，逐行 Sub/Up 自适应滤波 + zlib 压缩）。</summary>
public static class PngEncoder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], image.Height);
        ihdr[8] = 8;  // 位深
        ihdr[9] = 6;  // RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);

        int stride = image.Width * 4;
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[stride + 1];
            var alt = new byte[stride + 1];
            var px = image.Pixels;
            for (int y = 0; y < image.Height; y++)
            {
                var cur = px.AsSpan(y * stride, stride);
                var prev = y > 0 ? px.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;

                // 滤波 1：Sub
                row[0] = 1;
                for (int i = 0; i < stride; i++) row[i + 1] = (byte)(cur[i] - (i >= 4 ? cur[i - 4] : 0));
                // 滤波 2：Up，选绝对值和更小的
                alt[0] = 2;
                for (int i = 0; i < stride; i++) alt[i + 1] = (byte)(cur[i] - (prev.IsEmpty ? 0 : prev[i]));
                z.Write(Score(alt) < Score(row) ? alt : row);
            }
        }
        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static long Score(byte[] filtered)
    {
        long s = 0;
        for (int i = 1; i < filtered.Length; i++) s += Math.Abs((int)(sbyte)filtered[i]);
        return s;
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
