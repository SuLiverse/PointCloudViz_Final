using System.Globalization;
using System.IO;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO;

public class XyzReader : IPointReader
{
    public string Name => "XYZ / TXT";
    public bool CanRead(string extension) => extension.Equals(".xyz", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

    public Task<PointCloud> ReadAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var reader = new StreamReader(path);
        var points = new List<PointRecord>();
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            lineNumber++;
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var fields = line.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length is not (3 or 4 or 6 or 7))
                throw new InvalidDataException($"Line {lineNumber}: expected XYZ, XYZI, XYZRGB or XYZIRGB.");
            float Number(int index)
            {
                if (!float.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !float.IsFinite(value))
                    throw new InvalidDataException($"Line {lineNumber}: invalid number in column {index + 1}.");
                return value;
            }
            byte Channel(int index)
            {
                if (!byte.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    throw new InvalidDataException($"Line {lineNumber}: RGB channels must be integers from 0 to 255.");
                return value;
            }
            var intensity = fields.Length is 4 or 7 ? Number(3) : 0;
            var color = fields.Length >= 6
                ? Color.FromRgb(Channel(fields.Length - 3), Channel(fields.Length - 2), Channel(fields.Length - 1))
                : Colors.White;
            points.Add(new PointRecord(Number(0), Number(1), Number(2), intensity, color));
        }
        return new PointCloud(points);
    }, token);
}
