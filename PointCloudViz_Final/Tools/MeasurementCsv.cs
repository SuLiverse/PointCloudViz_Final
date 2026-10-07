using System.Globalization;
using System.IO;
using PointCloudViz_Final.IO;

namespace PointCloudViz_Final.Tools;

public static class MeasurementCsv
{
    public static Task WriteAsync(string path, IReadOnlyList<Measurement> measurements, CancellationToken token = default)
        => AtomicFile.WriteAsync(path, async stream =>
        {
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
            await writer.WriteLineAsync("measurement,type,value,unit,vertex,x,y,z");
            for (int i = 0; i < measurements.Count; i++)
            {
                var m = measurements[i];
                for (int j = 0; j < m.Points.Count; j++)
                {
                    token.ThrowIfCancellationRequested();
                    var p = m.Points[j];
                    string unit = m.Type == MeasurementType.Distance ? "u" : "u^2";
                    await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                        $"{i + 1},{m.Type},{m.Value:R},{unit},{j + 1},{p.X:R},{p.Y:R},{p.Z:R}"));
                }
            }
            await writer.FlushAsync(token);
        }, token);
}
