namespace LYBox.Plugin.Shared.Messages;

/// <summary>
/// 插件更新 Setting 中某个 <see cref="Models.SettingType.Action"/> 或
/// <see cref="Models.SettingType.ReadOnly"/> 条目副标题时的通知。
/// <see cref="Key"/> 对应 <see cref="Models.SettingItem.Key"/>，
/// <see cref="Text"/> 为新文本（可为空字符串表示清除）。
/// 宿主 <c>SettingsPageViewModel</c> 监听该消息后同步更新对应 entry 的 Subtitle。
/// </summary>
public sealed record SettingStatusChangedMessage(string Key, string Text);
