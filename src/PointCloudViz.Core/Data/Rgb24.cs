using System.Globalization;

namespace PointCloudViz.Core.Data;

/// <summary>24 位 RGB 颜色（与 UI 框架无关，Core 层不依赖 WPF）。</summary>
public readonly record struct Rgb24(byte R, byte G, byte B)
{
    public static Rgb24 White => new(255, 255, 255);
    public static Rgb24 Black => new(0, 0, 0);

    /// <summary>按 t∈[0,1] 线性插值两个颜色。</summary>
    public static Rgb24 Lerp(Rgb24 a, Rgb24 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgb24(
            (byte)MathF.Round(a.R + (b.R - a.R) * t),
            (byte)MathF.Round(a.G + (b.G - a.G) * t),
            (byte)MathF.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>解析 <c>#RRGGBB</c> 形式的十六进制颜色。</summary>
    public static Rgb24 FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var s = hex.AsSpan().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"无效的颜色值：{hex}");
        return new Rgb24((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static bool TryParseHex(string? hex, out Rgb24 color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            color = FromHex(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>ITU-R BT.601 亮度，范围 0~1。</summary>
    public float Luminance => (0.299f * R + 0.587f * G + 0.114f * B) / 255f;

    public override string ToString() => ToHex();
}
