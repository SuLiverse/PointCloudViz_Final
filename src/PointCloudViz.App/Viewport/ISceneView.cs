using PointCloudViz.Core.Data;
using PointCloudViz.Core.Measurements;
using PointCloudViz.Core.Project;
using PointCloudViz.Core.Viewing;

namespace PointCloudViz.App.Viewport;

/// <summary>视图模型操作三维场景的抽象（视图模型不直接依赖 Helix 控件）。</summary>
public interface ISceneView
{
    /// <summary>显示点云（已抽稀的显示子集）及其颜色。</summary>
    Task SetCloudAsync(PointCloud? cloud, Rgb24[]? colors, bool resetCamera);

    Task SetColorsAsync(Rgb24[] colors);

    void SetPointSize(double size);

    void SetBackground(Rgb24 color);

    void SetShowAxes(bool show);

    /// <summary>刷新量测叠加层：已完成的量测、正在绘制的顶点、高亮的量测。</summary>
    void SetOverlay(IReadOnlyList<Measurement> measurements, IReadOnlyList<Double3> pending, MeasurementKind pendingKind, int? highlightId);

    void FitView();

    void SetView(ViewPreset preset);

    /// <summary>当前相机状态（目标点为世界坐标）。</summary>
    CameraState? GetCameraState();

    void SetCameraState(CameraState state);

    /// <summary>将当前画面保存为 PNG，成功返回 true。</summary>
    bool SaveScreenshot(string path);
}
