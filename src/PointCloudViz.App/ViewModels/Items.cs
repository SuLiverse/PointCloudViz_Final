using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PointCloudViz.App.Infrastructure;
using PointCloudViz.Core.Coloring;
using PointCloudViz.Core.Data;

namespace PointCloudViz.App.ViewModels;

/// <summary>交互工具。</summary>
public enum ToolMode
{
    Navigate,
    Point,
    Distance,
    Polyline,
    Area,
}

/// <summary>着色模式选项。选项对象只创建一次，换点云时仅更新可用性，避免下拉框丢失选中项。</summary>
public sealed partial class ColorModeOption(ColorMode mode, string name) : ObservableObject
{
    public ColorMode Mode { get; } = mode;
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isAvailable = true;
}

public sealed record BackgroundOption(string Name, Rgb24 Color)
{
    public Brush Brush { get; } = Color.ToBrush();
}

public sealed record CountOption(string Name, int Value);

/// <summary>高程直方图中的一根柱子。</summary>
public sealed record HistogramBar(double Fraction, Brush Fill, string ToolTip);

/// <summary>分类列表项，勾选状态控制该分类是否显示（不修改数据）。</summary>
public sealed partial class ClassItem(byte code, int count, double percent, Action onVisibilityChanged) : ObservableObject
{
    public byte Code { get; } = code;
    public string Name { get; } = ClassificationColors.GetName(code);
    public int Count { get; } = count;
    public double Percent { get; } = percent;
    public Brush Swatch { get; } = ClassificationColors.GetColor(code).ToBrush();
    public string CountText => $"{Count:N0}（{Percent:F1}%）";

    [ObservableProperty]
    private bool _isVisible = true;

    partial void OnIsVisibleChanged(bool value) => onVisibilityChanged();
}

/// <summary>图例可见性：已加载点云且当前为连续色带着色。</summary>
public sealed class LegendVisibility : System.Windows.Data.IMultiValueConverter
{
    public static LegendVisibility Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        values.All(v => v is true) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
