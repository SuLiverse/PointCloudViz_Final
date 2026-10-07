using PointCloudViz.Core.Analysis;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Coloring;

/// <summary>着色模式。</summary>
public enum ColorMode
{
    /// <summary>真彩色（文件自带 RGB）。</summary>
    Rgb,
    /// <summary>按高程映射色带。</summary>
    Elevation,
    /// <summary>按强度映射色带。</summary>
    Intensity,
    /// <summary>按 ASPRS 分类着色。</summary>
    Classification,
    /// <summary>单一颜色。</summary>
    Uniform,
}

/// <summary>着色设置。<see cref="RangeMin"/>/<see cref="RangeMax"/> 为空时自动取 2%~98% 分位。</summary>
public sealed record ColorSettings
{
    public ColorMode Mode { get; init; } = ColorMode.Elevation;
    public string PaletteId { get; init; } = Palettes.Viridis.Id;
    public double? RangeMin { get; init; }
    public double? RangeMax { get; init; }
    public Rgb24 UniformColor { get; init; } = new(230, 230, 230);
}

/// <summary>根据设置为每个点计算显示颜色。</summary>
public static class PointColorizer
{
    /// <summary>根据点云属性选择合理的默认着色模式。</summary>
    public static ColorMode DefaultMode(PointCloud cloud) =>
        cloud.Has(PointAttributes.Color) ? ColorMode.Rgb : ColorMode.Elevation;

    /// <summary>检查着色模式对该点云是否有意义。</summary>
    public static bool IsAvailable(PointCloud cloud, ColorMode mode) => mode switch
    {
        ColorMode.Rgb => cloud.Has(PointAttributes.Color),
        ColorMode.Intensity => cloud.Has(PointAttributes.Intensity),
        ColorMode.Classification => cloud.Has(PointAttributes.Classification),
        _ => true,
    };

    /// <summary>
    /// 自动色带范围（世界坐标高程或原始强度）：取 2%~98% 分位，
    /// 避免极少数离群点让大部分点挤在色带一端。
    /// </summary>
    public static (double Min, double Max) AutoRange(PointCloud cloud, ColorMode mode, double lowQuantile = 0.02, double highQuantile = 0.98)
    {
        if (cloud.IsEmpty) return (0, 1);
        Func<PointRecord, double> selector = mode == ColorMode.Intensity
            ? p => p.Intensity
            : p => p.Position.Z + cloud.Origin.Z;

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (ref readonly var p in cloud.Points)
        {
            double v = selector(p);
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (!(max > min)) return (min, min + 1);

        var histogram = Histogram.Build(cloud.Points, 2048, selector, min, max);
        double lo = histogram.Quantile(lowQuantile), hi = histogram.Quantile(highQuantile);
        return hi > lo ? (lo, hi) : (min, max);
    }

    /// <summary>为全部点计算颜色。</summary>
    public static Rgb24[] Colorize(PointCloud cloud, ColorSettings settings)
    {
        var colors = new Rgb24[cloud.Count];
        Colorize(cloud, settings, colors);
        return colors;
    }

    public static void Colorize(PointCloud cloud, ColorSettings settings, Span<Rgb24> output)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(settings);
        if (output.Length < cloud.Count) throw new ArgumentException("输出缓冲区过小。", nameof(output));

        var pts = cloud.Points;
        var mode = IsAvailable(cloud, settings.Mode) ? settings.Mode : ColorMode.Elevation;
        switch (mode)
        {
            case ColorMode.Rgb:
                for (int i = 0; i < pts.Length; i++) output[i] = pts[i].Color;
                break;

            case ColorMode.Classification:
            {
                Span<Rgb24> lut = stackalloc Rgb24[256];
                for (int c = 0; c < 256; c++) lut[c] = ClassificationColors.GetColor((byte)c);
                for (int i = 0; i < pts.Length; i++) output[i] = lut[pts[i].Classification];
                break;
            }

            case ColorMode.Uniform:
                output[..pts.Length].Fill(settings.UniformColor);
                break;

            case ColorMode.Elevation:
            case ColorMode.Intensity:
            {
                var palette = Palettes.Get(settings.PaletteId);
                var (autoMin, autoMax) = settings.RangeMin is null || settings.RangeMax is null
                    ? AutoRange(cloud, mode)
                    : (0, 0);
                double min = settings.RangeMin ?? autoMin;
                double max = settings.RangeMax ?? autoMax;
                if (mode == ColorMode.Elevation)
                {
                    // 高程转换到局部坐标，循环内只做 float 运算
                    min -= cloud.Origin.Z;
                    max -= cloud.Origin.Z;
                }
                float lo = (float)min;
                float inv = max > min ? (float)(1.0 / (max - min)) : 0f;
                var lut = palette.Lut;
                bool byIntensity = mode == ColorMode.Intensity;
                for (int i = 0; i < pts.Length; i++)
                {
                    float v = byIntensity ? pts[i].Intensity : pts[i].Position.Z;
                    float t = Math.Clamp((v - lo) * inv, 0f, 1f);
                    output[i] = lut[(int)(t * 255f + 0.5f)];
                }
                break;
            }
        }
    }
}
