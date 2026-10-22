using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using LYBox.DemoPlugin.ViewModels;

namespace LYBox.DemoPlugin.Views;

/// <summary>演示工具页面。所有演示工具共用此视图，内容由 DataContext（DemoToolViewModel）提供。</summary>
public partial class DemoToolView : UserControl
{
    public DemoToolView()
    {
        InitializeComponent();
    }

    /// <summary>工具条「复制」动作：把输入内容写入系统剪贴板。</summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DemoToolViewModel viewModel) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || string.IsNullOrEmpty(viewModel.Input)) return;

        await clipboard.SetTextAsync(viewModel.Input);
    }
}
