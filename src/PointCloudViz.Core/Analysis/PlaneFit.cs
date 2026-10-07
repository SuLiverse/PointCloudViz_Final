using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Analysis;

/// <summary>最小二乘平面拟合结果：平面过 <see cref="Centroid"/>，法向 <see cref="Normal"/>（单位向量，Z 分量 ≥ 0）。</summary>
public readonly record struct FittedPlane(Double3 Centroid, Double3 Normal, double Rmse)
{
    public double SignedDistance(Double3 p) => Double3.Dot(p - Centroid, Normal);

    /// <summary>平面与水平面的夹角（度）。</summary>
    public double SlopeDegrees => Math.Acos(Math.Clamp(Math.Abs(Normal.Z), 0, 1)) * 180 / Math.PI;

    /// <summary>
    /// 主成分分析（PCA）拟合平面：协方差矩阵最小特征值对应的特征向量即法向。
    /// 使用 Jacobi 迭代求 3×3 对称矩阵特征分解。
    /// </summary>
    public static FittedPlane Fit(IReadOnlyList<Double3> points)
    {
        if (points.Count < 3) throw new ArgumentException("拟合平面至少需要 3 个点。", nameof(points));

        var c = Double3.Zero;
        foreach (var p in points) c += p;
        c /= points.Count;

        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in points)
        {
            var d = p - c;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
            yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }

        var m = new double[3, 3] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
        var (values, vectors) = Jacobi(m);
        int minIdx = values[0] <= values[1] && values[0] <= values[2] ? 0 : values[1] <= values[2] ? 1 : 2;
        var n = new Double3(vectors[0, minIdx], vectors[1, minIdx], vectors[2, minIdx]);
        n /= n.Length;
        if (n.Z < 0) n *= -1;

        double sse = 0;
        foreach (var p in points)
        {
            double dist = Double3.Dot(p - c, n);
            sse += dist * dist;
        }
        return new FittedPlane(c, n, Math.Sqrt(sse / points.Count));
    }

    private static (double[] Values, double[,] Vectors) Jacobi(double[,] a)
    {
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (off < 1e-15) break;
            for (int p = 0; p < 2; p++)
            for (int q = p + 1; q < 3; q++)
            {
                if (Math.Abs(a[p, q]) < 1e-18) continue;
                double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                if (theta == 0) t = 1;
                double cos = 1 / Math.Sqrt(t * t + 1), sin = t * cos;
                for (int k = 0; k < 3; k++)
                {
                    double akp = a[k, p], akq = a[k, q];
                    a[k, p] = cos * akp - sin * akq;
                    a[k, q] = sin * akp + cos * akq;
                }
                for (int k = 0; k < 3; k++)
                {
                    double apk = a[p, k], aqk = a[q, k];
                    a[p, k] = cos * apk - sin * aqk;
                    a[q, k] = sin * apk + cos * aqk;
                }
                for (int k = 0; k < 3; k++)
                {
                    double vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = cos * vkp - sin * vkq;
                    v[k, q] = sin * vkp + cos * vkq;
                }
            }
        }
        return ([a[0, 0], a[1, 1], a[2, 2]], v);
    }
}
