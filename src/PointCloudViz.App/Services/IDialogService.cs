namespace PointCloudViz.App.Services;

/// <summary>对话框服务：视图模型通过它与用户交互，便于替换与测试。</summary>
public interface IDialogService
{
    string? OpenFile(string title, string filter);

    string? SaveFile(string title, string filter, string? defaultFileName = null);

    void ShowInfo(string message, string title = "提示");

    void ShowError(string message, string title = "错误");

    bool Confirm(string message, string title = "确认");

    /// <summary>显示参数输入对话框，用户确认返回 true 且字段值已更新。</summary>
    bool EditParameters(string title, string description, IReadOnlyList<ParameterField> fields);
}

/// <summary>参数对话框中的一个数值输入项。</summary>
public sealed class ParameterField(string label, double value, double minimum = double.MinValue, double maximum = double.MaxValue, string? hint = null)
{
    public string Label { get; } = label;
    public double Value { get; set; } = value;
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
    public string? Hint { get; } = hint;

    /// <summary>是否只接受整数。</summary>
    public bool IsInteger { get; init; }

    public string Text { get; set; } = "";
}
