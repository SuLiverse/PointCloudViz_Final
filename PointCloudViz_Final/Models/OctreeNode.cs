using System;
using System.Collections.Generic;
using System.Numerics;

namespace PointCloudViz_Final.Models
{
    /// <summary>八叉树节点，用于空间索引加速</summary>
    public class OctreeNode
    {
        public BoundingBox Bounds { get; set; }
        public List<PointRecord> Points { get; set; } = new();
        public OctreeNode[]? Children { get; set; }
        public bool IsLeaf => Children == null;

        private const int MaxPointsPerNode = 1000; // 每个节点最多存储的点数
        private const int MaxDepth = 8; // 最大深度

        /// <summary>构建八叉树</summary>
        public static OctreeNode Build(IReadOnlyList<PointRecord> points, BoundingBox bounds, int depth = 0)
        {
            var node = new OctreeNode { Bounds = bounds };

            // 如果点数少或深度达到上限，作为叶子节点
            if (points.Count <= MaxPointsPerNode || depth >= MaxDepth)
            {
                node.Points = new List<PointRecord>(points);
                return node;
            }

            // 分割为8个子节点
            var centerX = (bounds.MinX + bounds.MaxX) * 0.5f;
            var centerY = (bounds.MinY + bounds.MaxY) * 0.5f;
            var centerZ = (bounds.MinZ + bounds.MaxZ) * 0.5f;

            node.Children = new OctreeNode[8];
            var childBounds = new BoundingBox[8]
            {
                // 前下左 (0)
                new BoundingBox(bounds.MinX, bounds.MinY, bounds.MinZ, centerX, centerY, centerZ),
                // 前下右 (1)
                new BoundingBox(centerX, bounds.MinY, bounds.MinZ, bounds.MaxX, centerY, centerZ),
                // 前上左 (2)
                new BoundingBox(bounds.MinX, centerY, bounds.MinZ, centerX, bounds.MaxY, centerZ),
                // 前上右 (3)
                new BoundingBox(centerX, centerY, bounds.MinZ, bounds.MaxX, bounds.MaxY, centerZ),
                // 后下左 (4)
                new BoundingBox(bounds.MinX, bounds.MinY, centerZ, centerX, centerY, bounds.MaxZ),
                // 后下右 (5)
                new BoundingBox(centerX, bounds.MinY, centerZ, bounds.MaxX, centerY, bounds.MaxZ),
                // 后上左 (6)
                new BoundingBox(bounds.MinX, centerY, centerZ, centerX, bounds.MaxY, bounds.MaxZ),
                // 后上右 (7)
                new BoundingBox(centerX, centerY, centerZ, bounds.MaxX, bounds.MaxY, bounds.MaxZ)
            };

            // 将点分配到子节点
            var childPoints = new List<PointRecord>[8];
            for (int i = 0; i < 8; i++)
                childPoints[i] = new List<PointRecord>();

            foreach (var point in points)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (IsPointInBounds(point, childBounds[i]))
                    {
                        childPoints[i].Add(point);
                        break; // 每个点只属于一个子节点
                    }
                }
            }

            // 递归构建子节点
            for (int i = 0; i < 8; i++)
            {
                if (childPoints[i].Count > 0)
                {
                    node.Children[i] = Build(childPoints[i], childBounds[i], depth + 1);
                }
            }

            return node;
        }

        /// <summary>检查点是否在边界内</summary>
        private static bool IsPointInBounds(PointRecord point, BoundingBox bounds)
        {
            return point.X >= bounds.MinX && point.X <= bounds.MaxX &&
                   point.Y >= bounds.MinY && point.Y <= bounds.MaxY &&
                   point.Z >= bounds.MinZ && point.Z <= bounds.MaxZ;
        }

        /// <summary>检查边界框是否与视锥体相交</summary>
        public static bool IntersectsFrustum(BoundingBox bounds, Matrix4x4 viewProj)
        {
            // 获取边界框的8个角点
            var corners = new Vector4[8]
            {
                new Vector4(bounds.MinX, bounds.MinY, bounds.MinZ, 1f),
                new Vector4(bounds.MaxX, bounds.MinY, bounds.MinZ, 1f),
                new Vector4(bounds.MinX, bounds.MaxY, bounds.MinZ, 1f),
                new Vector4(bounds.MaxX, bounds.MaxY, bounds.MinZ, 1f),
                new Vector4(bounds.MinX, bounds.MinY, bounds.MaxZ, 1f),
                new Vector4(bounds.MaxX, bounds.MinY, bounds.MaxZ, 1f),
                new Vector4(bounds.MinX, bounds.MaxY, bounds.MaxZ, 1f),
                new Vector4(bounds.MaxX, bounds.MaxY, bounds.MaxZ, 1f)
            };

            // 变换到裁剪空间
            bool allOutside = true;

            foreach (var corner in corners)
            {
                var transformed = Vector4.Transform(corner, viewProj);
                float w = transformed.W != 0 ? 1f / transformed.W : 1f;
                float x = transformed.X * w;
                float y = transformed.Y * w;
                float z = transformed.Z * w;

                // 检查是否在视锥体内（放宽边界以包含边界情况）
                bool inside = x >= -1.1f && x <= 1.1f && y >= -1.1f && y <= 1.1f && z >= -1.1f && z <= 1.1f;

                if (inside)
                {
                    allOutside = false;
                    // 如果至少有一个点在内部，说明相交，可以提前返回
                    break;
                }
            }

            // 如果所有点都在外部，返回false；否则返回true（相交）
            return !allOutside;
        }

        /// <summary>收集视锥体内的点</summary>
        public void CollectVisiblePoints(Matrix4x4 viewProj, List<PointRecord> result)
        {
            // 检查节点是否与视锥体相交
            if (!IntersectsFrustum(Bounds, viewProj))
                return;

            // 如果是叶子节点，添加所有点
            if (IsLeaf)
            {
                result.AddRange(Points);
                return;
            }

            // 递归检查子节点
            if (Children != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (Children[i] != null)
                    {
                        Children[i].CollectVisiblePoints(viewProj, result);
                    }
                }
            }
        }
    }
}

