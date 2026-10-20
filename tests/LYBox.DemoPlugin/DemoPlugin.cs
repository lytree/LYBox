using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Attributes;

namespace LYBox.DemoPlugin;

/// <summary>
/// 演示插件入口。元数据（名称 / 版本 / 作者 / PluginId 等）全部由 csproj 注入并由
/// <c>[GenerateMetadata]</c> 源生成器补全实现，入口类无需手写任何元数据属性。
/// </summary>
[GenerateMetadata]
public partial class DemoPlugin : IPluginMetadata
{
}
