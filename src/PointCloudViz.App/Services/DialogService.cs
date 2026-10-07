using System.Windows;
using Microsoft.Win32;
using PointCloudViz.App.Views;

namespace PointCloudViz.App.Services;

public sealed class DialogService(Func<Window?> owner) : IDialogService
{
    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(owner()) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName ?? "", AddExtension = true };
        return dialog.ShowDialog(owner()) == true ? dialog.FileName : null;
    }

    public void ShowInfo(string message, string title = "提示") => Show(message, title, MessageBoxImage.Information);

    public void ShowError(string message, string title = "错误") => Show(message, title, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "确认") =>
        (owner() is { } w
            ? MessageBox.Show(w, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)) == MessageBoxResult.Yes;

    public bool EditParameters(string title, string description, IReadOnlyList<ParameterField> fields)
    {
        var dialog = new ParameterDialog(title, description, fields) { Owner = owner() };
        return dialog.ShowDialog() == true;
    }

    private void Show(string message, string title, MessageBoxImage image)
    {
        if (owner() is { } w) MessageBox.Show(w, message, title, MessageBoxButton.OK, image);
        else MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }
}
