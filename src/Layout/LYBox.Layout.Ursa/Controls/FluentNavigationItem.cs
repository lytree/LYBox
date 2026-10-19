using CommunityToolkit.Mvvm.ComponentModel;

namespace LYBox.Layout.Ursa.Controls;

/// <summary>
/// 顶部导航项数据模型（供 <see cref="FluentTopNavigation"/> 使用）。
/// </summary>
public partial class FluentNavigationItem : ObservableObject
{
    public FluentNavigationItem()
    {
    }

    public FluentNavigationItem(string header, string? key = null, string? iconName = null)
    {
        Header = header;
        Key = key ?? header;
        IconName = iconName;
    }

    /// <summary>显示文本。</summary>
    public string? Header { get; init; }

    /// <summary>导航键（未指定时等于 Header）。</summary>
    public string? Key { get; init; }

    /// <summary>Fluent 图标资源键，例如 FluentHome20Regular（可省略 FluentIcon 前缀）。</summary>
    public string? IconName { get; init; }

    /// <summary>是否为当前选中项，由控件维护。</summary>
    [ObservableProperty]
    private bool _isSelected;
}
