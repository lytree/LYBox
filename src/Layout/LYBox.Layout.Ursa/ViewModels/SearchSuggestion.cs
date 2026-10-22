using System.Windows.Input;

namespace LYBox.Layout.Ursa.ViewModels;

/// <summary>
/// 搜索建议项（参考 DevToys AutoSuggestBox 的下拉结果项）：图标 + 名称，点击直达对应页面。
/// </summary>
public sealed class SearchSuggestion
{
    /// <summary>显示名称（已本地化）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>导航 Key。</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>图标资源键（Fluent 图标名）。</summary>
    public string? IconName { get; init; }

    /// <summary>所属分组名，用于关键字匹配。</summary>
    public string? Group { get; init; }

    /// <summary>点击建议项时执行（导航 + 关闭下拉 + 清空关键字）。</summary>
    public ICommand? ActivateCommand { get; set; }
}
