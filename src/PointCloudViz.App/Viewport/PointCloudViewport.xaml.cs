using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelixToolkit.Wpf.SharpDX;
using PointCloudViz.App.Infrastructure;
using PointCloudViz.Core.Data;
using PointCloudViz.Core.Measurements;
using PointCloudViz.Core.Picking;
using PointCloudViz.Core.Project;
using PointCloudViz.Core.Viewing;
using Media3D = System.Windows.Media.Media3D;
using DxVector3 = SharpDX.Vector3;
using WpfPoint = System.Windows.Point;

namespace PointCloudViz.App.Viewport;

/// <summary>
/// 点云三维视口：封装 Helix Toolkit 的 Viewport3DX。
/// <para>
/// 相机完全由 <see cref="OrbitCamera"/>（Core 中可测试的纯数学类）驱动，Helix 自带手势全部关闭。
/// 旧版同时存在"自己的相机变量"和 Helix 内部相机，两者互相覆盖，需要每帧同步、保存/恢复状态来"打补丁"；
/// 新版只有一个数据源，从根本上消除了缩放后视角被重置的问题。
/// </para>
/// </summary>
public partial class PointCloudViewport : UserControl, ISceneView
{
    private const double DragThreshold = 4;
    private const float PickTolerancePixels = 7;

    private readonly OrbitCamera _camera = new();
    private readonly CameraAnimator _animator;
    private PointCloud? _cloud;
    private Vector3Collection? _positions;

    private WpfPoint _pressPosition;
    private WpfPoint _lastPosition;
    private MouseButton? _pressedButton;
    private bool _dragging;
    private DateTime _lastHover;

    public PointCloudViewport()
    {
        InitializeComponent();
        _animator = new CameraAnimator(_camera, ApplyCamera);
        try
        {
            View.EffectsManager = new DefaultEffectsManager();
        }
        catch (Exception ex)
        {
            Log.Error("初始化 DirectX 渲染失败", ex);
            InitializationError = ex.Message;
        }

        PreviewMouseDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseUp += OnMouseUp;
        PreviewMouseWheel += OnMouseWheel;
        MouseLeave += (_, _) => HoverChanged?.Invoke(this, null);
        SizeChanged += (_, _) => ApplyCamera();
        Loaded += (_, _) => ApplyCamera();
    }

    /// <summary>渲染引擎初始化失败时的错误信息。</summary>
    public string? InitializationError { get; }

    /// <summary>单击拾取到点（世界坐标）；未拾取到时参数为 null。</summary>
    public event EventHandler<PointClickedEventArgs>? PointClicked;

    /// <summary>左键双击。处理程序把 <see cref="System.ComponentModel.HandledEventArgs.Handled"/> 置为 true 可阻止默认行为（设旋转中心）。</summary>
    public event EventHandler<System.ComponentModel.HandledEventArgs>? DoubleClicked;

    /// <summary>右键单击（未拖动）。</summary>
    public event EventHandler? RightClicked;

    /// <summary>鼠标悬停处的点（世界坐标），节流到约 12 Hz。</summary>
    public event EventHandler<Double3?>? HoverChanged;

    public OrbitCamera OrbitCamera => _camera;

    #region ISceneView

    public async Task SetCloudAsync(PointCloud? cloud, Rgb24[]? colors, bool resetCamera)
    {
        _cloud = cloud;
        if (cloud is null || cloud.IsEmpty)
        {
            _positions = null;
            PointModel.Geometry = null;
            return;
        }

        var (positions, colorCollection) = await Task.Run(() =>
        {
            var pts = cloud.Points;
            var p = new Vector3Collection(pts.Length);
            foreach (ref readonly var r in pts) p.Add(new DxVector3(r.Position.X, r.Position.Y, r.Position.Z));
            return (p, colors is null ? null : BuildColors(colors));
        });
        if (!ReferenceEquals(cloud, _cloud)) return; // 期间又加载了别的点云

        _positions = positions;
        PointModel.Geometry = new PointGeometry3D { Positions = positions, Colors = colorCollection };
        if (resetCamera)
        {
            _animator.Stop();
            _camera.SetPreset(ViewPreset.Isometric);
            _camera.Fit(cloud.Bounds, Aspect);
            ApplyCamera();
        }
        else
        {
            ApplyCamera();
        }
    }

    public async Task SetColorsAsync(Rgb24[] colors)
    {
        if (_positions is null) return;
        var positions = _positions;
        var collection = await Task.Run(() => BuildColors(colors));
        if (!ReferenceEquals(positions, _positions)) return;
        PointModel.Geometry = new PointGeometry3D { Positions = positions, Colors = collection };
    }

    public void SetPointSize(double size) => PointModel.Size = new Size(size, size);

    public void SetBackground(Rgb24 color)
    {
        View.BackgroundColor = color.ToWpf();
        View.CoordinateSystemLabelForeground = color.Contrasting().ToWpf();
    }

    public void SetShowAxes(bool show) => View.ShowCoordinateSystem = show;

    public void SetOverlay(IReadOnlyList<Measurement> measurements, IReadOnlyList<Double3> pending, MeasurementKind pendingKind, int? highlightId)
    {
        if (_cloud is null)
        {
            MeasureLines.Geometry = HighlightLines.Geometry = PendingLines.Geometry = null;
            Markers.Geometry = null;
            Labels.Geometry = null;
            return;
        }

        var lines = new LineBuilder();
        var highlight = new LineBuilder();
        var pendingLines = new LineBuilder();
        var markers = new Vector3Collection();
        var labels = new BillboardText3D();
        bool anyLine = false, anyHighlight = false, anyPending = false;

        foreach (var m in measurements)
        {
            var pts = m.Points.Select(ToLocal).ToList();
            foreach (var p in pts) markers.Add(p);
            var builder = m.Id == highlightId ? highlight : lines;
            bool added = AddPath(builder, pts, closed: m.Kind == MeasurementKind.Area);
            if (m.Id == highlightId) anyHighlight |= added; else anyLine |= added;
            if (pts.Count > 0) labels.TextInfo.Add(CreateLabel(m, pts));
        }

        var pendingLocal = pending.Select(ToLocal).ToList();
        foreach (var p in pendingLocal) markers.Add(p);
        anyPending = AddPath(pendingLines, pendingLocal, closed: pendingKind == MeasurementKind.Area && pendingLocal.Count >= 3);

        MeasureLines.Geometry = anyLine ? lines.ToLineGeometry3D() : null;
        HighlightLines.Geometry = anyHighlight ? highlight.ToLineGeometry3D() : null;
        PendingLines.Geometry = anyPending ? pendingLines.ToLineGeometry3D() : null;
        Markers.Geometry = markers.Count > 0 ? new PointGeometry3D { Positions = markers } : null;
        Labels.Geometry = labels.TextInfo.Count > 0 ? labels : null;
    }

    public void FitView()
    {
        if (_cloud is null) return;
        var target = _camera.Clone();
        target.Fit(_cloud.Bounds, Aspect);
        _animator.AnimateTo(target);
    }

    public void SetView(ViewPreset preset)
    {
        if (_cloud is null) return;
        var target = _camera.Clone();
        target.SetPreset(preset);
        target.Fit(_cloud.Bounds, Aspect);
        _animator.AnimateTo(target);
    }

    public CameraState? GetCameraState()
    {
        if (_cloud is null) return null;
        var t = _cloud.ToWorld(_camera.Target);
        return new CameraState([t.X, t.Y, t.Z], _camera.Yaw, _camera.Pitch, _camera.Distance);
    }

    public void SetCameraState(CameraState state)
    {
        if (_cloud is null || state.Target is not { Length: 3 } t) return;
        _animator.Stop();
        _camera.Target = _cloud.ToLocal(new Double3(t[0], t[1], t[2]));
        _camera.Yaw = state.Yaw;
        _camera.Pitch = state.Pitch;
        _camera.Distance = state.Distance;
        ApplyCamera();
    }

    public bool SaveScreenshot(string path)
    {
        try
        {
            var bitmap = View.RenderBitmap();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or SharpDX.SharpDXException)
        {
            Log.Error("截图失败", ex);
            return false;
        }
    }

    #endregion

    #region 键盘漫游（由窗口在视口获得焦点时转发）

    /// <summary>处理导航按键，返回是否已处理。</summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (_cloud is null || modifiers.HasFlag(ModifierKeys.Control)) return false;
        float step = 0.05f * (modifiers.HasFlag(ModifierKeys.Shift) ? 4f : 1f);
        switch (key)
        {
            case Key.W: Move(step, 0, 0); return true;
            case Key.S: Move(-step, 0, 0); return true;
            case Key.A: Move(0, -step, 0); return true;
            case Key.D: Move(0, step, 0); return true;
            case Key.Q: Move(0, 0, step); return true;
            case Key.E: Move(0, 0, -step); return true;
            case Key.Add or Key.OemPlus: _animator.Stop(); _camera.Zoom(0.85f); ApplyCamera(); return true;
            case Key.Subtract or Key.OemMinus: _animator.Stop(); _camera.Zoom(1 / 0.85f); ApplyCamera(); return true;
            default: return false;
        }
    }

    private void Move(float forward, float right, float up)
    {
        _animator.Stop();
        _camera.Move(forward, right, up);
        ApplyCamera();
    }

    #endregion

    #region 鼠标交互

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_pressedButton is not null) return;

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            var args = new System.ComponentModel.HandledEventArgs();
            DoubleClicked?.Invoke(this, args);
            // 默认行为：以拾取点为新的旋转中心（与 CloudCompare 一致）
            if (!args.Handled && Pick(e.GetPosition(this)) is { } hit)
            {
                var target = _camera.Clone();
                target.Target = hit.Position;
                _animator.AnimateTo(target);
            }
            e.Handled = true;
            return;
        }

        _pressedButton = e.ChangedButton;
        _pressPosition = _lastPosition = e.GetPosition(this);
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_pressedButton is { } button)
        {
            if (!_dragging && (pos - _pressPosition).Length < DragThreshold) return;
            _dragging = true;
            _animator.Stop();
            var delta = pos - _lastPosition;
            _lastPosition = pos;
            if (button == MouseButton.Left)
                _camera.Orbit((float)delta.X, (float)delta.Y);
            else
                _camera.Pan((float)delta.X, (float)delta.Y, (float)Math.Max(1, ActualHeight));
            ApplyCamera();
            e.Handled = true;
            return;
        }

        if (HoverChanged is not null && (DateTime.UtcNow - _lastHover).TotalMilliseconds > 80)
        {
            _lastHover = DateTime.UtcNow;
            var hit = Pick(pos);
            HoverChanged.Invoke(this, hit is { } h && _cloud is not null ? _cloud.ToWorld(h.Position) : null);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedButton != e.ChangedButton) return;
        _pressedButton = null;
        ReleaseMouseCapture();
        e.Handled = true;
        if (_dragging) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            var hit = Pick(e.GetPosition(this));
            Double3? world = hit is { } h && _cloud is not null ? _cloud.ToWorld(h.Position) : null;
            PointClicked?.Invoke(this, new PointClickedEventArgs(world));
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            RightClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _animator.Stop();
        float factor = MathF.Pow(0.88f, e.Delta / 120f);
        var ray = GetRay(e.GetPosition(this));
        _camera.ZoomTowards(factor, ray);
        ApplyCamera();
        e.Handled = true;
    }

    #endregion

    private float Aspect => (float)(Math.Max(1, ActualWidth) / Math.Max(1, ActualHeight));

    private DxVector3 ToLocal(Double3 world)
    {
        var v = _cloud!.ToLocal(world);
        return new DxVector3(v.X, v.Y, v.Z);
    }

    private PickResult? Pick(WpfPoint position)
    {
        if (_cloud is null || _cloud.IsEmpty) return null;
        var ray = GetRay(position);
        float tolerance = PickTolerancePixels * _camera.RadiansPerPixel((float)Math.Max(1, ActualHeight));
        return PointPicker.Pick(_cloud, ray, tolerance, _camera.Distance * 1e-4f);
    }

    /// <summary>
    /// 屏幕坐标转拾取射线。优先使用 Helix 的反投影（与实际渲染的矩阵完全一致），
    /// 渲染尚未就绪时退回到 <see cref="OrbitCamera.ScreenRay"/>。
    /// </summary>
    private Ray3 GetRay(WpfPoint p)
    {
        try
        {
            var ray = View.UnProject(p);
            var origin = new Vector3(ray.Position.X, ray.Position.Y, ray.Position.Z);
            var direction = new Vector3(ray.Direction.X, ray.Direction.Y, ray.Direction.Z);
            if (float.IsFinite(origin.X) && float.IsFinite(direction.X) && direction.LengthSquared() > 1e-12f)
                return new Ray3(origin, direction);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentException)
        {
            // 渲染器尚未初始化
        }
        return _camera.ScreenRay((float)p.X, (float)p.Y, (float)Math.Max(1, ActualWidth), (float)Math.Max(1, ActualHeight));
    }

    private void ApplyCamera()
    {
        var pos = _camera.Position;
        var look = _camera.Forward * _camera.Distance;
        var up = _camera.Up;
        Camera.Position = new Media3D.Point3D(pos.X, pos.Y, pos.Z);
        Camera.LookDirection = new Media3D.Vector3D(look.X, look.Y, look.Z);
        Camera.UpDirection = new Media3D.Vector3D(up.X, up.Y, up.Z);
        Camera.FieldOfView = _camera.FieldOfView;

        // 近/远裁剪面随视距自适应，兼顾近处细节与远处不被裁掉
        float extent = _cloud is { IsEmpty: false } c ? c.Bounds.Diagonal : 100f;
        Camera.NearPlaneDistance = Math.Max(1e-4, _camera.Distance * 1e-3);
        Camera.FarPlaneDistance = (_camera.Distance + extent) * 4;
    }

    private static Color4Collection BuildColors(Rgb24[] colors)
    {
        var c = new Color4Collection(colors.Length);
        foreach (var color in colors) c.Add(color.ToColor4());
        return c;
    }

    private static bool AddPath(LineBuilder builder, IReadOnlyList<DxVector3> pts, bool closed)
    {
        if (pts.Count < 2) return false;
        for (int i = 1; i < pts.Count; i++) builder.AddLine(pts[i - 1], pts[i]);
        if (closed) builder.AddLine(pts[^1], pts[0]);
        return true;
    }

    private static TextInfo CreateLabel(Measurement m, IReadOnlyList<DxVector3> pts)
    {
        DxVector3 anchor = pts[0];
        string text = $"#{m.Id}";
        switch (m.Kind)
        {
            case MeasurementKind.Point:
                text = $"#{m.Id}  Z={m.Points[0].Z:F3}";
                break;
            case MeasurementKind.Distance or MeasurementKind.Polyline when pts.Count >= 2:
                anchor = (pts[^2] + pts[^1]) * 0.5f;
                text = $"#{m.Id}  {m.Length:F3} m";
                break;
            case MeasurementKind.Area when pts.Count >= 3:
                anchor = pts.Aggregate(DxVector3.Zero, (a, b) => a + b) / pts.Count;
                text = $"#{m.Id}  {m.Area:F2} m²";
                break;
        }
        return new TextInfo(text, anchor)
        {
            Foreground = new SharpDX.Color4(1f, 1f, 1f, 1f),
            Background = new SharpDX.Color4(0.1f, 0.1f, 0.12f, 0.75f),
            Scale = 0.9f,
        };
    }
}

public sealed class PointClickedEventArgs(Double3? world) : EventArgs
{
    /// <summary>拾取到的世界坐标；为 null 表示鼠标处没有点。</summary>
    public Double3? World { get; } = world;
}
