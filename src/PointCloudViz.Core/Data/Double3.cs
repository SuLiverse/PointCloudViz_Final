using System.Numerics;

namespace PointCloudViz.Core.Data;

/// <summary>
/// 双精度三维坐标。测绘坐标（UTM、高斯-克吕格等）通常是百万量级，
/// 用 float 存储会丢失到分米甚至米级精度，因此世界坐标统一用 double 表示。
/// </summary>
public readonly record struct Double3(double X, double Y, double Z)
{
    public static Double3 Zero => default;

    public static Double3 operator +(Double3 a, Double3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Double3 operator -(Double3 a, Double3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Double3 operator *(Double3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Double3 operator /(Double3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static Double3 operator +(Double3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double HorizontalLength => Math.Sqrt(X * X + Y * Y);

    public static double Dot(Double3 a, Double3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Double3 Cross(Double3 a, Double3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static double Distance(Double3 a, Double3 b) => (a - b).Length;

    /// <summary>转为 float 向量；仅应用于已经去中心化的小数值。</summary>
    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

    public static Double3 FromVector3(Vector3 v) => new(v.X, v.Y, v.Z);

    public double[] ToArray() => [X, Y, Z];

    public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
}
