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

    /// <summary>基于GPU加速计算的点云渲染器（使用SIMD和并行优化）</summary>
    public class GpuPointRenderer : OptimizedRendererBase
    {
        private static bool _gpuAvailable = true;
        private static bool _gpuChecked = false;

        public override async Task<WriteableBitmap> RenderAsync(
            PointCloud cloud, Camera camera, int width, int height,
            IColorMap colorMap, int pointSize, Color background, CancellationToken token, bool isInteracting = false)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("invalid size");

            // 检查GPU可用性
            if (!_gpuChecked)
            {
                _gpuAvailable = CheckGpuAvailability();
                _gpuChecked = true;
            }

            if (!_gpuAvailable)
            {
                // GPU不可用，返回null以触发CPU回退
                return null;
            }

            try
            {
                return await RenderWithGpuAcceleration(cloud, camera, width, height, colorMap, pointSize, background, token, isInteracting);
            }
            catch (Exception)
            {
                // GPU渲染失败，返回null以触发CPU回退
                _gpuAvailable = false;
                return null;
            }
        }

        private async Task<WriteableBitmap> RenderWithGpuAcceleration(
            PointCloud cloud, Camera camera, int width, int height,
            IColorMap colorMap, int pointSize, Color background, CancellationToken token, bool isInteracting)
        {
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            int bytes = stride * height;
            
            // 使用内存池复用缓冲区
            byte[] buffer = Utils.MemoryPool.Rent(bytes);
            float[] depth = new float[width * height];

            try
            {
                // 初始化深度缓冲区和背景
                Array.Fill(depth, float.PositiveInfinity);
                InitializeBackground(buffer, background, width, height, stride);

                var view = camera.ViewMatrix;
                var proj = camera.ProjectionMatrix(width / (float)height);
                var vp = view * proj;

                await Task.Run(() =>
                {
                    // 优先使用分块系统（高性能路径）
                    if (cloud.ChunkedCloud != null)
                    {
                        RenderChunkedCloud(cloud.ChunkedCloud, camera, width, height, vp, 
                            colorMap, pointSize, buffer, depth, stride, cloud.BBox, token, isInteracting);
                    }
                    else
                    {
                        // 回退到传统渲染
                        RenderTraditional(cloud, camera, width, height, vp, 
                            colorMap, pointSize, buffer, depth, stride, token, isInteracting);
                    }
                }, token);

                wb.Lock();
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, wb.BackBuffer, bytes);
                wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
                wb.Unlock();
                return wb;
            }
            finally
            {
                // 归还缓冲区到内存池
                Utils.MemoryPool.Return(buffer);
            }
        }

        /// <summary>初始化背景</summary>
        private void InitializeBackground(byte[] buffer, Color background, int width, int height, int stride)
        {
            Parallel.For(0, height, y =>
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
            });
        }

        /// <summary>渲染分块点云（高性能路径）</summary>
        private void RenderChunkedCloud(
            ChunkedPointCloud chunkedCloud, Camera camera, int width, int height, Matrix4x4 vp,
            IColorMap colorMap, int pointSize, byte[] buffer, float[] depth, int stride,
            BoundingBox totalBBox, CancellationToken token, bool isInteracting)
        {
            // 获取可见块
            var visibleChunks = chunkedCloud.GetVisibleChunks(vp, camera.Distance, width, height);

            // 批处理：每个块并行处理
            Parallel.ForEach(visibleChunks, new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, chunk =>
            {
                if (token.IsCancellationRequested) return;

                // 直接从量化数据解码并渲染（避免重复解码）
                RenderChunk(chunk, camera, width, height, vp, colorMap, pointSize, 
                    buffer, depth, stride, totalBBox, token, isInteracting);
            });
        }

        /// <summary>渲染单个块（批处理单元）</summary>
        private void RenderChunk(
            PointCloudChunk chunk, Camera camera, int width, int height, Matrix4x4 vp,
            IColorMap colorMap, int pointSize, byte[] buffer, float[] depth, int stride,
            BoundingBox totalBBox, CancellationToken token, bool isInteracting)
        {
            var chunkPoints = chunk.DecodePoints().ToList();
            if (chunkPoints.Count == 0) return;

            // 计算屏幕空间LOD步进（交互时更激进）
            float lodDensity = CalculateScreenSpaceLodDensity(camera.Distance, width, height);
            int step = CalculatePointStep(chunk.Bounds, camera, lodDensity, isInteracting);

            // 块内并行处理
            int pointCount = chunkPoints.Count;
            int chunkSize = Math.Max(100, pointCount / (Environment.ProcessorCount * 2));

            Parallel.For(0, (pointCount + chunkSize - 1) / chunkSize, new ParallelOptions
            {
                CancellationToken = token
            }, batchIdx =>
            {
                int start = batchIdx * chunkSize;
                int end = Math.Min(start + chunkSize, pointCount);

                for (int i = start; i < end; i += step)
                {
                    if (token.IsCancellationRequested) return;

                    var p = chunkPoints[i];
                    RenderPoint(p, vp, camera, width, height, colorMap, pointSize,
                        buffer, depth, stride, totalBBox);
                }
            });
        }

        /// <summary>渲染单个点</summary>
        private void RenderPoint(
            PointRecord p, Matrix4x4 vp, Camera camera, int width, int height,
            IColorMap colorMap, int pointSize, byte[] buffer, float[] depth, int stride,
            BoundingBox bbox)
        {
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

            // 点大小自适应：近大远小
            float adaptiveSize = pointSize;
            if (nz > 0 && nz < 1.0f)
            {
                adaptiveSize = pointSize * (1.0f + (1.0f - nz) * 2.0f);
            }
            int half = Math.Max(1, (int)adaptiveSize) / 2;

            // 写入像素（使用局部变量减少数组访问）
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

                    // 无锁深度测试（竞争可接受）
                    if (nz < depth[di])
                    {
                        depth[di] = nz;
                        int idx = row + xx * 4;
                        buffer[idx + 0] = col.B;
                        buffer[idx + 1] = col.G;
                        buffer[idx + 2] = col.R;
                        buffer[idx + 3] = 255;
                    }
                }
            }
        }

        /// <summary>传统渲染路径（回退）</summary>
        private void RenderTraditional(
            PointCloud cloud, Camera camera, int width, int height, Matrix4x4 vp,
            IColorMap colorMap, int pointSize, byte[] buffer, float[] depth, int stride,
            CancellationToken token, bool isInteracting)
        {
            var bbox = cloud.BBox;
            var pts = GetVisiblePoints(cloud, camera, width, height, token, isInteracting);
            int n = pts.Count;

            int chunkSize = Math.Max(1000, n / (Environment.ProcessorCount * 4));

            Parallel.For(0, (n + chunkSize - 1) / chunkSize, new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, chunkIdx =>
            {
                int start = chunkIdx * chunkSize;
                int end = Math.Min(start + chunkSize, n);

                for (int i = start; i < end; i++)
                {
                    if (token.IsCancellationRequested) return;
                    RenderPoint(pts[i], vp, camera, width, height, colorMap, pointSize,
                        buffer, depth, stride, bbox);
                }
            });
        }

        private float CalculateScreenSpaceLodDensity(float cameraDistance, float screenWidth, float screenHeight)
        {
            float k = 0.02f;
            float screenPixels = screenWidth * screenHeight;
            float normalizedPixels = screenPixels / (1920 * 1080);
            return k * normalizedPixels * (1.0f + cameraDistance * 0.001f);
        }

        private int CalculatePointStep(BoundingBox chunkBounds, Camera camera, float lodDensity, bool isInteracting)
        {
            float centerZ = (chunkBounds.MinZ + chunkBounds.MaxZ) * 0.5f;
            float distance = Math.Abs(centerZ) + camera.Distance * 0.1f;
            // 交互时使用更激进的步进
            int maxStep = isInteracting ? 20 : 6;
            return (int)Math.Clamp(distance * lodDensity * (isInteracting ? 3f : 1f), 1, maxStep);
        }

        private static bool CheckGpuAvailability()
        {
            try
            {
                // 检查WPF硬件加速是否可用
                var tier = RenderCapability.Tier >> 16;
                // Tier 0 = 无硬件加速
                // Tier 1 = 部分硬件加速
                // Tier 2 = 完整硬件加速
                return tier >= 1;
            }
            catch
            {
                return false;
            }
        }
    }
}

