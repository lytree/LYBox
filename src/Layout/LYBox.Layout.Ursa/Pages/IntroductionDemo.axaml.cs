using Avalonia;
using Avalonia.Controls;
using LYBox.Layout.Core.ViewModels;

namespace LYBox.Layout.Ursa.Pages;

public partial class IntroductionDemo : UserControl
{
    /// <summary>工具卡片最小列宽（参考 DevToys GridView 的 ItemMinWidth=330）。</summary>
    private const double ToolCardMinWidth = 330;

    /// <summary>卡片之间的间距，与卡片自身的 16px 外边距一致。</summary>
    private const double ToolCardGap = 16;

    public IntroductionDemo()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 按可用宽度推算网格列数：宽度不足时自动降列，保证卡片不被压扁
    /// （等价于 DevToys GridView 的 ItemMinWidth 行为）。
    /// </summary>
    private void OnToolGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not IntroductionDemoViewModel viewModel || e.NewSize.Width <= 0)
            return;

        var columns = (int)Math.Max(1,
            Math.Floor((e.NewSize.Width + ToolCardGap) / (ToolCardMinWidth + ToolCardGap)));
        viewModel.GridColumns = columns;
    }
}
