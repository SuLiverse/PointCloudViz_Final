using System.Globalization;
using System.Text;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Measurements;

/// <summary>量测类型。</summary>
public enum MeasurementKind
{
    /// <summary>单点坐标。</summary>
    Point,
    /// <summary>两点距离（斜距、平距、高差、坡度）。</summary>
    Distance,
    /// <summary>折线长度。</summary>
    Polyline,
    /// <summary>多边形面积（空间面积与水平投影面积）。</summary>
    Area,
}

/// <summary>
/// 一次量测结果，顶点为世界坐标（double）。所有量值都是按需计算的只读属性，
/// 因此量测可以安全地序列化到项目文件并在加载后复原。
/// </summary>
public sealed record Measurement(int Id, MeasurementKind Kind, IReadOnlyList<Double3> Points)
{
    public static int MinimumPoints(MeasurementKind kind) => kind switch
    {
        MeasurementKind.Point => 1,
        MeasurementKind.Distance or MeasurementKind.Polyline => 2,
        MeasurementKind.Area => 3,
        _ => 1,
    };

    public bool IsComplete => Points.Count >= MinimumPoints(Kind);

    /// <summary>三维长度（折线/距离为沿线总长，面积为周长）。</summary>
    public double Length => MeasurementMath.PathLength(Points, closed: Kind == MeasurementKind.Area);

    /// <summary>水平投影长度。</summary>
    public double HorizontalLength => MeasurementMath.HorizontalPathLength(Points, closed: Kind == MeasurementKind.Area);

    /// <summary>首末点高差（终点 − 起点）。</summary>
    public double HeightDifference => Points.Count >= 2 ? Points[^1].Z - Points[0].Z : 0;

    /// <summary>坡度（%），即高差 / 平距。</summary>
    public double SlopePercent
    {
        get
        {
            double h = HorizontalLength;
            return h > 1e-9 ? HeightDifference / h * 100 : 0;
        }
    }

    /// <summary>空间多边形面积（Newell 向量面积，非共面时为最佳投影面积）。</summary>
    public double Area => Kind == MeasurementKind.Area ? MeasurementMath.PolygonArea3D(Points) : 0;

    /// <summary>水平投影面积（测绘中常用的"平面面积"）。</summary>
    public double HorizontalArea => Kind == MeasurementKind.Area ? MeasurementMath.PolygonAreaXY(Points) : 0;

    public string KindName => Kind switch
    {
        MeasurementKind.Point => "坐标",
        MeasurementKind.Distance => "距离",
        MeasurementKind.Polyline => "折线",
        MeasurementKind.Area => "面积",
        _ => Kind.ToString(),
    };

    /// <summary>简短摘要，用于列表与状态栏。</summary>
    public string Summary => Kind switch
    {
        MeasurementKind.Point when Points.Count > 0 =>
            string.Create(CultureInfo.InvariantCulture, $"X {Points[0].X:F3}  Y {Points[0].Y:F3}  Z {Points[0].Z:F3}"),
        MeasurementKind.Distance =>
            string.Create(CultureInfo.InvariantCulture, $"斜距 {Length:F3} m · 平距 {HorizontalLength:F3} m · 高差 {HeightDifference:+0.000;-0.000} m"),
        MeasurementKind.Polyline =>
            string.Create(CultureInfo.InvariantCulture, $"长度 {Length:F3} m · {Points.Count} 个顶点"),
        MeasurementKind.Area =>
            string.Create(CultureInfo.InvariantCulture, $"面积 {Area:F3} m² · 水平投影 {HorizontalArea:F3} m² · 周长 {Length:F3} m"),
        _ => "",
    };

    public override string ToString() => $"#{Id} {KindName}: {Summary}";
}

/// <summary>量测几何计算。</summary>
public static class MeasurementMath
{
    public static double PathLength(IReadOnlyList<Double3> pts, bool closed)
    {
        double sum = 0;
        for (int i = 1; i < pts.Count; i++) sum += Double3.Distance(pts[i - 1], pts[i]);
        if (closed && pts.Count > 2) sum += Double3.Distance(pts[^1], pts[0]);
        return sum;
    }

    public static double HorizontalPathLength(IReadOnlyList<Double3> pts, bool closed)
    {
        double sum = 0;
        for (int i = 1; i < pts.Count; i++) sum += (pts[i] - pts[i - 1]).HorizontalLength;
        if (closed && pts.Count > 2) sum += (pts[0] - pts[^1]).HorizontalLength;
        return sum;
    }

    /// <summary>
    /// 空间多边形面积：Newell 法求向量面积的模。对平面多边形精确，对略微不共面的点（真实点云常见）
    /// 给出最佳投影平面上的面积——旧版遇到这种情况会直接拒绝计算。
    /// 顶点按用户点击顺序连接，不再按极角重排，因此支持凹多边形。
    /// </summary>
    public static double PolygonArea3D(IReadOnlyList<Double3> pts)
    {
        if (pts.Count < 3) return 0;
        // 先减去首点，降低大坐标下的舍入误差
        var o = pts[0];
        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i] - o;
            var b = pts[(i + 1) % pts.Count] - o;
            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }
        return 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
    }

    /// <summary>水平投影面积（鞋带公式）。</summary>
    public static double PolygonAreaXY(IReadOnlyList<Double3> pts)
    {
        if (pts.Count < 3) return 0;
        var o = pts[0];
        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i] - o;
            var b = pts[(i + 1) % pts.Count] - o;
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) * 0.5;
    }
}

/// <summary>量测结果导出。</summary>
public static class MeasurementExport
{
    /// <summary>导出为 CSV（UTF-8 BOM，Excel 可直接打开中文）。</summary>
    public static string ToCsv(IEnumerable<Measurement> measurements)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("编号,类型,顶点数,长度(m),平距(m),高差(m),坡度(%),面积(m²),水平面积(m²),顶点坐标");
        foreach (var m in measurements)
        {
            var vertices = string.Join(" ; ", m.Points.Select(p => string.Create(ci, $"{p.X:F3} {p.Y:F3} {p.Z:F3}")));
            sb.Append(ci, $"{m.Id},{m.KindName},{m.Points.Count},");
            sb.Append(ci, $"{m.Length:F3},{m.HorizontalLength:F3},{m.HeightDifference:F3},{m.SlopePercent:F2},");
            sb.Append(ci, $"{m.Area:F3},{m.HorizontalArea:F3},\"{vertices}\"");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static void WriteCsv(string path, IEnumerable<Measurement> measurements) =>
        File.WriteAllText(path, ToCsv(measurements), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
}
