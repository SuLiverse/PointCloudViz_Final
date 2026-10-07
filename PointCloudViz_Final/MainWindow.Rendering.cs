using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Core;
using Microsoft.Win32;
using PointCloudViz_Final.Rendering;
using PointCloudViz_Final.Tools;
using Color4 = SharpDX.Color4;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NVector3 = System.Numerics.Vector3;
using NMatrix = System.Numerics.Matrix4x4;
using WPoint = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace PointCloudViz_Final;

public partial class MainWindow
{
    private double _yaw = -55, _pitch = 32, _distance = 100;
    private NVector3 _target, _offset;
    private WPoint _lastMouse, _mouseDown;
    private bool _dragging;
    private MouseButton? _dragButton;

    private void RenderScene()
    {
        if (!_ready || _cloud == null) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var points = _cloud.Points;
        int step = RenderBudget.IsChecked == true ? RenderSampling.Step(points.Count, 1_000_000) : 1;
        var positions = new Vector3Collection((points.Count + step - 1) / step);
        var colors = new Color4Collection(positions.Capacity);
        for (int i = 0; i < points.Count; i += step)
        {
            var p = points[i];
            positions.Add(new SharpDX.Vector3(p.X - _offset.X, p.Y - _offset.Y, p.Z - _offset.Z));
            var color = _colorMap.Map(p, _cloud.BBox);
            colors.Add(new Color4(color.R / 255f, color.G / 255f, color.B / 255f, 1));
        }
        PointModel.Geometry = new PointGeometry3D { Positions = positions, Colors = colors };
        PointModel.IsRendering = CloudVisible.IsChecked == true && points.Count > 0;
        RenderCountText.Text = $"{positions.Count:N0} / {points.Count:N0} points";
        RendererStatus.Text = $"DirectX 11  /  几何更新 {clock.ElapsedMilliseconds} ms";
        LegendPanel.Visibility = _colorMap is HeightColorMap ? Visibility.Visible : Visibility.Collapsed;
        LegendMin.Text = _cloud.BBox.MinZ.ToString("F2");
        LegendMax.Text = _cloud.BBox.MaxZ.ToString("F2");
        BuildGrid();
        RefreshMeasurements();
    }

    private void BuildGrid()
    {
        if (_original == null) return;
        var box = _original.BBox;
        double extent = Math.Max((double)box.MaxX - box.MinX, (double)box.MaxY - box.MinY);
        double spacing = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(0.01, extent / 8))));
        if (extent / spacing > 24) spacing *= 5;
        float size = (float)(Math.Ceiling(extent / 2 / spacing) * spacing + spacing);
        float z = box.MinZ - _offset.Z - (float)spacing * 0.01f;
        var builder = new LineBuilder();
        for (double position = -size; position <= size + spacing * 0.1; position += spacing)
        {
            builder.AddLine(new SharpDX.Vector3((float)position, -size, z), new SharpDX.Vector3((float)position, size, z));
            builder.AddLine(new SharpDX.Vector3(-size, (float)position, z), new SharpDX.Vector3(size, (float)position, z));
        }
        GridModel.Geometry = builder.ToLineGeometry3D();
        GridModel.IsRendering = ShowGrid.IsChecked == true;
    }

    private void DrawHistogram(int[] bins)
    {
        Histogram.Children.Clear();
        Histogram.ColumnDefinitions.Clear();
        int maximum = Math.Max(1, bins.Max());
        var map = new HeightColorMap();
        for (int i = 0; i < bins.Length; i++)
        {
            Histogram.ColumnDefinitions.Add(new ColumnDefinition());
            var color = map.Map(new Models.PointRecord(0, 0, i), new Models.BoundingBox(0, 0, 0, 0, 0, bins.Length - 1));
            var bar = new Rectangle { Fill = new SolidColorBrush(color), Margin = new Thickness(0.5, 0, 0.5, 0),
                Height = bins[i] == 0 ? 0 : Math.Max(1, 68.0 * bins[i] / maximum),
                VerticalAlignment = VerticalAlignment.Bottom, ToolTip = $"{bins[i]:N0} 点" };
            Grid.SetColumn(bar, i);
            Histogram.Children.Add(bar);
        }
    }

    internal void FitScene()
    {
        if (_cloud == null || _cloud.Count == 0) return;
        var box = _cloud.BBox;
        _target = new NVector3((float)(((double)box.MinX + box.MaxX) / 2),
            (float)(((double)box.MinY + box.MaxY) / 2), (float)(((double)box.MinZ + box.MaxZ) / 2)) - _offset;
        double aspect = Math.Max(0.1, Viewport.ActualWidth / Math.Max(1, Viewport.ActualHeight));
        var projection = SceneCamera.CreateProjectionMatrix(aspect);
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var radial = new NVector3((float)(Math.Cos(pitch) * Math.Cos(yaw)),
            (float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch));
        var right = NVector3.Normalize(NVector3.Cross(-radial, NVector3.UnitZ));
        var up = NVector3.Cross(right, -radial);
        double required = .01;
        foreach (float x in new[] { box.MinX, box.MaxX })
        foreach (float y in new[] { box.MinY, box.MaxY })
        foreach (float z in new[] { box.MinZ, box.MaxZ })
        {
            var corner = new NVector3(x, y, z) - _offset - _target;
            required = Math.Max(required, NVector3.Dot(corner, radial) +
                Math.Max(Math.Abs(NVector3.Dot(corner, right)) * projection.M11,
                    Math.Abs(NVector3.Dot(corner, up)) * projection.M22));
        }
        _distance = required * 1.15;
        ApplyCamera();
    }

    private void ApplyCamera()
    {
        if (!_ready) return;
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var radial = new NVector3((float)(Math.Cos(pitch) * Math.Cos(yaw)),
            (float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch));
        var position = _target + radial * (float)_distance;
        var look = _target - position;
        SceneCamera.Position = new(position.X, position.Y, position.Z);
        SceneCamera.LookDirection = new(look.X, look.Y, look.Z);
        SceneCamera.UpDirection = new(0, 0, 1);
        SceneCamera.NearPlaneDistance = Math.Max(0.0001, _distance / 10000);
        SceneCamera.FarPlaneDistance = Math.Max(1000, _distance * 100);
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => FitScene();
    private void MoveCamera(Key key)
    {
        double yaw = _yaw * Math.PI / 180;
        var forward = new NVector3(-(float)Math.Cos(yaw), -(float)Math.Sin(yaw), 0);
        var right = NVector3.Cross(forward, NVector3.UnitZ);
        var direction = key switch { Key.W => forward, Key.S => -forward, Key.D => right,
            Key.A => -right, Key.E => NVector3.UnitZ, _ => -NVector3.UnitZ };
        _target += direction * (float)(_distance * .025);
        ApplyCamera();
        _dirty = true;
    }
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset }) SetPreset(preset);
    }
    private void ViewPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && ViewPreset.SelectedItem is ComboBoxItem { Tag: string preset }) SetPreset(preset);
    }
    private void SetPreset(string preset)
    {
        (_yaw, _pitch) = preset switch { "top" => (-90, 89.9), "front" => (-90, 0), _ => (-55, 32) };
        FitScene();
    }

    private void ColorMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ColorMode.SelectedItem is not ComboBoxItem { Tag: string mode }) return;
        _colorMap = mode switch { "RGB" => new OriginalColorMap(), "Intensity" => new IntensityColorMap(), _ => new HeightColorMap() };
        RenderScene();
        _dirty = true;
    }
    private void SetColorMode(string mode)
    {
        ColorMode.SelectedIndex = mode switch { "RGB" => 1, "Intensity" => 2, _ => 0 };
        _colorMap = mode switch { "RGB" => new OriginalColorMap(), "Intensity" => new IntensityColorMap(), _ => new HeightColorMap() };
    }
    private void PointSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PointModel == null) return;
        PointModel.Size = new System.Windows.Size(e.NewValue, e.NewValue);
        if (PointSizeText != null) PointSizeText.Text = $"{e.NewValue:F0} px";
        if (_ready) _dirty = true;
    }
    private void Background_Click(object sender, RoutedEventArgs e)
    {
        _background = (string)((Button)sender).Tag;
        Viewport.BackgroundColor = (Color)ColorConverter.ConvertFromString(_background);
        _dirty = true;
    }
    private void Grid_Click(object sender, RoutedEventArgs e) => GridModel.IsRendering = ShowGrid.IsChecked == true;
    private void Visibility_Click(object sender, RoutedEventArgs e) => PointModel.IsRendering = CloudVisible.IsChecked == true;
    private void Budget_Click(object sender, RoutedEventArgs e) { RenderScene(); _dirty = true; }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsBusy || _cloud == null || e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return;
        Viewport.Focus();
        _lastMouse = _mouseDown = e.GetPosition(Viewport);
        _dragButton = e.ChangedButton;
        _dragging = false;
        Viewport.CaptureMouse();
        e.Handled = true;
    }
    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragButton == null || IsBusy) return;
        var position = e.GetPosition(Viewport);
        var delta = position - _lastMouse;
        _lastMouse = position;
        _dragging |= (position - _mouseDown).Length > 3;
        if (!_dragging) return;
        if (_dragButton == MouseButton.Left)
        {
            if (_measurements.IsActive && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
            _yaw -= delta.X * 0.35;
            _pitch = Math.Clamp(_pitch + delta.Y * 0.35, -89.9, 89.9);
        }
        else
        {
            var look = SceneCamera.LookDirection;
            var forward = NVector3.Normalize(new NVector3((float)look.X, (float)look.Y, (float)look.Z));
            var right = NVector3.Normalize(NVector3.Cross(forward, NVector3.UnitZ));
            var up = NVector3.Cross(right, forward);
            float scale = (float)(_distance * 0.0016);
            _target += right * (float)(-delta.X) * scale + up * (float)delta.Y * scale;
        }
        ApplyCamera();
        _dirty = true;
        e.Handled = true;
    }
    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragButton == null) return;
        bool pick = !_dragging && _dragButton == MouseButton.Left && _measurements.IsActive;
        _dragButton = null;
        Viewport.ReleaseMouseCapture();
        if (pick) Pick(e.GetPosition(Viewport));
        e.Handled = true;
    }
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_cloud == null || IsBusy) return;
        _distance = Math.Clamp(_distance * Math.Pow(0.85, e.Delta / 120.0), 0.001, 1e12);
        ApplyCamera();
        _dirty = true;
        e.Handled = true;
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string mode }) return;
        _measurements.Mode = Enum.Parse<MeasurementMode>(mode);
        _measurements.IsActive = _measurements.Mode != MeasurementMode.None;
        Viewport.Cursor = _measurements.IsActive ? Cursors.Cross : Cursors.Arrow;
        RefreshMeasurements();
    }
    internal void Pick(WPoint point)
    {
        if (_cloud == null || CloudVisible.IsChecked != true) return;
        double aspect = Viewport.ActualWidth / Math.Max(1, Viewport.ActualHeight);
        // Use the rendering engine's actual matrices, not a separately reconstructed camera.
        var m = SceneCamera.CreateViewMatrix() * SceneCamera.CreateProjectionMatrix(aspect);
        var matrix = new NMatrix(m.M11,m.M12,m.M13,m.M14, m.M21,m.M22,m.M23,m.M24,
            m.M31,m.M32,m.M33,m.M34, m.M41,m.M42,m.M43,m.M44);
        int step = RenderBudget.IsChecked == true ? RenderSampling.Step(_cloud.Count, 1_000_000) : 1;
        var selected = MeasurementTool.PickPoint(point, _cloud.Points, matrix, Viewport.ActualWidth, Viewport.ActualHeight, _offset, step: step);
        if (selected.HasValue) _measurements.AddPoint(selected.Value);
        else StatusText.Text = "该位置没有可拾取的点";
        RefreshMeasurements();
    }
    private void FinishMeasurement_Click(object sender, RoutedEventArgs e) { _measurements.ClearSelection(); RefreshMeasurements(); }
    private void ClearMeasurements_Click(object sender, RoutedEventArgs e)
    {
        _measurements.ClearAll();
        RefreshMeasurements();
        _dirty = true;
    }
    private void RefreshMeasurements()
    {
        if (!_ready) return;
        _measurementRows.Clear();
        foreach (var measurement in _measurements.Measurements) _measurementRows.Add(measurement);
        var lines = new LineBuilder();
        var positions = new Vector3Collection();
        SharpDX.Vector3 Render(NVector3 point) => new(point.X - _offset.X, point.Y - _offset.Y, point.Z - _offset.Z);
        foreach (var measurement in _measurements.Measurements)
        {
            foreach (var point in measurement.Points) positions.Add(Render(point));
            for (int i = 1; i < measurement.Points.Count; i++) lines.AddLine(Render(measurement.Points[i - 1]), Render(measurement.Points[i]));
            if (measurement.Type == MeasurementType.Area)
                lines.AddLine(Render(measurement.Points[^1]), Render(measurement.Points[0]));
        }
        var selected = _measurements.SelectedPoints;
        foreach (var point in selected) positions.Add(Render(point));
        for (int i = 1; i < selected.Count; i++) lines.AddLine(Render(selected[i - 1]), Render(selected[i]));
        MeasurementLines.Geometry = lines.ToLineGeometry3D();
        MeasurementMarkers.Geometry = new PointGeometry3D { Positions = positions };
    }

    internal BitmapSource CaptureViewport() => Viewport.RenderBitmap();
    private async void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || IsBusy) return;
        var dialog = new SaveFileDialog { Filter = "PNG 图像|*.png", FileName = "pointcloud.png" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bitmap = CaptureViewport();
            await IO.AtomicFile.WriteAsync(dialog.FileName, stream =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                return Task.CompletedTask;
            });
            Record("导出视口 PNG");
        }
        catch (Exception ex) { ReportError(ex); }
    }
}
