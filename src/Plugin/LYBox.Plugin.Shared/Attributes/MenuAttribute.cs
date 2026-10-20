namespace LYBox.Plugin.Shared.Attributes;

/// <summary>
/// 菜单项特性，用于标记ViewModel并自动生成菜单项
/// </summary>
/// <remarks>
/// 初始化菜单项特性
/// </remarks>
/// <param name="header">菜单项标题</param>
/// <param name="key">菜单项键</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class MenuAttribute(string header, string key, string? parentKey = null) : Attribute
{
    /// <summary>
    /// 菜单项标题
    /// </summary>
    public string Header { get; set; } = header;

    /// <summary>
    /// 菜单项键
    /// </summary>
    public string Key { get; set; } = key;

    /// <summary>
    /// 父菜单项键
    /// </summary>
    public string? ParentKey { get; set; } = parentKey;

    /// <summary>
    /// 菜单项图标名称
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// 菜单项状态
    /// </summary>
    public string? Status { get; set; } = null;

    /// <summary>
    /// 菜单项描述。用于首页工具卡片的次级文案；为空时宿主回退为所属分组名。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 菜单项顺序
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// 菜单分组标识。相邻菜单项的 Group 不同时，宿主会在两者之间插入一条分隔线；
    /// 留空表示不参与分组（不会产生分隔线）。
    /// </summary>
    public string? Group { get; set; }
}
