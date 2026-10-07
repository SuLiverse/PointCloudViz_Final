using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Coloring;

/// <summary>连续色带，预计算 256 级查找表。</summary>
public sealed class Palette
{
    private readonly Rgb24[] _lut = new Rgb24[256];

    public Palette(string id, string displayName, IReadOnlyList<(float Position, Rgb24 Color)> stops)
    {
        Id = id;
        DisplayName = displayName;
        if (stops.Count < 2) throw new ArgumentException("色带至少需要两个色标。", nameof(stops));
        for (int i = 0; i < 256; i++) _lut[i] = Interpolate(stops, i / 255f);
    }

    public Palette(string id, string displayName, Func<float, Rgb24> function)
    {
        Id = id;
        DisplayName = displayName;
        for (int i = 0; i < 256; i++) _lut[i] = function(i / 255f);
    }

    public string Id { get; }
    public string DisplayName { get; }

    /// <summary>按 t∈[0,1] 取色（越界会被截断）。</summary>
    public Rgb24 Sample(float t)
    {
        if (float.IsNaN(t)) t = 0;
        return _lut[(int)(Math.Clamp(t, 0f, 1f) * 255f + 0.5f)];
    }

    public ReadOnlySpan<Rgb24> Lut => _lut;

    private static Rgb24 Interpolate(IReadOnlyList<(float Position, Rgb24 Color)> stops, float t)
    {
        if (t <= stops[0].Position) return stops[0].Color;
        for (int i = 1; i < stops.Count; i++)
        {
            var (p1, c1) = stops[i];
            if (t <= p1)
            {
                var (p0, c0) = stops[i - 1];
                return Rgb24.Lerp(c0, c1, (t - p0) / Math.Max(1e-6f, p1 - p0));
            }
        }
        return stops[^1].Color;
    }

    public override string ToString() => DisplayName;
}

/// <summary>内置色带。</summary>
public static class Palettes
{
    /// <summary>感知均匀、色盲友好（matplotlib 默认）。</summary>
    public static Palette Viridis { get; } = FromHex("viridis", "Viridis（感知均匀）",
        "#440154", "#482475", "#414487", "#355F8D", "#2A788E", "#21918C", "#22A884", "#44BF70", "#7AD151", "#BDDF26", "#FDE725");

    /// <summary>Google Turbo：彩虹色带的改良版，对比度高且过渡平滑。</summary>
    public static Palette Turbo { get; } = new("turbo", "Turbo（高对比）", t =>
    {
        double r = 0.13572138 + t * (4.61539260 + t * (-42.66032258 + t * (132.13108234 + t * (-152.94239396 + t * 59.28637943))));
        double g = 0.09140261 + t * (2.19418839 + t * (4.84296658 + t * (-14.18503333 + t * (4.27729857 + t * 2.82956604))));
        double b = 0.10667330 + t * (12.64194608 + t * (-60.58204836 + t * (110.36276771 + t * (-89.90310912 + t * 27.34824973))));
        return new Rgb24(ToByte(r), ToByte(g), ToByte(b));
    });

    /// <summary>旧版程序使用的"深蓝-青-绿-黄-红"彩虹色带。</summary>
    public static Palette Rainbow { get; } = FromHex("rainbow", "经典彩虹",
        "#00008B", "#00FFFF", "#008000", "#FFFF00", "#FF0000");

    /// <summary>地形色带：水体蓝 → 低地绿 → 丘陵黄褐 → 山地棕 → 雪线白。</summary>
    public static Palette Terrain { get; } = FromHex("terrain", "地形",
        "#2C7BB6", "#1A9641", "#A6D96A", "#FFFFBF", "#C49A6C", "#8C5A3C", "#FFFFFF");

    /// <summary>冷暖发散色带，适合显示高差/残差。</summary>
    public static Palette CoolWarm { get; } = FromHex("coolwarm", "冷暖发散",
        "#3B4CC0", "#8DB0FE", "#DDDDDD", "#F49A7B", "#B40426");

    public static Palette Grayscale { get; } = FromHex("gray", "灰度", "#000000", "#FFFFFF");

    public static IReadOnlyList<Palette> All { get; } = [Viridis, Turbo, Rainbow, Terrain, CoolWarm, Grayscale];

    public static Palette Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Viridis;

    private static Palette FromHex(string id, string name, params string[] colors) =>
        new(id, name, colors.Select((c, i) => (i / (float)(colors.Length - 1), Rgb24.FromHex(c))).ToArray());

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
