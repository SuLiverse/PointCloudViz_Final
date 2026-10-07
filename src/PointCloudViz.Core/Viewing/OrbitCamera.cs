using System.Numerics;
using PointCloudViz.Core.Data;
using PointCloudViz.Core.Picking;

namespace PointCloudViz.Core.Viewing;

/// <summary>预设视角。</summary>
public enum ViewPreset
{
    Top,
    Bottom,
    Front,
    Back,
    Left,
    Right,
    Isometric,
}

/// <summary>
/// Z 轴朝上的轨道相机（转台式交互，与 CloudCompare / 大多数 GIS 软件一致）。
/// <para>
/// 相机绕 <see cref="Target"/> 旋转：<see cref="Yaw"/> 为绕 Z 轴的方位角，<see cref="Pitch"/> 为仰角。
/// 俯仰角限制在 ±89.5°，不会出现旧版"翻过头后上下颠倒"的问题。
/// 测绘数据（LAS 等）本身就是 Z 朝上，旧版使用 Y 朝上的相机，导致地面看起来像一面墙。
/// </para>
/// </summary>
public sealed class OrbitCamera
{
    public const float MaxPitch = 89.5f;

    private float _pitch = 30f;
    private float _distance = 10f;

    public Vector3 Target { get; set; }

    /// <summary>方位角（度），0° 表示相机位于目标点 +X 方向。</summary>
    public float Yaw { get; set; } = -120f;

    /// <summary>仰角（度），正值表示相机在目标点上方。</summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, -MaxPitch, MaxPitch);
    }

    public float Distance
    {
        get => _distance;
        set => _distance = Math.Clamp(value, 1e-3f, 1e7f);
    }

    /// <summary>垂直视场角（度）。</summary>
    public float FieldOfView { get; set; } = 45f;

    public Vector3 Position => Target + Offset * Distance;

    /// <summary>从相机指向目标的单位向量。</summary>
    public Vector3 Forward => -Offset;

    public Vector3 Right
    {
        get
        {
            var r = Vector3.Cross(Forward, Vector3.UnitZ);
            return r.LengthSquared() < 1e-10f ? Vector3.UnitX : Vector3.Normalize(r);
        }
    }

    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

    private Vector3 Offset
    {
        get
        {
            float yaw = Yaw * MathF.PI / 180f, pitch = Pitch * MathF.PI / 180f;
            return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        }
    }

    /// <summary>鼠标拖拽旋转。</summary>
    public void Orbit(float deltaXPixels, float deltaYPixels, float degreesPerPixel = 0.3f)
    {
        Yaw = NormalizeAngle(Yaw - deltaXPixels * degreesPerPixel);
        Pitch += deltaYPixels * degreesPerPixel;
    }

    /// <summary>鼠标拖拽平移：光标下的内容跟随鼠标移动。</summary>
    public void Pan(float deltaXPixels, float deltaYPixels, float viewportHeight)
    {
        float worldPerPixel = WorldUnitsPerPixel(viewportHeight);
        Target += (-Right * deltaXPixels + Up * deltaYPixels) * worldPerPixel;
    }

    /// <summary>沿视线方向移动目标点（键盘漫游）。<paramref name="forward"/>/<paramref name="right"/> 在水平面内移动。</summary>
    public void Move(float forward, float right, float up)
    {
        var f = Forward with { Z = 0 };
        f = f.LengthSquared() < 1e-8f ? Up with { Z = 0 } : f;
        f = Vector3.Normalize(f);
        var r = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitZ));
        Target += (f * forward + r * right + Vector3.UnitZ * up) * Distance;
    }

    /// <summary>以目标点为中心缩放。<paramref name="factor"/> &lt; 1 拉近。</summary>
    public void Zoom(float factor) => Distance *= factor;

    /// <summary>
    /// 朝光标方向缩放：焦点为射线与"过目标点、垂直于视线"平面的交点，
    /// 缩放时该焦点在屏幕上保持不动。
    /// </summary>
    public void ZoomTowards(float factor, Ray3 ray)
    {
        var n = Forward;
        float denom = Vector3.Dot(ray.Direction, n);
        if (MathF.Abs(denom) < 1e-6f)
        {
            Zoom(factor);
            return;
        }
        float t = Vector3.Dot(Target - ray.Origin, n) / denom;
        var focus = ray.PointAt(t);
        Target = focus + (Target - focus) * factor;
        Distance *= factor;
    }

    /// <summary>调整相机使包围盒完整可见。</summary>
    public void Fit(BoundingBox bounds, float aspectRatio = 1.5f)
    {
        if (bounds.IsEmpty) return;
        Target = bounds.Center;
        float radius = MathF.Max(bounds.Diagonal * 0.5f, 1e-3f);
        float halfV = FieldOfView * MathF.PI / 360f;
        float halfH = MathF.Atan(MathF.Tan(halfV) * MathF.Max(aspectRatio, 0.1f));
        Distance = radius / MathF.Sin(MathF.Min(halfV, halfH)) * 1.05f;
    }

    public void SetPreset(ViewPreset preset)
    {
        (Yaw, Pitch) = preset switch
        {
            ViewPreset.Top => (-90f, MaxPitch),
            ViewPreset.Bottom => (-90f, -MaxPitch),
            ViewPreset.Front => (-90f, 0f),
            ViewPreset.Back => (90f, 0f),
            ViewPreset.Left => (180f, 0f),
            ViewPreset.Right => (0f, 0f),
            _ => (-120f, 30f),
        };
    }

    /// <summary>当前视距下每像素对应的世界长度（目标点所在深度）。</summary>
    public float WorldUnitsPerPixel(float viewportHeight) =>
        2f * Distance * MathF.Tan(FieldOfView * MathF.PI / 360f) / MathF.Max(1f, viewportHeight);

    /// <summary>每像素对应的视角（弧度），用于把像素容差换算为拾取容差角。</summary>
    public float RadiansPerPixel(float viewportHeight) => FieldOfView * MathF.PI / 180f / MathF.Max(1f, viewportHeight);

    /// <summary>由屏幕坐标（像素，原点左上）生成拾取射线。</summary>
    public Ray3 ScreenRay(float x, float y, float width, float height)
    {
        float tanY = MathF.Tan(FieldOfView * MathF.PI / 360f);
        float aspect = width / MathF.Max(1f, height);
        float ndcX = x / width * 2f - 1f;
        float ndcY = 1f - y / height * 2f;
        var dir = Forward + Right * (ndcX * tanY * aspect) + Up * (ndcY * tanY);
        return new Ray3(Position, dir);
    }

    /// <summary>投影到屏幕：返回像素坐标与视深；点在相机后方时返回 false。</summary>
    public bool Project(Vector3 p, float width, float height, out Vector2 screen, out float depth)
    {
        var v = p - Position;
        depth = Vector3.Dot(v, Forward);
        screen = default;
        if (depth <= 1e-6f) return false;
        float tanY = MathF.Tan(FieldOfView * MathF.PI / 360f);
        float aspect = width / MathF.Max(1f, height);
        float x = Vector3.Dot(v, Right) / (depth * tanY * aspect);
        float y = Vector3.Dot(v, Up) / (depth * tanY);
        screen = new Vector2((x * 0.5f + 0.5f) * width, (0.5f - y * 0.5f) * height);
        return true;
    }

    public OrbitCamera Clone() => new()
    {
        Target = Target,
        Yaw = Yaw,
        Pitch = Pitch,
        Distance = Distance,
        FieldOfView = FieldOfView,
    };

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        return degrees > 180f ? degrees - 360f : degrees < -180f ? degrees + 360f : degrees;
    }
}
