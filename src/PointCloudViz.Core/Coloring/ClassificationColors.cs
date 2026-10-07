using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Coloring;

/// <summary>ASPRS LAS 标准分类码的名称与配色。</summary>
public static class ClassificationColors
{
    private static readonly (string Name, Rgb24 Color)[] Known =
    [
        ("从未分类", new Rgb24(160, 160, 160)),     // 0
        ("未分类", new Rgb24(200, 200, 200)),       // 1
        ("地面", new Rgb24(166, 118, 72)),          // 2
        ("低矮植被", new Rgb24(178, 223, 138)),     // 3
        ("中等植被", new Rgb24(102, 189, 99)),      // 4
        ("高大植被", new Rgb24(26, 128, 60)),       // 5
        ("建筑物", new Rgb24(231, 76, 60)),         // 6
        ("低点（噪声）", new Rgb24(255, 0, 255)),   // 7
        ("模型关键点", new Rgb24(255, 255, 0)),     // 8
        ("水体", new Rgb24(52, 152, 219)),          // 9
        ("铁路", new Rgb24(127, 96, 0)),            // 10
        ("路面", new Rgb24(80, 80, 90)),            // 11
        ("重叠点", new Rgb24(255, 200, 150)),       // 12
        ("导线防护", new Rgb24(240, 240, 120)),     // 13
        ("导线", new Rgb24(255, 255, 160)),         // 14
        ("输电塔", new Rgb24(200, 120, 255)),       // 15
        ("导线连接件", new Rgb24(160, 100, 200)),   // 16
        ("桥面", new Rgb24(0, 200, 200)),           // 17
        ("高点（噪声）", new Rgb24(255, 0, 128)),   // 18
    ];

    /// <summary>本软件合成数据使用的自定义分类（64 以上为 LAS 规范中的用户自定义区间）。</summary>
    private static readonly Dictionary<byte, (string Name, Rgb24 Color)> Custom = new()
    {
        [64] = ("车辆", new Rgb24(52, 73, 190)),
        [65] = ("杆状物", new Rgb24(241, 196, 15)),
    };

    public static string GetName(byte classification) =>
        classification < Known.Length ? Known[classification].Name
        : Custom.TryGetValue(classification, out var c) ? c.Name
        : $"自定义 {classification}";

    public static Rgb24 GetColor(byte classification)
    {
        if (classification < Known.Length) return Known[classification].Color;
        if (Custom.TryGetValue(classification, out var custom)) return custom.Color;
        // 自定义分类：用黄金角在色相环上散布，保证相邻编号颜色差异明显
        float hue = (classification * 137.508f) % 360f;
        return FromHsv(hue, 0.65f, 0.95f);
    }

    private static Rgb24 FromHsv(float h, float s, float v)
    {
        float c = v * s, x = c * (1 - MathF.Abs(h / 60f % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x),
            3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x),
        };
        return new Rgb24((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
