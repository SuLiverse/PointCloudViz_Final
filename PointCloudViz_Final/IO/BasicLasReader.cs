using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO;

// Compatibility entry point sharing the validated decoder.
public class BasicLasReader : IPointReader
{
    private readonly StreamingLasReader _reader = new();
    public string Name => "LAS (built-in)";
    public bool CanRead(string extension) => _reader.CanRead(extension);
    public Task<PointCloud> ReadAsync(string path, CancellationToken token) => _reader.ReadAsync(path, token);
}
