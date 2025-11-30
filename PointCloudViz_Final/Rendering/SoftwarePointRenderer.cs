using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    // 解决 WPF Media3D.Camera 与 自定义 Camera 的同名冲突
    using Camera = PointCloudViz_Final.Models.Camera;

    /// <summary>纯 CPU 点云软渲染（含深度测试）</summary>
    public class SoftwarePointRenderer : LodAwareRendererBase
    {
        public override async Task<WriteableBitmap> RenderAsync(
            PointCloud cloud, Camera camera, int width, int height,
            IColorMap colorMap, int pointSize, Color background, CancellationToken token, bool isInteracting = false)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("invalid size");

            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;                 // BGRA32 → 每像素 4 字节
            int bytes = stride * height;
            byte[] buffer = new byte[bytes];
            float[] depth = new float[width * height];

            for (int i = 0; i < depth.Length; i++) depth[i] = float.PositiveInfinity;
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int idx = row + x * 4;
                    buffer[idx + 0] = background.B;
                    buffer[idx + 1] = background.G;
                    buffer[idx + 2] = background.R;
                    buffer[idx + 3] = 255;
                }
            }

            var view = camera.ViewMatrix;
            var proj = camera.ProjectionMatrix(width / (float)height);
            // 关键：System.Numerics 的组合应写成 view * proj
            var vp = view * proj;

            await Task.Run(() =>
            {
                var bbox = cloud.BBox;
                // 使用优化的点列表（LOD + 视锥体剔除，交互时使用激进LOD）
                var pts = GetVisiblePoints(cloud, camera, width, height, token, isInteracting);
                int n = pts.Count;

                Parallel.For(0, n, new ParallelOptions { CancellationToken = token }, i =>
                {
                    var p = pts[i];
                    var pos = new Vector4(p.X, p.Y, p.Z, 1f);

                    Vector4 t = Vector4.Transform(pos, vp);
                    
                    // 快速视锥体剔除
                    if (!IsPointInFrustum(t, camera.Near, camera.Far)) return;

                    float iw = (t.W != 0f) ? (1f / t.W) : 1f;
                    float nx = t.X * iw;
                    float ny = t.Y * iw;
                    float nz = t.Z * iw;

                    int sx = (int)((nx * 0.5f + 0.5f) * (width - 1));
                    int sy = (int)(((-ny) * 0.5f + 0.5f) * (height - 1));

                    var col = colorMap.Map(p, bbox);
                    
                    // 点大小自适应：近大远小，增强深度感
                    float adaptiveSize = pointSize;
                    if (nz > 0 && nz < 1.0f)
                    {
                        // Z值越小（越近）点越大
                        adaptiveSize = pointSize * (1.0f + (1.0f - nz) * 2.0f);
                    }
                    int half = Math.Max(1, (int)adaptiveSize) / 2;
                    for (int dy = -half; dy <= half; dy++)
                    {
                        int yy = sy + dy;
                        if (yy < 0 || yy >= height) continue;
                        int row = yy * stride;

                        for (int dx = -half; dx <= half; dx++)
                        {
                            int xx = sx + dx;
                            if (xx < 0 || xx >= width) continue;

                            int di = yy * width + xx;
                            if (nz >= depth[di]) continue;
                            depth[di] = nz;

                            int idx = row + xx * 4;
                            buffer[idx + 0] = col.B;
                            buffer[idx + 1] = col.G;
                            buffer[idx + 2] = col.R;
                            buffer[idx + 3] = 255;
                        }
                    }
                });
            }, token);

            wb.Lock();
            System.Runtime.InteropServices.Marshal.Copy(buffer, 0, wb.BackBuffer, bytes);
            wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
            wb.Unlock();
            return wb;
        }
    }
}
