using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PointCloudViz.App.ViewModels;
using PointCloudViz.Core.Coloring;

namespace PointCloudViz.App.Converters;

/// <summary>布尔 → 可见性，<c>Invert=True</c> 时取反。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) != Invert;
}

/// <summary>对象非空（字符串非空白）→ 可见。</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string s && string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>枚举值与参数相等 → true；用于工具栏切换按钮绑定 <see cref="ToolMode"/>。</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && value.ToString() == parameter.ToString();

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>色带 → 横向渐变画刷（色带下拉框预览）。</summary>
public sealed class PaletteToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is Palette p ? MainViewModel.PaletteBrush(p, vertical: false) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0~1 的比例 × 参数（像素）→ 高度，用于直方图柱高。</summary>
public sealed class FractionToLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double max = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 100;
        return value is double d ? Math.Max(1, d * max) : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>完整路径 → 文件名（最近文件菜单）。</summary>
public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? System.IO.Path.GetFileName(s) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
