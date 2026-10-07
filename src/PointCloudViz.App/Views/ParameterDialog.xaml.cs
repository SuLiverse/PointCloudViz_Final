using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PointCloudViz.App.Services;

namespace PointCloudViz.App.Views;

/// <summary>通用参数输入对话框，替代旧版的 SimpleOneInputDialog / SimpleTwoInputDialog，并增加范围校验。</summary>
public partial class ParameterDialog : Window
{
    private readonly IReadOnlyList<ParameterField> _fields;

    public ParameterDialog(string title, string description, IReadOnlyList<ParameterField> fields)
    {
        InitializeComponent();
        Title = title;
        DescriptionText.Text = description;
        DescriptionText.Visibility = string.IsNullOrWhiteSpace(description) ? Visibility.Collapsed : Visibility.Visible;
        _fields = fields;
        foreach (var f in fields)
            f.Text = f.IsInteger ? ((long)f.Value).ToString(CultureInfo.InvariantCulture) : f.Value.ToString("G6", CultureInfo.InvariantCulture);
        FieldsList.ItemsSource = fields;
        Loaded += (_, _) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _fields)
        {
            if (!TryParse(f.Text, out double v) || (f.IsInteger && v != Math.Floor(v)))
            {
                ShowError($"“{f.Label.TrimEnd('：', ':')}”不是有效的{(f.IsInteger ? "整数" : "数值")}。");
                return;
            }
            if (v < f.Minimum || v > f.Maximum)
            {
                ShowError($"“{f.Label.TrimEnd('：', ':')}”应在 {Format(f.Minimum)} ~ {Format(f.Maximum)} 之间。");
                return;
            }
            f.Value = v;
        }
        DialogResult = true;
    }

    /// <summary>同时接受 "0.5" 与本地化的 "0,5"。</summary>
    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string Format(double v) => Math.Abs(v) >= 1e15 ? (v < 0 ? "-∞" : "∞") : v.ToString("G6", CultureInfo.InvariantCulture);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }
}
