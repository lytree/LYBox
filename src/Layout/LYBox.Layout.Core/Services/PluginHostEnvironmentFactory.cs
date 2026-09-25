using LYBox.Plugin.Shared.Services;

namespace LYBox.Layout.Core.Services;

/// <summary>
/// 宿主侧 <see cref="IPluginHostEnvironmentFactory"/> 默认实现。
/// 工厂本身无状态；每次 <see cref="Create"/> 返回一个新的 <see cref="PluginHostEnvironment"/>。
/// </summary>
public sealed class PluginHostEnvironmentFactory : IPluginHostEnvironmentFactory
{
    public IPluginHostEnvironment Create(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new PluginHostEnvironment(pluginId);
    }
}