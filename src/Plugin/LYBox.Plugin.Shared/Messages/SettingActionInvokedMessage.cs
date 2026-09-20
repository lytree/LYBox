namespace LYBox.Plugin.Shared.Messages;

/// <summary>
/// 宿主在用户点击 <see cref="LYBox.Plugin.Shared.Models.SettingType.Action"/> 设置项的按钮时派发。
/// 插件侧可通过 <c>WeakReferenceMessenger.Default.Register<TdlPlugin, SettingActionInvokedMessage></c>
/// 监听并执行对应动作。<see cref="ActionId"/> 与 <see cref="LYBox.Plugin.Shared.Models.SettingItem.Key"/> 一致。
/// </summary>
public sealed record SettingActionInvokedMessage(string ActionId);
