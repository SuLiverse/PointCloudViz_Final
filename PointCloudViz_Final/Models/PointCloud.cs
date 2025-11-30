using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PointCloudViz_Final.Models
{
    public class PointCloud
    {
        private readonly List<PointRecord> _points;
        private OctreeNode? _octreeRoot;
        private bool _octreeBuilt = false;
        private Rendering.ChunkedPointCloud? _chunkedCloud;
        private bool _spatialIndexBuilt = false;

        public IReadOnlyList<PointRecord> Points => _points;
        public BoundingBox BBox { get; private set; }
        public Rendering.ChunkedPointCloud? ChunkedCloud => _chunkedCloud;

        public PointCloud(IEnumerable<PointRecord> points)
        {
            var tmp = points.ToList();
            // 加一层内存友好下采样：超过 120 万点时按步长抽稀
            const int MaxKeepPoints = 1_200_000;
            if (tmp.Count > MaxKeepPoints)
            {
                int step = (tmp.Count + MaxKeepPoints - 1) / MaxKeepPoints;
                int original = tmp.Count;
                tmp = tmp.Where((_, i) => i % step == 0).ToList();
                Utils.Logger.Info($"自动下采样以节省内存：原 {original} 点 -> {tmp.Count} 点，步长 {step}");
            }
            _points = tmp;
            BBox = BoundingBox.FromPoints(_points);
            BuildSpatialIndex();
        }

        public int Count => _points.Count;

        public void Replace(IEnumerable<PointRecord> points)
        {
            _points.Clear();
            _points.AddRange(points);
            // 重复利用加载时的内存友好下采样
            const int MaxKeepPoints = 1_200_000;
            if (_points.Count > MaxKeepPoints)
            {
                int original = _points.Count;
                int step = (original + MaxKeepPoints - 1) / MaxKeepPoints;
                var sampled = _points.Where((_, i) => i % step == 0).ToList();
                _points.Clear();
                _points.AddRange(sampled);
                Utils.Logger.Info($"自动下采样以节省内存：原 {original} 点 -> {_points.Count} 点，步长 {step}");
            }
            BBox = BoundingBox.FromPoints(_points);
            BuildSpatialIndex();
        }

        /// <summary>构建八叉树索引</summary>
        private void BuildSpatialIndex()
        {
            const int MaxIndexPoints = 2_000_000;
            if (_points.Count == 0) { _octreeRoot = null; _chunkedCloud = null; _spatialIndexBuilt = false; return; }
            if (_points.Count > MaxIndexPoints) {
                _octreeRoot = null;
                _chunkedCloud = null;
                _octreeBuilt = false;
                _spatialIndexBuilt = false;
                Utils.Logger.Info($"跳过空间索引/分块以节省内存: {_points.Count} 点");
                return;
            }
            BuildOctree();
            BuildChunkedCloud();
            _spatialIndexBuilt = true;
        }

        private void BuildOctree()
        {
            if (_points.Count == 0)
            {
                _octreeRoot = null;
                _octreeBuilt = false;
                return;
            }

            _octreeRoot = OctreeNode.Build(_points, BBox);
            _octreeBuilt = true;
        }

        /// <summary>构建分块点云</summary>
        private void BuildChunkedCloud()
        {
            if (_points.Count > 100_000) // 只有大点云才分块
            {
                _chunkedCloud = new Rendering.ChunkedPointCloud(_points, BBox);
            }
        }

        /// <summary>使用八叉树获取视锥体内的点（性能优化）</summary>
        public List<PointRecord> GetVisiblePoints(Matrix4x4 viewProj)
        {
            if (!_octreeBuilt || _octreeRoot == null)
            {
                // 如果没有八叉树，返回所有点
                return new List<PointRecord>(_points);
            }

            var visible = new List<PointRecord>();
            _octreeRoot.CollectVisiblePoints(viewProj, visible);
            return visible;
        }

        public (int Count, float MeanZ, float MinZ, float MaxZ) StatsZ()
        {
            if (_points.Count == 0) return (0, 0, 0, 0);
            float sumZ = 0, minZ = _points[0].Z, maxZ = _points[0].Z;
            foreach (var p in _points)
            {
                sumZ += p.Z;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            return (_points.Count, sumZ / _points.Count, minZ, maxZ);
        }

        public (int Count, float MeanZ, float MinZ, float MaxZ) StatsZ(float minZ, float maxZ)
        {
            var list = _points.Where(p => p.Z >= minZ && p.Z <= maxZ).ToList();
            if (list.Count == 0) return (0, 0, 0, 0);
            float sumZ = 0, min = list[0].Z, max = list[0].Z;
            foreach (var p in list)
            {
                sumZ += p.Z;
                if (p.Z < min) min = p.Z;
                if (p.Z > max) max = p.Z;
            }
            return (list.Count, sumZ / list.Count, min, max);
        }
    }
}
