using System.Windows;

namespace PointCloudViz_Final
{
    public partial class SimpleOneInputDialog : Window
    {
        public string Value { get; private set; } = "";
        public SimpleOneInputDialog(string label, string defaultValue = "")
        {
            InitializeComponent();
            LabelText.Text = label;
            InputText.Text = defaultValue;
        }
        private void Ok_Click(object sender, RoutedEventArgs e) { Value = InputText.Text.Trim(); DialogResult = true; }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
