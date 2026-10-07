using System.Windows;
using System.Windows.Controls;

namespace PointCloudViz.App.Views;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        InitializeComponent();
        Fill(MouseList,
        [
            ("左键拖动", "旋转视角（绕旋转中心）"),
            ("右键 / 中键拖动", "平移"),
            ("滚轮", "朝光标方向缩放"),
            ("左键双击", "把双击的点设为旋转中心"),
            ("左键单击", "量测工具下拾取点"),
            ("右键单击", "完成折线 / 面积量测"),
        ]);
        Fill(KeyboardList,
        [
            ("W / A / S / D", "水平前后左右移动"),
            ("Q / E", "上升 / 下降"),
            ("Shift + 移动键", "4 倍速移动"),
            ("+ / -", "缩放"),
            ("R 或 F", "适应窗口"),
            ("1 / 2 / 3 / 4 / 5 / 6 / 0", "俯视 / 前视 / 左视 / 右视 / 后视 / 仰视 / 轴测"),
            ("Enter", "完成当前量测"),
            ("Backspace", "撤回上一个量测顶点"),
            ("Delete", "删除选中的量测"),
            ("Esc", "取消当前量测；再按一次回到浏览模式"),
        ]);
        Fill(GlobalList,
        [
            ("Ctrl + O", "打开点云或项目"),
            ("Ctrl + S", "保存项目"),
            ("Ctrl + Shift + S", "导出点云"),
            ("Ctrl + Z / Ctrl + Y", "撤销 / 重做（滤波与量测都可撤销）"),
            ("F12", "保存截图"),
            ("F1", "显示本窗口"),
        ]);
    }

    private void Fill(ItemsControl list, (string Key, string Description)[] items)
    {
        foreach (var (key, description) in items)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var keyBorder = new Border { Style = (Style)FindResource("Key"), Child = new TextBlock { Text = key } };
            var text = new TextBlock { Text = description, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            row.Children.Add(keyBorder);
            row.Children.Add(text);
            list.Items.Add(row);
        }
    }
}
