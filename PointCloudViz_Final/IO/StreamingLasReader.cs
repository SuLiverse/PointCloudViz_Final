using System.IO;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO;

public class StreamingLasReader : IPointReader
{
    public string Name => "LAS 1.0-1.4";
    public int TargetPointCount { get; set; } = int.MaxValue;
    public bool CanRead(string extension) => extension.Equals(".las", StringComparison.OrdinalIgnoreCase);
    public Task<PointCloud> ReadAsync(string path, CancellationToken token) => ReadAsync(path, token, null);

    public Task<PointCloud> ReadAsync(string path, CancellationToken token, IProgress<double>? progress)
        => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (TargetPointCount < 1) throw new ArgumentOutOfRangeException(nameof(TargetPointCount));
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 227 || new string(reader.ReadChars(4)) != "LASF")
                throw new InvalidDataException("Missing or truncated LAS header.");
            stream.Position = 24;
            var major = reader.ReadByte();
            var minor = reader.ReadByte();
            if (major != 1 || minor > 4) throw new NotSupportedException("Only LAS 1.0-1.4 is supported.");
            stream.Position = 94;
            var headerSize = reader.ReadUInt16();
            var offset = reader.ReadUInt32();
            reader.ReadUInt32();
            var format = reader.ReadByte();
            var recordSize = reader.ReadUInt16();
            ulong count = reader.ReadUInt32();
            int minimumRecord = format switch { 0 => 20, 1 => 28, 2 => 26, 3 => 34,
                6 => 30, 7 => 36, 8 => 38, _ => throw new NotSupportedException(
                    $"LAS point format {format} is not supported. Supported: 0-3, 6-8; LAZ is not supported.") };
            int minimumHeader = minor >= 4 ? 375 : minor >= 3 ? 235 : 227;
            if (headerSize < minimumHeader || offset < headerSize || offset > stream.Length ||
                recordSize < minimumRecord || (format >= 6 && minor < 4))
                throw new InvalidDataException("Invalid LAS header size, data offset or point record length.");
            stream.Position = 131;
            var scales = new[] { reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble() };
            var offsets = new[] { reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble() };
            if (scales.Any(s => !double.IsFinite(s) || s <= 0) || offsets.Any(o => !double.IsFinite(o)))
                throw new InvalidDataException("Invalid LAS coordinate scale or offset.");
            if (minor >= 4)
            {
                stream.Position = 247;
                ulong extended = reader.ReadUInt64();
                if (extended != 0 || format >= 6) count = extended;
            }
            if (count > (ulong)((stream.Length - offset) / recordSize))
                throw new EndOfStreamException("LAS point count exceeds the available point records.");
            ulong step = Math.Max(1UL, (count + (ulong)TargetPointCount - 1) / (ulong)TargetPointCount);
            ulong keptCount = (count + step - 1) / step;
            if (keptCount > 10_000_000)
                throw new NotSupportedException("This in-memory viewer supports up to 10 million LAS points. Downsample the file before importing.");
            var points = new List<PointRecord>((int)keptCount);
            for (ulong i = 0; i < count; i += step)
            {
                token.ThrowIfCancellationRequested();
                long start = checked((long)offset + (long)i * recordSize);
                stream.Position = start;
                float x = (float)(reader.ReadInt32() * scales[0] + offsets[0]);
                float y = (float)(reader.ReadInt32() * scales[1] + offsets[1]);
                float z = (float)(reader.ReadInt32() * scales[2] + offsets[2]);
                float intensity = reader.ReadUInt16() / 65535f;
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    throw new InvalidDataException("LAS coordinate is outside the supported numeric range.");
                var color = Colors.White;
                int rgbOffset = format switch { 2 => 20, 3 => 28, 7 or 8 => 30, _ => -1 };
                if (rgbOffset >= 0)
                {
                    stream.Position = start + rgbOffset;
                    color = Color.FromRgb((byte)(reader.ReadUInt16() >> 8),
                        (byte)(reader.ReadUInt16() >> 8), (byte)(reader.ReadUInt16() >> 8));
                }
                points.Add(new PointRecord(x, y, z, intensity, color));
                if (points.Count % 10000 == 0) progress?.Report((i + 1) * 100.0 / count);
            }
            progress?.Report(100);
            return new PointCloud(points);
        }, token);
}

