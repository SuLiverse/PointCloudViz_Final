using System.Numerics;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Picking;

/// <summary>射线（局部坐标），方向为单位向量。</summary>
public readonly record struct Ray3
{
    public Ray3(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }

    public Vector3 Origin { get; }
    public Vector3 Direction { get; }

    public Vector3 PointAt(float t) => Origin + Direction * t;
}

/// <summary>拾取结果。</summary>
public readonly record struct PickResult(int Index, Vector3 Position, float Depth);

/// <summary>
/// 点拾取：在以射线为轴、半顶角为 <c>angularTolerance</c> 的圆锥内寻找离相机最近的点。
/// <para>
/// 旧版做法是把所有点投影到屏幕后 LINQ 排序（O(n log n) 且每次分配），
/// 新实现并行单遍扫描 O(n)，百万点约数毫秒，并且优先选中"最前面"的点，不会穿透到背后。
/// </para>
/// </summary>
public static class PointPicker
{
    /// <param name="points">候选点。</param>
    /// <param name="ray">拾取射线（与点同一坐标系）。</param>
    /// <param name="angularTolerance">容差角（弧度），通常由像素容差 × 每像素视角得到。</param>
    /// <param name="minDistance">忽略过近的点（近裁剪面）。</param>
    public static PickResult? Pick(ReadOnlySpan<PointRecord> points, Ray3 ray, float angularTolerance, float minDistance = 0f) =>
        Pick(points.ToArray(), ray, angularTolerance, minDistance);

    public static PickResult? Pick(PointCloud cloud, Ray3 ray, float angularTolerance, float minDistance = 0f) =>
        Pick(cloud.RawPoints, ray, angularTolerance, minDistance);

    private static PickResult? Pick(PointRecord[] array, Ray3 ray, float angularTolerance, float minDistance)
    {
        if (array.Length == 0) return null;
        float tanTol = MathF.Tan(Math.Clamp(angularTolerance, 1e-6f, 0.5f));
        float tan2 = tanTol * tanTol;

        // 分块并行，每块记录局部最优，最后合并
        const int chunk = 1 << 15;
        int chunks = (array.Length + chunk - 1) / chunk;
        var bestIndex = new int[chunks];
        var bestDepth = new float[chunks];

        Parallel.For(0, chunks, c =>
        {
            int start = c * chunk, end = Math.Min(array.Length, start + chunk);
            int bi = -1;
            float bd = float.PositiveInfinity;
            var o = ray.Origin;
            var d = ray.Direction;
            for (int i = start; i < end; i++)
            {
                var v = array[i].Position - o;
                float t = Vector3.Dot(v, d);
                if (t <= minDistance || t >= bd) continue;
                float perp2 = v.LengthSquared() - t * t;
                if (perp2 <= tan2 * t * t)
                {
                    bd = t;
                    bi = i;
                }
            }
            bestIndex[c] = bi;
            bestDepth[c] = bd;
        });

        int best = -1;
        float depth = float.PositiveInfinity;
        for (int c = 0; c < chunks; c++)
        {
            if (bestIndex[c] >= 0 && bestDepth[c] < depth)
            {
                depth = bestDepth[c];
                best = bestIndex[c];
            }
        }
        return best < 0 ? null : new PickResult(best, array[best].Position, depth);
    }
}
