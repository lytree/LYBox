namespace LYBox.Plugin.Shared.Models;

public enum SettingType
{
    Text,
    Switch,
    Dropdown,
    Path,
    /// <summary>
    /// 只读状态/标签条目。宿主将其渲染为标题+正文样式，不可编辑、不计入 IsDirty。
    /// </summary>
    ReadOnly,
    /// <summary>
    /// 动作条目（按钮 + 可选只读状态）。宿主在卡片中渲染一个按钮，
    /// 点击时通过 <see cref="LYBox.Plugin.Shared.Messages.SettingActionInvokedMessage"/> 派发；
    /// 插件通过 <c>WeakReferenceMessenger.Default.Register<...></c> 订阅并执行具体逻辑。
    /// </summary>
    Action,
}
