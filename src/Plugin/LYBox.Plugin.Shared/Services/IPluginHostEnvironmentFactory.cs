namespace LYBox.Plugin.Shared.Services;

/// <summary>
/// 宿主侧 <see cref="IPluginHostEnvironment"/> 工厂。
/// 每个插件按 <see cref="PluginInfo.PluginId"/> 创建独立的实例（每个实例自带独立的日志通道）。
/// </summary>
public interface IPluginHostEnvironmentFactory
{
    /// <summary>为指定插件创建一个独立的 <see cref="IPluginHostEnvironment"/>。</summary>
    IPluginHostEnvironment Create(string pluginId);
}