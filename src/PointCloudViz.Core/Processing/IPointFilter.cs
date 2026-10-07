using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Processing;

/// <summary>点云滤波器。所有实现都不修改输入，而是返回新的点云（便于撤销/重做）。</summary>
public interface IPointFilter
{
    /// <summary>用于界面与历史记录的描述，如"体素下采样 (0.20)"。</summary>
    string Description { get; }

    PointCloud Apply(PointCloud input, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>按"保留/剔除"掩码筛选点的滤波器基类（模板方法模式）。</summary>
public abstract class MaskFilter : IPointFilter
{
    public abstract string Description { get; }

    public PointCloud Apply(PointCloud input, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var keep = new bool[input.Count];
        ComputeMask(input, keep, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        int kept = 0;
        foreach (var k in keep) if (k) kept++;
        if (kept == input.Count) return input;

        var src = input.Points;
        var result = new PointRecord[kept];
        for (int i = 0, j = 0; i < keep.Length; i++)
        {
            if (keep[i]) result[j++] = src[i];
        }
        progress?.Report(1);
        return input.WithPoints(result);
    }

    /// <summary>为每个点填写是否保留。</summary>
    protected abstract void ComputeMask(PointCloud input, Span<bool> keep, IProgress<double>? progress, CancellationToken cancellationToken);
}
