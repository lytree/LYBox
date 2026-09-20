namespace LYBox.Plugin.Shared.Models;

public class SettingDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GroupName { get; set; } = "General";
    public int GroupOrder { get; set; }
    public int ItemOrder { get; set; }
    public SettingType SettingType { get; set; }
    public string? DefaultValue { get; set; }
    public List<string>? Options { get; set; }

    public string? PlaceholderText { get; set; }

    public string? PluginId { get; set; }

    public bool IsFolder { get; set; }

    public static SettingDefinition Text(string key, string displayName, string? description = null,string placeholder = "",
        string group = "General", int groupOrder = 0, int itemOrder = 0, string? defaultValue = null, string? pluginId = null)
    {
        return new SettingDefinition
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            GroupName = group,
            GroupOrder = groupOrder,
            ItemOrder = itemOrder,
            SettingType = SettingType.Text,
            DefaultValue = defaultValue,
            PlaceholderText = placeholder,
            PluginId = pluginId
        };
    }

    public static SettingDefinition Switch(string key, string displayName, string? description = null,
        string group = "General", int groupOrder = 0, int itemOrder = 0, bool defaultValue = false, string? pluginId = null)
    {
        return new SettingDefinition
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            GroupName = group,
            GroupOrder = groupOrder,
            ItemOrder = itemOrder,
            SettingType = SettingType.Switch,
            DefaultValue = defaultValue ? "true" : "false",
            PluginId = pluginId
        };
    }

    public static SettingDefinition Dropdown(string key, string displayName, List<string> options, string? description = null,
        string group = "General", int groupOrder = 0, int itemOrder = 0, string? defaultValue = null, string? pluginId = null)
    {
        return new SettingDefinition
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            GroupName = group,
            GroupOrder = groupOrder,
            ItemOrder = itemOrder,
            SettingType = SettingType.Dropdown,
            Options = options,
            DefaultValue = defaultValue,
            PluginId = pluginId
        };
    }

    public static SettingDefinition Path(string key, string displayName, string? description = null,
        string group = "General", int groupOrder = 0, int itemOrder = 0, string? defaultValue = null, string? pluginId = null,
        bool isFolder = false)
    {
        return new SettingDefinition
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            GroupName = group,
            GroupOrder = groupOrder,
            ItemOrder = itemOrder,
            SettingType = SettingType.Path,
            DefaultValue = defaultValue,
            PluginId = pluginId,
            IsFolder = isFolder
        };
    }

    /// <summary>
    /// 只读条目（用于显示状态/提示文本，不参与保存与脏标记）。
    /// </summary>
    public static SettingDefinition ReadOnly(string key, string displayName, string? description = null,
        string group = "General", int groupOrder = 0, int itemOrder = 0, string? defaultValue = null, string? pluginId = null)
    {
        return new SettingDefinition
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            GroupName = group,
            GroupOrder = groupOrder,
            ItemOrder = itemOrder,
            SettingType = SettingType.ReadOnly,
            DefaultValue = defaultValue,
            PluginId = pluginId
        };
    }

    /// <summary>
    /// 动作条目（按钮 + 可选状态文本）。点击按钮时宿主派发
    /// <see cref="LYBox.Plugin.Shared.Messages.SettingActionInvokedMessage"/>，
    /// 插件订阅后执行具体逻辑（如初始化、打开登录弹框等）。
    /// <paramref name="defaultValue"/> 可作为按钮旁的次要文本（例如状态文案），
    /// 宿主将其作为 <c>Action.Subtitle</c> 渲染。
    /// </summary>
    public static SettingDefinition Action(string key, string displayName, string? description = null,
        string group = "General", int groupOrder = 0, int itemOrder = 0, string? defaultValue = null, string? pluginId = null)
    {
        return new SettingDefinition
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            GroupName = group,
            GroupOrder = groupOrder,
            ItemOrder = itemOrder,
            SettingType = SettingType.Action,
            DefaultValue = defaultValue,
            PluginId = pluginId
        };
    }
}
