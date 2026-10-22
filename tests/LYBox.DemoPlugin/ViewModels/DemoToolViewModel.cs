using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LYBox.DemoPlugin.ViewModels;

/// <summary>
/// 演示工具页的公共基类：标题 + 副标题。
/// 主页卡片与工具页面共用同一份文案，便于目检两处渲染是否一致。
/// </summary>
public abstract partial class DemoToolViewModel : ObservableObject
{
    /// <summary>工具名称（页面大标题 / 首页卡片标题）。</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>工具说明（页面副标题）。</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>工具页输入内容（演示工具条的复制 / 重置动作）。</summary>
    [ObservableProperty]
    private string _input = string.Empty;

    /// <summary>重置：清空输入内容。</summary>
    [RelayCommand]
    private void Reset() => Input = string.Empty;
}
