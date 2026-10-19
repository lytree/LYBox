using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LYBox.Layout.Ursa.Controls;

/// <summary>命令栏按钮的标签位置（对齐 WinUI CommandBarDefaultLabelPosition）。</summary>
public enum FluentCommandLabelPosition
{
    /// <summary>图标下方显示文字。</summary>
    Bottom,

    /// <summary>图标右侧显示文字。</summary>
    Right,

    /// <summary>仅图标（紧凑模式）。</summary>
    Collapsed,
}

/// <summary>
/// 命令栏项数据模型（供 <see cref="FluentCommandBar"/> 使用）。
/// </summary>
public partial class FluentCommandBarItem : ObservableObject
{
    public FluentCommandBarItem()
    {
    }

    public FluentCommandBarItem(string? header, string? iconName = null, ICommand? command = null, object? commandParameter = null)
    {
        Header = header;
        IconName = iconName;
        Command = command;
        CommandParameter = commandParameter;
    }

    /// <summary>按钮文字（紧凑模式下可省略）。</summary>
    public string? Header { get; init; }

    /// <summary>Fluent 图标资源键，例如 FluentAdd20Regular（可省略 FluentIcon 前缀）。</summary>
    public string? IconName { get; init; }

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    /// <summary>渲染为切换按钮（ToggleButton）。</summary>
    public bool IsToggle { get; init; }

    /// <summary>渲染为分隔线（此时其余字段忽略）。</summary>
    public bool IsSeparator { get; init; }

    /// <summary>切换按钮的选中态。</summary>
    [ObservableProperty]
    private bool _isChecked;
}
