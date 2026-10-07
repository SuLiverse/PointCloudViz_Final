using System.Numerics;
using System.Runtime.InteropServices;

namespace PointCloudViz.Core.Data;

/// <summary>
/// 单个点。<see cref="Position"/> 为相对于 <see cref="PointCloud.Origin"/> 的局部坐标（float），
/// 每点 20 字节，百万点约 20 MB。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PointRecord
{
    public Vector3 Position;

    /// <summary>原始强度值（不同格式量纲不同，着色时按实际范围归一化）。</summary>
    public float Intensity;

    public Rgb24 Color;

    /// <summary>ASPRS LAS 分类码。</summary>
    public byte Classification;

    public PointRecord(Vector3 position, float intensity = 0f, Rgb24 color = default, byte classification = 0)
    {
        Position = position;
        Intensity = intensity;
        Color = color;
        Classification = classification;
    }

    public PointRecord(float x, float y, float z, float intensity = 0f, Rgb24 color = default, byte classification = 0)
        : this(new Vector3(x, y, z), intensity, color, classification)
    {
    }
}
