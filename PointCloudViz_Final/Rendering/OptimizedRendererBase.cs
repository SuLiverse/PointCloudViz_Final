using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>优化的渲染器基类，提供视锥体剔除和自适应LOD</summary>
    public abstract class OptimizedRendererBase : IRenderer
    {
        public abstract Task<WriteableBitmap> RenderAsync(
            PointCloud cloud, Camera camera, int width, int height,
            IColorMap colorMap, int pointSize, Color background, CancellationToken token, bool isInteracting = false);

        /// <summary>获取需要渲染的点（分块 + 八叉树视锥体剔除 + LOD）</summary>
        protected IReadOnlyList<PointRecord> GetVisiblePoints(
            PointCloud cloud, Camera camera, int width, int height, CancellationToken token, bool isInteracting = false)
        {
            if (cloud == null) return Array.Empty<PointRecord>();
            
            var bbox = cloud.BBox;
            var view = camera.ViewMatrix;
            var proj = camera.ProjectionMatrix(width / (float)height);
            var viewProj = view * proj;

            // 优先使用分块系统（如果可用）
            if (cloud.ChunkedCloud != null)
            {
                return GetVisiblePointsFromChunks(cloud.ChunkedCloud, camera, width, height, viewProj, token, isInteracting);
            }

            // 回退到八叉树
            var visiblePoints = cloud.GetVisiblePoints(viewProj);
            
            if (visiblePoints.Count == 0) return Array.Empty<PointRecord>();
            
            int totalCount = visiblePoints.Count;

            // 自适应LOD：根据点云大小和距离决定采样率（可通过开关控制）
            // 交互时使用激进的LOD策略
            int sampleRate = IsLodEnabled() ? CalculateSampleRate(totalCount, camera.Distance, bbox, isInteracting) : 1;
            
            // 如果不需要下采样，直接返回视锥体内的点
            if (sampleRate <= 1)
            {
                return visiblePoints;
            }

            // 下采样：每隔sampleRate个点取一个
            var sampled = new List<PointRecord>(totalCount / sampleRate + 1);
            for (int i = 0; i < totalCount; i += sampleRate)
            {
                if (token.IsCancellationRequested) break;
                sampled.Add(visiblePoints[i]);
            }

            return sampled;
        }

        /// <summary>从分块系统获取可见点（高性能路径）</summary>
        private IReadOnlyList<PointRecord> GetVisiblePointsFromChunks(
            ChunkedPointCloud chunkedCloud, Camera camera, int width, int height, 
            Matrix4x4 viewProj, CancellationToken token, bool isInteracting = false)
        {
            var result = new List<PointRecord>();

            // 获取可见块
            var visibleChunks = chunkedCloud.GetVisibleChunks(viewProj, camera.Distance, width, height);

            if (visibleChunks.Count == 0)
            {
                // 如果没有可见块，可能是视锥体剔除太严格，返回所有点（降级处理）
                Utils.Logger.Warning("分块系统未找到可见块，降级到全量渲染");
                return chunkedCloud.Chunks.SelectMany(c => c.DecodePoints()).Take(100_000).ToList();
            }

            // 计算屏幕空间LOD密度（降低密度系数，确保有足够的点被渲染）
            float lodDensity = CalculateScreenSpaceLodDensity(camera.Distance, width, height);
            
            // 如果结果太少，降低LOD密度
            int totalPoints = 0;
            foreach (var chunk in visibleChunks)
            {
                totalPoints += chunk.PointCount;
            }

            // 如果总点数太少，不使用LOD或使用更小的步进
            bool useLod = totalPoints > 500_000;

            foreach (var chunk in visibleChunks)
            {
                if (token.IsCancellationRequested) break;

                // 从量化数据解码点（只解码可见块）
                var chunkPoints = chunk.DecodePoints().ToList();

                if (chunkPoints.Count == 0) continue;

                if (useLod)
                {
                    // 屏幕空间密度控制：根据距离跳点（交互时更激进）
                    int step = CalculatePointStep(chunk.Bounds, camera, lodDensity, isInteracting);
                    step = Math.Max(1, step); // 确保至少为1
                    for (int i = 0; i < chunkPoints.Count; i += step)
                    {
                        result.Add(chunkPoints[i]);
                    }
                }
                else
                {
                    // 点数不多，全部添加
                    result.AddRange(chunkPoints);
                }
            }

            // 如果结果为空，至少返回一些点
            if (result.Count == 0 && visibleChunks.Count > 0)
            {
                Utils.Logger.Warning("分块渲染结果为空，返回第一个块的所有点");
                var firstChunk = visibleChunks[0];
                result.AddRange(firstChunk.DecodePoints().Take(10_000));
            }

            return result;
        }

        private float CalculateScreenSpaceLodDensity(float cameraDistance, float screenWidth, float screenHeight)
        {
            // LOD密度系数：降低默认值，确保有足够的点被渲染
            float k = 0.01f; // 从0.02降低到0.01，减少LOD强度
            float screenPixels = screenWidth * screenHeight;
            float normalizedPixels = Math.Max(screenPixels / (1920 * 1080), 0.5f); // 最小0.5，避免过度缩放
            return k * normalizedPixels * (1.0f + cameraDistance * 0.0005f); // 降低距离影响
        }

        private int CalculatePointStep(BoundingBox chunkBounds, Camera camera, float lodDensity, bool isInteracting = false)
        {
            // 估算块到相机的距离
            float centerZ = (chunkBounds.MinZ + chunkBounds.MaxZ) * 0.5f;
            float distance = Math.Abs(centerZ) + camera.Distance * 0.1f;

            // 根据距离计算步进：距离越远，步进越大
            // 交互时使用更激进的步进
            int maxStep = isInteracting ? 20 : 3;
            float multiplier = isInteracting ? 3f : 1f;
            int step = (int)Math.Clamp(distance * lodDensity * multiplier, 1, maxStep);
            return step;
        }

        /// <summary>计算采样率（LOD）</summary>
        protected int CalculateSampleRate(int pointCount, float distance, BoundingBox bbox, bool isInteracting = false)
        {
            // 交互时使用激进的LOD策略：只渲染10%甚至1%的点
            if (isInteracting)
            {
                // 交互时：根据点数决定采样率
                // 百万级点云：采样率10-20（只渲染5-10%）
                // 千万级点云：采样率50-100（只渲染1-2%）
                if (pointCount > 10_000_000)
                    return 50; // 只渲染2%
                else if (pointCount > 5_000_000)
                    return 20; // 只渲染5%
                else if (pointCount > 1_000_000)
                    return 10; // 只渲染10%
                else
                    return 5; // 只渲染20%
            }
            
            // 静态时：根据点数和距离动态调整采样率
            // 点数越多，距离越远，采样率越高（跳过更多点）
            
            float bboxSize = Math.Max(bbox.MaxX - bbox.MinX, Math.Max(bbox.MaxY - bbox.MinY, bbox.MaxZ - bbox.MinZ));
            float normalizedDistance = distance / Math.Max(bboxSize, 1f);
            
            // 目标渲染点数：根据距离调整（静态时更宽松）
            int targetPoints = normalizedDistance > 2f ? 500_000 : normalizedDistance > 1f ? 1_000_000 : 2_000_000;
            
            if (pointCount <= targetPoints)
                return 1; // 不需要下采样
            
            // 计算采样率
            int rate = (int)Math.Ceiling((double)pointCount / targetPoints);
            
            // 限制采样率范围（静态时更保守）
            return Math.Clamp(rate, 1, 10);
        }

        /// <summary>快速视锥体剔除（粗略检查）</summary>
        protected bool IsPointInFrustum(Vector4 transformed, float near, float far)
        {
            float iw = (transformed.W != 0f) ? (1f / transformed.W) : 1f;
            float nx = transformed.X * iw;
            float ny = transformed.Y * iw;
            float nz = transformed.Z * iw;

            // 检查是否在视锥体内
            return nx >= -1.1f && nx <= 1.1f && 
                   ny >= -1.1f && ny <= 1.1f && 
                   nz >= -1.1f && nz <= 1.1f;
        }

        /// <summary>检查LOD是否启用（子类可重写）</summary>
        protected virtual bool IsLodEnabled() => true;
    }
}

