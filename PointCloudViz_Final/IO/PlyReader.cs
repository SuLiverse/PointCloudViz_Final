using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO;

public class PlyReader : IPointReader
{
    private sealed record Property(string Name, string Type, string? CountType = null);
    private sealed record Element(string Name, int Count, List<Property> Properties);
    public string Name => "PLY (ASCII / binary)";
    public bool CanRead(string extension) => extension.Equals(".ply", StringComparison.OrdinalIgnoreCase);
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
        { "char", "int8", "uchar", "uint8", "short", "int16", "ushort", "uint16",
          "int", "int32", "uint", "uint32", "float", "float32", "double", "float64" };

    public Task<PointCloud> ReadAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        int headerBytes = 0;
        string HeaderLine()
        {
            var bytes = new List<byte>();
            int value;
            while ((value = stream.ReadByte()) != -1)
            {
                if (++headerBytes > 1_048_576) throw new InvalidDataException("PLY header exceeds 1 MiB.");
                if (value == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
                bytes.Add((byte)value);
            }
            throw new EndOfStreamException("Incomplete PLY header.");
        }
        if (HeaderLine() != "ply") throw new InvalidDataException("Missing PLY signature.");
        string? format = null;
        var elements = new List<Element>();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var fields = HeaderLine().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0 || fields[0] is "comment" or "obj_info") continue;
            if (fields[0] == "end_header") break;
            if (fields[0] == "format" && fields.Length == 3 && fields[2] == "1.0")
                format = fields[1];
            else if (fields[0] == "element" && fields.Length == 3 &&
                int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                elements.Add(new Element(fields[1], count, new()));
            else if (fields[0] == "property" && elements.Count > 0)
            {
                Property property;
                if (fields.Length == 3) property = new(fields[2], fields[1]);
                else if (fields.Length == 5 && fields[1] == "list")
                    property = new(fields[4], fields[3], fields[2]);
                else throw new InvalidDataException("Invalid PLY property.");
                if (!Types.Contains(property.Type) || (property.CountType != null &&
                    (!Types.Contains(property.CountType) || property.CountType is "float" or "float32" or "double" or "float64")))
                    throw new InvalidDataException("Unsupported PLY scalar type.");
                elements[^1].Properties.Add(property);
            }
            else throw new InvalidDataException("Invalid PLY header declaration.");
        }
        if (format is not ("ascii" or "binary_little_endian" or "binary_big_endian"))
            throw new NotSupportedException("Unsupported PLY encoding.");
        var vertices = elements.Where(e => e.Name == "vertex").ToArray();
        if (vertices.Length != 1) throw new InvalidDataException("PLY requires exactly one vertex element.");
        var vertex = vertices[0];
        foreach (var axis in new[] { "x", "y", "z" })
            if (vertex.Properties.Count(p => p.Name == axis && p.CountType == null) != 1)
                throw new InvalidDataException($"PLY requires a scalar {axis} coordinate.");
        if (vertex.Properties.Any(p => p.CountType != null))
            throw new NotSupportedException("List-valued vertex properties are not supported.");

        using var ascii = format == "ascii" ? new StreamReader(stream, Encoding.ASCII, false, 65536, leaveOpen: true) : null;
        var points = new List<PointRecord>(Math.Min(vertex.Count, 1_000_000));
        var buffer = new byte[8];
        double BinaryScalar(string type)
        {
            int size = type switch { "char" or "int8" or "uchar" or "uint8" => 1,
                "short" or "int16" or "ushort" or "uint16" => 2, "double" or "float64" => 8, _ => 4 };
            stream.ReadExactly(buffer.AsSpan(0, size));
            if (format == "binary_big_endian") Array.Reverse(buffer, 0, size);
            return type switch
            {
                "char" or "int8" => (sbyte)buffer[0], "uchar" or "uint8" => buffer[0],
                "short" or "int16" => BinaryPrimitives.ReadInt16LittleEndian(buffer),
                "ushort" or "uint16" => BinaryPrimitives.ReadUInt16LittleEndian(buffer),
                "int" or "int32" => BinaryPrimitives.ReadInt32LittleEndian(buffer),
                "uint" or "uint32" => BinaryPrimitives.ReadUInt32LittleEndian(buffer),
                "float" or "float32" => BinaryPrimitives.ReadSingleLittleEndian(buffer),
                _ => BinaryPrimitives.ReadDoubleLittleEndian(buffer)
            };
        }
        foreach (var element in elements)
        {
            for (int row = 0; row < element.Count; row++)
            {
                token.ThrowIfCancellationRequested();
                var fields = ascii?.ReadLine()?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (ascii != null && fields == null) throw new EndOfStreamException($"Truncated PLY {element.Name} data.");
                int column = 0;
                double Scalar(string type)
                {
                    if (ascii == null) return BinaryScalar(type);
                    if (column >= fields!.Length || !double.TryParse(fields[column++], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                        throw new InvalidDataException($"Invalid PLY {element.Name} row {row + 1}.");
                    return number;
                }
                float x = 0, y = 0, z = 0, intensity = 0;
                byte r = 255, g = 255, b = 255;
                foreach (var property in element.Properties)
                {
                    if (property.CountType != null)
                    {
                        var count = Scalar(property.CountType);
                        if (count < 0 || count > 1_000_000 || count != Math.Truncate(count))
                            throw new InvalidDataException("Invalid PLY list length.");
                        for (int i = 0; i < count; i++)
                        {
                            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                            Scalar(property.Type);
                        }
                        continue;
                    }
                    var value = Scalar(property.Type);
                    if (element.Name != "vertex") continue;
                    if (!float.IsFinite((float)value)) throw new InvalidDataException("Non-finite PLY vertex.");
                    byte ColorValue()
                    {
                        if (value < 0 || value > 255 || value != Math.Truncate(value))
                            throw new InvalidDataException("PLY RGB channels must be integers from 0 to 255.");
                        return (byte)value;
                    }
                    switch (property.Name)
                    {
                        case "x": x = (float)value; break;
                        case "y": y = (float)value; break;
                        case "z": z = (float)value; break;
                        case "intensity": intensity = (float)value; break;
                        case "red": r = ColorValue(); break;
                        case "green": g = ColorValue(); break;
                        case "blue": b = ColorValue(); break;
                    }
                }
                if (ascii != null && column != fields!.Length) throw new InvalidDataException("Extra PLY row values.");
                if (element.Name == "vertex") points.Add(new PointRecord(x, y, z, intensity, Color.FromRgb(r, g, b)));
            }
        }
        return new PointCloud(points);
    }, token);
}
