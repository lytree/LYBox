using Avalonia.Controls;

namespace LYBox.DemoPlugin.Views;

/// <summary>演示工具页面。所有演示工具共用此视图，内容由 DataContext（DemoToolViewModel）提供。</summary>
public partial class DemoToolView : UserControl
{
    public DemoToolView()
    {
        InitializeComponent();
    }
}
