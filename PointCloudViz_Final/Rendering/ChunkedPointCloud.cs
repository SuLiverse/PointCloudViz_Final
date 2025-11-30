using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    /// <summary>分块点云：将点云分割为多个块，支持LOD和按需加载</summary>
    public class ChunkedPointCloud
    {
        private readonly List<PointCloudChunk> _chunks = new();
        private readonly Dictionary<int, PointCloudChunk> _chunkCache = new();
        private readonly BoundingBox _totalBounds;
        private const int MaxPointsPerChunk = 200_000; // 每块最多20万点
        private const int MinPointsPerChunk = 50_000;  // 每块至少5万点

        public IReadOnlyList<PointCloudChunk> Chunks => _chunks;
        public BoundingBox TotalBounds => _totalBounds;
        public int TotalChunkCount => _chunks.Count;

        public ChunkedPointCloud(IReadOnlyList<PointRecord> points, BoundingBox bounds)
        {
            _totalBounds = bounds;
            BuildChunks(points);
        }

        /// <summary>构建分块（使用八叉树或固定尺寸）</summary>
        private void BuildChunks(IReadOnlyList<PointRecord> points)
        {
            if (points.Count == 0) return;

            // 使用固定尺寸分块（简单高效）
            float chunkSize = CalculateOptimalChunkSize(_totalBounds, points.Count);

            // 计算块的数量
            float sizeX = _totalBounds.MaxX - _totalBounds.MinX;
            float sizeY = _totalBounds.MaxY - _totalBounds.MinY;
            float sizeZ = _totalBounds.MaxZ - _totalBounds.MinZ;

            int chunksX = Math.Max(1, (int)Math.Ceiling(sizeX / chunkSize));
            int chunksY = Math.Max(1, (int)Math.Ceiling(sizeY / chunkSize));
            int chunksZ = Math.Max(1, (int)Math.Ceiling(sizeZ / chunkSize));

            // 将点分配到块
            var chunkPoints = new Dictionary<int, List<PointRecord>>();

            foreach (var point in points)
            {
                int cx = (int)Math.Clamp((point.X - _totalBounds.MinX) / chunkSize, 0, chunksX - 1);
                int cy = (int)Math.Clamp((point.Y - _totalBounds.MinY) / chunkSize, 0, chunksY - 1);
                int cz = (int)Math.Clamp((point.Z - _totalBounds.MinZ) / chunkSize, 0, chunksZ - 1);

                int chunkId = cx + cy * chunksX + cz * chunksX * chunksY;

                if (!chunkPoints.ContainsKey(chunkId))
                    chunkPoints[chunkId] = new List<PointRecord>();

                chunkPoints[chunkId].Add(point);
            }

            // 创建块
            foreach (var kvp in chunkPoints)
            {
                if (kvp.Value.Count < MinPointsPerChunk)
                    continue; // 跳过太小的块

                var chunkBounds = CalculateChunkBounds(kvp.Value, kvp.Key, chunksX, chunksY, chunksZ, chunkSize);
                var chunk = PointCloudChunk.Create(kvp.Value, chunkBounds, lodLevel: 0);
                _chunks.Add(chunk);
            }

            Utils.Logger.Info($"点云分块完成: {_chunks.Count} 块, 平均每块 {points.Count / Math.Max(1, _chunks.Count)} 点");
        }

        private float CalculateOptimalChunkSize(BoundingBox bounds, int pointCount)
        {
            float sizeX = bounds.MaxX - bounds.MinX;
            float sizeY = bounds.MaxY - bounds.MinY;
            float sizeZ = bounds.MaxZ - bounds.MinZ;
            float maxSize = Math.Max(sizeX, Math.Max(sizeY, sizeZ));

            // 目标：每块约10-20万点
            float volume = sizeX * sizeY * sizeZ;
            float pointsPerUnitVolume = pointCount / Math.Max(volume, 0.001f);
            float targetChunkVolume = MaxPointsPerChunk / pointsPerUnitVolume;
            float targetChunkSize = (float)Math.Pow(targetChunkVolume, 1.0 / 3.0);

            // 限制块大小范围
            return Math.Clamp(targetChunkSize, maxSize / 100, maxSize / 4);
        }

        private BoundingBox CalculateChunkBounds(List<PointRecord> points, int chunkId, int chunksX, int chunksY, int chunksZ, float chunkSize)
        {
            if (points.Count == 0)
                return _totalBounds;

            float minX = points[0].X, minY = points[0].Y, minZ = points[0].Z;
            float maxX = points[0].X, maxY = points[0].Y, maxZ = points[0].Z;

            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z > maxZ) maxZ = p.Z;
            }

            return new BoundingBox(minX, minY, minZ, maxX, maxY, maxZ);
        }

        /// <summary>获取视锥体内的可见块</summary>
        public List<PointCloudChunk> GetVisibleChunks(Matrix4x4 viewProj, float cameraDistance, float screenWidth, float screenHeight)
        {
            var visible = new List<PointCloudChunk>();

            foreach (var chunk in _chunks)
            {
                // 视锥剔除
                if (!OctreeNode.IntersectsFrustum(chunk.Bounds, viewProj))
                    continue;

                // 不再使用严格的LOD阈值过滤，让所有可见块都参与渲染
                // LOD通过点级别的步进控制来实现
                visible.Add(chunk);
                chunk.LastAccessTime = DateTime.Now;
            }

            return visible;
        }
    }
}

