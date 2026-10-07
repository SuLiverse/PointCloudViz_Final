namespace PointCloudViz.Core.Data;

/// <summary>点云包含哪些有效属性（决定 UI 中哪些着色模式可用）。</summary>
[Flags]
public enum PointAttributes
{
    None = 0,
    Intensity = 1 << 0,
    Color = 1 << 1,
    Classification = 1 << 2,
}
