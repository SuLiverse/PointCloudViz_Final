using System.Numerics;
using PointCloudViz.Core.Data;
using PointCloudViz.Core.Viewing;

namespace PointCloudViz.Core.Rendering;

/// <summary>软件渲染参数。</summary>
public sealed record RenderOptions
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;

    /// <summary>点的像素尺寸（正方形 splat）。</summary>
    public int PointSize { get; init; } = 2;

    public Rgb24 Background { get; init; } = new(24, 26, 31);

    /// <summary>
    /// 是否启用 Eye-Dome Lighting（EDL）：只依赖深度图的屏幕空间明暗处理，
    /// 无需法向即可让点云呈现清晰的轮廓与立体感（CloudCompare、Potree 均采用）。
    /// </summary>
    public bool EyeDomeLighting { get; init; } = true;

    public float EdlStrength { get; init; } = 1.2f;

    public int EdlRadius { get; init; } = 2;
}

/// <summary>
/// 无 GPU 的离屏点云渲染器（深度测试 + 可选 EDL），用于命令行生成预览图和自动化测试。
/// 与界面中的相机共用 <see cref="OrbitCamera"/>，所以 CLI 渲染出的视角与软件中一致。
/// </summary>
public static class SoftwareRenderer
{
    public static RgbaImage Render(PointCloud cloud, ReadOnlySpan<Rgb24> colors, OrbitCamera camera, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(camera);
        options ??= new RenderOptions();
        if (colors.Length < cloud.Count) throw new ArgumentException("颜色数组长度不足。", nameof(colors));

        int w = options.Width, h = options.Height;
        var image = new RgbaImage(w, h);
        image.Fill(options.Background);
        var depth = new float[w * h];
        Array.Fill(depth, float.PositiveInfinity);

        var color = new Rgb24[w * h];
        int size = Math.Max(1, options.PointSize);
        int half = (size - 1) / 2;
        var pts = cloud.Points;

        // 预先计算投影所需的相机基向量，内循环只做点积
        var pos = camera.Position;
        var fwd = camera.Forward;
        float tanY = MathF.Tan(camera.FieldOfView * MathF.PI / 360f);
        float aspect = w / (float)h;
        var right = camera.Right / (tanY * aspect);
        var up = camera.Up / tanY;
        float near = camera.Distance * 1e-3f;

        for (int i = 0; i < pts.Length; i++)
        {
            var v = pts[i].Position - pos;
            float z = Vector3.Dot(v, fwd);
            if (z <= near) continue;
            float invZ = 1f / z;
            int sx = (int)((Vector3.Dot(v, right) * invZ * 0.5f + 0.5f) * w);
            int sy = (int)((0.5f - Vector3.Dot(v, up) * invZ * 0.5f) * h);
            if (sx < -size || sy < -size || sx >= w + size || sy >= h + size) continue;

            for (int dy = -half; dy < size - half; dy++)
            {
                int y = sy + dy;
                if ((uint)y >= (uint)h) continue;
                int row = y * w;
                for (int dx = -half; dx < size - half; dx++)
                {
                    int x = sx + dx;
                    if ((uint)x >= (uint)w) continue;
                    int idx = row + x;
                    if (z < depth[idx])
                    {
                        depth[idx] = z;
                        color[idx] = colors[i];
                    }
                }
            }
        }

        var px = image.Pixels;
        if (options.EyeDomeLighting)
        {
            ApplyEdl(depth, color, px, w, h, options);
        }
        else
        {
            for (int i = 0; i < depth.Length; i++)
            {
                if (float.IsPositiveInfinity(depth[i])) continue;
                px[i * 4] = color[i].R;
                px[i * 4 + 1] = color[i].G;
                px[i * 4 + 2] = color[i].B;
            }
        }
        return image;
    }

    private static void ApplyEdl(float[] depth, Rgb24[] color, byte[] px, int w, int h, RenderOptions options)
    {
        var logDepth = new float[depth.Length];
        float maxLog = float.NegativeInfinity;
        for (int i = 0; i < depth.Length; i++)
        {
            logDepth[i] = float.IsPositiveInfinity(depth[i]) ? float.PositiveInfinity : MathF.Log2(depth[i]);
            if (!float.IsPositiveInfinity(logDepth[i])) maxLog = MathF.Max(maxLog, logDepth[i]);
        }

        int r = Math.Max(1, options.EdlRadius);
        ReadOnlySpan<(int Dx, int Dy)> dirs = [(r, 0), (-r, 0), (0, r), (0, -r), (r, r), (-r, -r), (r, -r), (-r, r)];
        float strength = options.EdlStrength * 300f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float ld = logDepth[i];
                if (float.IsPositiveInfinity(ld)) continue;

                float response = 0;
                foreach (var (dx, dy) in dirs)
                {
                    int nx = x + dx, ny = y + dy;
                    // 屏幕外与背景视为"无限远"，用最大深度代替，从而勾勒出轮廓
                    float nld = (uint)nx < (uint)w && (uint)ny < (uint)h ? logDepth[ny * w + nx] : float.PositiveInfinity;
                    if (float.IsPositiveInfinity(nld)) nld = maxLog + 1f;
                    response += MathF.Max(0f, ld - nld) + MathF.Max(0f, (nld - ld) * 0.25f) * (nld > maxLog ? 1f : 0f);
                }
                float shade = MathF.Exp(-strength * response / dirs.Length * 0.01f);
                var c = color[i];
                px[i * 4] = (byte)(c.R * shade);
                px[i * 4 + 1] = (byte)(c.G * shade);
                px[i * 4 + 2] = (byte)(c.B * shade);
            }
        }
    }
}
