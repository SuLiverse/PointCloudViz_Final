using System.Windows.Media;
using PointCloudViz.Core.Data;

namespace PointCloudViz.App.Infrastructure;

/// <summary>Core 颜色类型与 WPF / SharpDX 颜色之间的转换。</summary>
public static class ColorExtensions
{
    public static Color ToWpf(this Rgb24 c) => Color.FromRgb(c.R, c.G, c.B);

    public static Rgb24 ToRgb24(this Color c) => new(c.R, c.G, c.B);

    public static SharpDX.Color4 ToColor4(this Rgb24 c, float alpha = 1f) => new(c.R / 255f, c.G / 255f, c.B / 255f, alpha);

    public static SolidColorBrush ToBrush(this Rgb24 c)
    {
        var brush = new SolidColorBrush(c.ToWpf());
        brush.Freeze();
        return brush;
    }

    /// <summary>根据背景亮度选择对比色（深色背景用浅色文字）。</summary>
    public static Rgb24 Contrasting(this Rgb24 background) =>
        background.Luminance > 0.55f ? new Rgb24(20, 20, 24) : new Rgb24(235, 235, 240);
}
