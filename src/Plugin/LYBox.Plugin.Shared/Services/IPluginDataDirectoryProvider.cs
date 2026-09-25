using LYBox.Plugin.Shared.Paths;

namespace LYBox.Plugin.Shared.Services;

/// <summary>
/// 插件数据目录契约。
/// 宿主为每个插件分配一个独立的可写目录 <c>{HostDataRoot}/{PluginId}/</c>,
/// 用于存放插件的配置、缓存、本地数据库、用户数据等所有持久化内容。
///
/// 设计目标:
/// <list type="bullet">
/// <item>集中:所有插件的可写文件统一在主体的 Data 目录下,便于备份/迁移/卸载清理。</item>
/// <item>隔离:每个插件独立子目录,互不干扰;卸载时直接删除该子目录即可。</item>
/// <item>升级安全:插件升级(覆盖式安装)时,宿主保留该目录,
///       插件可在 <c>RegisterAsync</c> 中按需迁移本地数据库 schema。</item>
/// <item>无依赖:插件通过 DI 解析本接口;<see cref="LYBox.Plugin.Shared.ServiceLocator"/>
///       初始化之前可通过 <see cref="FallbackDataRoot"/> 兜底(仅用于 ServiceLocator 早期调用)。</item>
/// </list>
///
/// 实现依据:docs/Plugin-Upgrade-Evaluation.md(升级场景下的数据保留)。
/// </summary>
public interface IPluginDataDirectoryProvider
{
    /// <summary>
    /// 宿主根数据目录(含 appdata.db 等共享数据)。
    /// 典型值:<c>%LOCALAPPDATA%/LYBox</c>(Windows)/ <c>~/.config/LYBox</c>(Linux)。
    /// </summary>
    string HostDataRoot { get; }

    /// <summary>
    /// 获取指定插件的数据子目录绝对路径,并确保目录存在。
    /// 等价 <c>Path.Combine(HostDataRoot, pluginId)</c> + <c>Directory.CreateDirectory</c>。
    /// </summary>
    /// <param name="pluginId">插件唯一标识(必须与 <c>PluginInfo.PluginId</c> 一致)。</param>
    string GetPluginDataDirectory(string pluginId);

    /// <summary>
    /// 获取指定子目录路径(绝对),并确保目录存在。相当于
    /// <c>Directory.CreateDirectory(Path.Combine(GetPluginDataDirectory(pluginId), subPath))</c>。
    /// </summary>
    string GetSubDirectory(string pluginId, string subPath);

    // —— 语义化便捷方法(走 PluginSubDirectories 常量,插件无需自行拼接子目录名)——

    /// <summary>缓存目录:<c>{HostDataRoot}/{pluginId}/cache/</c>。</summary>
    string GetPluginCacheDirectory(string pluginId)
        => GetSubDirectory(pluginId, PluginSubDirectories.Cache);

    /// <summary>私有配置目录:<c>{HostDataRoot}/{pluginId}/config/</c>。</summary>
    string GetPluginConfigDirectory(string pluginId)
        => GetSubDirectory(pluginId, PluginSubDirectories.Config);

    /// <summary>下载产物目录:<c>{HostDataRoot}/{pluginId}/downloads/</c>。</summary>
    string GetPluginDownloadsDirectory(string pluginId)
        => GetSubDirectory(pluginId, PluginSubDirectories.Downloads);

    /// <summary>导出产物目录:<c>{HostDataRoot}/{pluginId}/exports/</c>。</summary>
    string GetPluginExportsDirectory(string pluginId)
        => GetSubDirectory(pluginId, PluginSubDirectories.Exports);

    /// <summary>历史记录目录:<c>{HostDataRoot}/{pluginId}/history/</c>。</summary>
    string GetPluginHistoryDirectory(string pluginId)
        => GetSubDirectory(pluginId, PluginSubDirectories.History);

    /// <summary>抖音子模块子目录:<c>{HostDataRoot}/{pluginId}/douyin/</c>(仅 Downloader 插件使用)。</summary>
    string GetPluginDouyinDirectory(string pluginId)
        => GetSubDirectory(pluginId, PluginSubDirectories.Douyin);
}