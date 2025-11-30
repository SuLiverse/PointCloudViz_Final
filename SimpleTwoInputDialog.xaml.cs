using System.Windows;

namespace PointCloudViz_Final
{
    public partial class SimpleTwoInputDialog : Window
    {
        public string Value1 { get; private set; } = "";
        public string Value2 { get; private set; } = "";
        public SimpleTwoInputDialog(string label1, string label2, string default1 = "", string default2 = "")
        {
            InitializeComponent();
            Label1.Text = label1;
            Label2.Text = label2;
            Input1.Text = default1;
            Input2.Text = default2;
        }
        private void Ok_Click(object sender, RoutedEventArgs e) { Value1 = Input1.Text.Trim(); Value2 = Input2.Text.Trim(); DialogResult = true; }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
