using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Tools
{
    /// <summary>交互式测量工具：测距、面积量算</summary>
    public class MeasurementTool
    {
        private readonly List<Vector3> _selectedPoints = new();
        private readonly List<Measurement> _measurements = new();
        private bool _isActive = false;
        private MeasurementMode _mode = MeasurementMode.None;
        private Measurement? _latestAreaMeasurement;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                if (!value) ClearSelection();
            }
        }

        public MeasurementMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                ClearSelection();
            }
        }

        public IReadOnlyList<Vector3> SelectedPoints => _selectedPoints;
        public IReadOnlyList<Measurement> Measurements => _measurements;

        /// <summary>处理鼠标点击，选择点</summary>
        public bool OnMouseClick(Point screenPos, Camera camera, PointCloud? cloud, int width, int height)
        {
            if (!_isActive || cloud == null || _mode == MeasurementMode.None) return false;

            // Screen to world (nearest visible point)
            var worldPoint = ScreenToWorld(screenPos, camera, cloud, width, height);
            if (!worldPoint.HasValue) return false;

            _selectedPoints.Add(worldPoint.Value);

            if (_mode == MeasurementMode.Distance)
            {
                if (_selectedPoints.Count == 2)
                {
                    var distance = Vector3.Distance(_selectedPoints[0], _selectedPoints[1]);
                    var measurement = new Measurement
                    {
                        Type = MeasurementType.Distance,
                        Points = new List<Vector3>(_selectedPoints),
                        Value = distance,
                        Label = $"距离: {distance:F2}m"
                    };
                    _measurements.Add(measurement);
                    OnMeasurementCreated?.Invoke(measurement);
                    _selectedPoints.Clear();
                }
            }
            else if (_mode == MeasurementMode.Area)
            {
                if (_selectedPoints.Count >= 3)
                {
                    // 先检测共面性
                    if (!IsNearlyCoplanar(_selectedPoints, out var normal, out var centroid))
                    {
                        // 回退最后一次添加，提示
                        _selectedPoints.RemoveAt(_selectedPoints.Count - 1);
                        OnMeasurementMessage?.Invoke("选点不共面，无法计算面积（请在同一平面选点）");
                        return false;
                    }

                    // 按平面排序，避免交叉；同时投影到局部平面计算面积
                    var ordered3D = OrderPointsOnPlane(_selectedPoints, normal, centroid);
                    var area = CalculatePolygonArea2D(ProjectToPlane(ordered3D, normal, centroid));
                    var measurement = new Measurement
                    {
                        Type = MeasurementType.Area,
                        Points = new List<Vector3>(ordered3D),
                        Value = area,
                        Label = $"面积: {area:F2}m²"
                    };

                    if (_latestAreaMeasurement != null)
                    {
                        _measurements.Remove(_latestAreaMeasurement);
                    }

                    _measurements.Add(measurement);
                    _latestAreaMeasurement = measurement;
                    OnMeasurementCreated?.Invoke(measurement);
                }
            }

            return true;
        }
        private Vector3? ScreenToWorld(Point screenPos, Camera camera, PointCloud cloud, int width, int height)
        {
            if (width <= 0 || height <= 0) return null;

            // 将屏幕坐标归一化到[-1, 1]
            float nx = (float)((screenPos.X / width) * 2.0 - 1.0);
            float ny = (float)(1.0 - (screenPos.Y / height) * 2.0);

            // 构建视图投影矩阵
            var view = camera.ViewMatrix;
            var proj = camera.ProjectionMatrix(width / (float)height);
            var viewProj = view * proj;

            // 找到屏幕位置附近最近的点
            if (cloud.Points == null || cloud.Points.Count == 0)
                return null;

            // 使用可见点（如果有点云分块，使用分块的点）
            var pointsToSearch = cloud.Points;
            
            // 如果点太多，先做粗略筛选（只检查视锥体内的点）
            if (pointsToSearch.Count > 10000)
            {
                var visiblePoints = cloud.GetVisiblePoints(viewProj);
                if (visiblePoints.Count > 0)
                {
                    pointsToSearch = visiblePoints;
                }
            }

            if (pointsToSearch.Count == 0)
                return null;

            // 找到屏幕位置最近的点（扩大搜索范围以提高成功率）
            var nearPoint = pointsToSearch
                .Where(p =>
                {
                    // 快速视锥体检查
                    var pos = new Vector4(p.X, p.Y, p.Z, 1f);
                    var screen = Vector4.Transform(pos, viewProj);
                    float w = screen.W != 0 ? 1f / screen.W : 1f;
                    float sx = screen.X * w;
                    float sy = screen.Y * w;
                    
                    // 检查是否在屏幕范围内（放宽边界）
                    return sx >= -1.1f && sx <= 1.1f && sy >= -1.1f && sy <= 1.1f;
                })
                .OrderBy(p =>
                {
                    var pos = new Vector4(p.X, p.Y, p.Z, 1f);
                    var screen = Vector4.Transform(pos, viewProj);
                    float w = screen.W != 0 ? 1f / screen.W : 1f;
                    float sx = screen.X * w;
                    float sy = screen.Y * w;
                    float dx = sx - nx;
                    float dy = sy - ny;
                    return dx * dx + dy * dy;
                })
                .FirstOrDefault();

            // 检查是否找到有效点
            if (nearPoint.X == 0 && nearPoint.Y == 0 && nearPoint.Z == 0)
            {
                // 检查是否所有点都是原点
                if (pointsToSearch.Any(p => p.X != 0 || p.Y != 0 || p.Z != 0))
                {
                    return null; // 找到了原点但其他点不是原点，说明没找到合适的点
                }
            }

            return new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z);
        }

        /// <summary>计算多边形面积（使用叉积）</summary>
        private float CalculatePolygonArea2D(List<Vector2> points)
        {
            if (points.Count < 3) return 0f;

            double area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % points.Count];
                area += p1.X * p2.Y - p2.X * p1.Y;
            }
            return (float)(Math.Abs(area) * 0.5);
        }

        /// <summary>将点排序到平面上（按质心角度），避免自交</summary>
        private List<Vector3> OrderPointsOnPlane(List<Vector3> points, Vector3 normal, Vector3 centroid)
        {
            if (points.Count <= 3) return new List<Vector3>(points);

            // 构建平面基
            Vector3 basisX = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY));
            if (basisX.LengthSquared() < 1e-6f)
                basisX = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX));
            Vector3 basisY = Vector3.Normalize(Vector3.Cross(normal, basisX));

            // 投影并按极角排序
            var ordered = points
                .Select(p =>
                {
                    var v = p - centroid;
                    float x = Vector3.Dot(v, basisX);
                    float y = Vector3.Dot(v, basisY);
                    double angle = Math.Atan2(y, x);
                    return (point: p, angle);
                })
                .OrderBy(t => t.angle)
                .Select(t => t.point)
                .ToList();

            return ordered;
        }

        /// <summary>将3D点投影到平面坐标</summary>
        private List<Vector2> ProjectToPlane(List<Vector3> points, Vector3 normal, Vector3 centroid)
        {
            Vector3 basisX = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY));
            if (basisX.LengthSquared() < 1e-6f)
                basisX = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX));
            Vector3 basisY = Vector3.Normalize(Vector3.Cross(normal, basisX));

            return points.Select(p =>
            {
                var v = p - centroid;
                return new Vector2(Vector3.Dot(v, basisX), Vector3.Dot(v, basisY));
            }).ToList();
        }

        /// <summary>判断点集是否近似共面</summary>
        private bool IsNearlyCoplanar(List<Vector3> points, out Vector3 normal, out Vector3 centroid, float toleranceFactor = 0.001f)
        {
            centroid = new Vector3(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));

            // Newell 法求法线
            normal = Vector3.Zero;
            for (int i = 0; i < points.Count; i++)
            {
                var curr = points[i];
                var next = points[(i + 1) % points.Count];
                normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
                normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
                normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
            }
            if (normal.LengthSquared() < 1e-8f)
            {
                normal = Vector3.UnitZ;
            }
            else
            {
                normal = Vector3.Normalize(normal);
            }

            // 计算点到平面的最大距离
            float maxRange = 0f;
            foreach (var p in points)
            {
                maxRange = Math.Max(maxRange, (p - centroid).Length());
            }
            float tolerance = Math.Max(0.001f, maxRange * toleranceFactor);

            foreach (var p in points)
            {
                var v = p - centroid;
                float dist = Math.Abs(Vector3.Dot(v, normal));
                if (dist > tolerance)
                    return false;
            }
            return true;
        }

        public void ClearSelection()
        {
            _selectedPoints.Clear();
        }

        public void ClearAll()
        {
            _selectedPoints.Clear();
            _measurements.Clear();
            _latestAreaMeasurement = null;
        }

        public event Action<Measurement>? OnMeasurementCreated;
        public event Action<string>? OnMeasurementMessage;
    }

    /// <summary>测量结果</summary>
    public class Measurement
    {
        public MeasurementType Type { get; set; }
        public List<Vector3> Points { get; set; } = new();
        public float Value { get; set; }
        public string Label { get; set; } = "";
    }

    public enum MeasurementType
    {
        Distance,
        Area
    }

    public enum MeasurementMode
    {
        None,
        Distance,
        Area
    }
}
