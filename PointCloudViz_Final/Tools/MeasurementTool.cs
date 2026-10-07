using System.Numerics;
using System.Windows;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Tools;

public class MeasurementTool
{
    private readonly List<Vector3> _selectedPoints = new();
    private readonly List<Measurement> _measurements = new();
    private Measurement? _latestArea;
    private MeasurementMode _mode;
    private bool _active;
    public bool IsActive { get => _active; set { _active = value; if (!value) ClearSelection(); } }
    public MeasurementMode Mode { get => _mode; set { _mode = value; ClearSelection(); } }
    public IReadOnlyList<Vector3> SelectedPoints => _selectedPoints;
    public IReadOnlyList<Measurement> Measurements => _measurements;

    public bool OnMouseClick(Point screen, Camera camera, PointCloud? cloud, int width, int height)
    {
        if (cloud == null) return false;
        var point = PickPoint(screen, cloud.Points, camera.ViewMatrix * camera.ProjectionMatrix(width / (float)height), width, height);
        return point.HasValue && AddPoint(point.Value);
    }

    public static Vector3? PickPoint(Point screen, IReadOnlyList<PointRecord> points, Matrix4x4 viewProjection,
        double width, double height, Vector3 offset = default, double radius = 12, int step = 1)
    {
        if (step < 1) throw new ArgumentOutOfRangeException(nameof(step));
        if (width <= 0 || height <= 0) return null;
        double bestDistance = radius * radius, bestDepth = double.MaxValue;
        Vector3? best = null;
        for (int i = 0; i < points.Count; i += step)
        {
            var p = points[i];
            var clip = Vector4.Transform(new Vector4(p.X - offset.X, p.Y - offset.Y, p.Z - offset.Z, 1), viewProjection);
            if (clip.W <= 0 || clip.Z < 0 || clip.Z > clip.W) continue;
            double x = (clip.X / clip.W + 1) * width / 2;
            double y = (1 - clip.Y / clip.W) * height / 2;
            double distance = (x - screen.X) * (x - screen.X) + (y - screen.Y) * (y - screen.Y);
            if (distance < bestDistance || (Math.Abs(distance - bestDistance) < 0.01 && clip.W < bestDepth))
            {
                best = new Vector3(p.X, p.Y, p.Z);
                bestDistance = distance;
                bestDepth = clip.W;
            }
        }
        return best;
    }

    public bool AddPoint(Vector3 point)
    {
        if (!IsActive || Mode == MeasurementMode.None) return false;
        if (_selectedPoints.Contains(point)) return false;
        _selectedPoints.Add(point);
        if (Mode == MeasurementMode.Distance && _selectedPoints.Count == 2)
        {
            float value = Vector3.Distance(_selectedPoints[0], _selectedPoints[1]);
            var result = new Measurement { Type = MeasurementType.Distance, Points = new(_selectedPoints),
                Value = value, Label = $"Distance  {value:F3} u" };
            _measurements.Add(result);
            ClearSelection();
            OnMeasurementCreated?.Invoke(result);
        }
        else if (Mode == MeasurementMode.Area && _selectedPoints.Count >= 3)
        {
            if (!TryArea(_selectedPoints, out var area))
            {
                _selectedPoints.RemoveAt(_selectedPoints.Count - 1);
                OnMeasurementMessage?.Invoke("选点必须共面、非共线，且多边形边不能交叉。");
                return false;
            }
            if (_latestArea != null) _measurements.Remove(_latestArea);
            _latestArea = new Measurement { Type = MeasurementType.Area, Points = new(_selectedPoints),
                Value = area, Label = $"Area  {area:F3} u²" };
            _measurements.Add(_latestArea);
            OnMeasurementCreated?.Invoke(_latestArea);
        }
        return true;
    }

    public static bool TryArea(IReadOnlyList<Vector3> points, out float area)
    {
        area = 0;
        if (points.Count < 3) return false;
        var origin = points[0];
        Vector3 normal = Vector3.Zero;
        for (int i = 1; i < points.Count - 1 && normal.LengthSquared() < 1e-12f; i++)
            normal = Vector3.Cross(points[i] - origin, points[i + 1] - origin);
        if (normal.LengthSquared() < 1e-12f) return false;
        normal = Vector3.Normalize(normal);
        float extent = points.Max(p => Vector3.Distance(p, origin));
        if (points.Any(p => Math.Abs(Vector3.Dot(p - origin, normal)) > Math.Max(1e-5f, extent * 0.001f))) return false;
        var reference = Math.Abs(normal.Z) < 0.9 ? Vector3.UnitZ : Vector3.UnitX;
        var basisX = Vector3.Normalize(Vector3.Cross(normal, reference));
        var basisY = Vector3.Cross(normal, basisX);
        var projected = points.Select(p => new Vector2(Vector3.Dot(p - origin, basisX), Vector3.Dot(p - origin, basisY))).ToArray();
        double Cross(Vector2 a, Vector2 b, Vector2 c) => ((double)b.X - a.X) * (c.Y - a.Y) - ((double)b.Y - a.Y) * (c.X - a.X);
        for (int i = 0; i < projected.Length; i++)
        for (int j = i + 1; j < projected.Length; j++)
        {
            int ni = (i + 1) % projected.Length, nj = (j + 1) % projected.Length;
            if (ni == j || nj == i) continue;
            var a = projected[i]; var b = projected[ni]; var c = projected[j]; var d = projected[nj];
            if (Cross(a,b,c) * Cross(a,b,d) <= 0 && Cross(c,d,a) * Cross(c,d,b) <= 0 &&
                Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X)) <= Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X)) &&
                Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y)) <= Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y))) return false;
        }
        double sum = 0;
        for (int i = 0; i < projected.Length; i++)
        {
            var p = projected[i]; var q = projected[(i + 1) % projected.Length];
            sum += (double)p.X * q.Y - (double)q.X * p.Y;
        }
        area = (float)(Math.Abs(sum) / 2);
        return float.IsFinite(area) && area > 0;
    }

    public void ClearSelection() { _selectedPoints.Clear(); _latestArea = null; }
    public void ClearAll() { ClearSelection(); _measurements.Clear(); }
    public void Restore(IEnumerable<Measurement> measurements)
    {
        ClearAll();
        _measurements.AddRange(measurements);
    }
    public event Action<Measurement>? OnMeasurementCreated;
    public event Action<string>? OnMeasurementMessage;
}

public class Measurement
{
    public MeasurementType Type { get; set; }
    public List<Vector3> Points { get; set; } = new();
    public float Value { get; set; }
    public string Label { get; set; } = "";
}
public enum MeasurementType { Distance, Area }
public enum MeasurementMode { None, Distance, Area }
