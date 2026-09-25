namespace LYBox.Plugin.Shared.Paths;

/// <summary>
/// 插件内部数据目录语义化常量。
/// <para>
/// 各插件自行起子目录名(如 <c>download</c> / <c>message</c> / <c>chats</c> ...)会导致
/// 同类用途在不同插件之间命名漂移。本类统一规定通用语义名,
/// 插件通过 <see cref="IPluginDataDirectoryProvider"/> 的语义化方法获取对应目录。
/// </para>
/// <para>
/// 适用范围:
/// <list type="bullet">
/// <item>通用语义目录(cache / config / downloads / exports / history / logs)。</item>
/// <item>插件若有"专属子目录"需求(命名明确且不通用),继续走 <see cref="IPluginDataDirectoryProvider.GetSubDirectory"/>。</item>
/// </list>
/// </para>
/// </summary>
public static class PluginSubDirectories
{
    /// <summary>通用缓存(BTSou 资源池等)。</summary>
    public const string Cache = "cache";

    /// <summary>插件私有 JSON / 配置目录(binary 路径、第三方账号等)。</summary>
    public const string Config = "config";

    /// <summary>下载产物(视频 / 文件 / 媒体)。</summary>
    public const string Downloads = "downloads";

    /// <summary>导出产物(聊天 / 消息 / 成员列表)。</summary>
    public const string Exports = "exports";

    /// <summary>执行 / 转发历史。</summary>
    public const string History = "history";

    /// <summary>插件临时日志(不是 ZLogger 滚动日志;那是宿主提供)。</summary>
    public const string Logs = "logs";

    /// <summary>抖音子模块专属子目录(仅 Downloader 插件使用)。</summary>
    public const string Douyin = "douyin";

    /// <summary>通用数据目录(无明确语义,留给插件自定义的 db / 中间态文件)。</summary>
    public const string Data = "data";
}