using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>读取选项。</summary>
public sealed record PointCloudReadOptions
{
    public static PointCloudReadOptions Default { get; } = new();

    /// <summary>
    /// 最多保留的点数；超过时按固定步长均匀抽稀。<c>null</c> 表示不限制。
    /// 对于 XYZ 等无法预知点数的格式，按文件大小估算步长。
    /// </summary>
    public int? MaxPoints { get; init; }
}

/// <summary>点云读取器（策略模式：每种格式一个实现）。</summary>
public interface IPointCloudReader
{
    string FormatName { get; }

    /// <summary>支持的扩展名（小写、含点号）。</summary>
    IReadOnlyList<string> Extensions { get; }

    PointCloud Read(Stream stream, PointCloudReadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>点云写出器。</summary>
public interface IPointCloudWriter
{
    string FormatName { get; }

    IReadOnlyList<string> Extensions { get; }

    void Write(PointCloud cloud, Stream stream,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
