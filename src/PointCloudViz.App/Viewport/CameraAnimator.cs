using System.Numerics;
using System.Windows.Media;
using PointCloudViz.Core.Viewing;

namespace PointCloudViz.App.Viewport;

/// <summary>相机平滑过渡（切换视角、适应窗口、双击设旋转中心时使用）。</summary>
internal sealed class CameraAnimator(OrbitCamera camera, Action apply)
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(320);

    private OrbitCamera? _from;
    private OrbitCamera? _to;
    private DateTime _start;
    private bool _running;

    public void AnimateTo(OrbitCamera target)
    {
        _from = camera.Clone();
        _to = target;
        // 方位角走最短弧
        float delta = (_to.Yaw - _from.Yaw) % 360f;
        if (delta > 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        _to.Yaw = _from.Yaw + delta;
        _start = DateTime.UtcNow;
        if (!_running)
        {
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_from is null || _to is null)
        {
            Stop();
            return;
        }
        double t = Math.Clamp((DateTime.UtcNow - _start) / Duration, 0, 1);
        float k = (float)(1 - Math.Pow(1 - t, 3)); // ease-out cubic

        camera.Target = Vector3.Lerp(_from.Target, _to.Target, k);
        camera.Yaw = _from.Yaw + (_to.Yaw - _from.Yaw) * k;
        camera.Pitch = _from.Pitch + (_to.Pitch - _from.Pitch) * k;
        // 距离按对数插值，远近变化时速度感一致
        camera.Distance = MathF.Exp(MathF.Log(_from.Distance) + (MathF.Log(_to.Distance) - MathF.Log(_from.Distance)) * k);
        apply();
        if (t >= 1) Stop();
    }
}
