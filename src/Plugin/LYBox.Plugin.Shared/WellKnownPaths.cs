namespace LYBox.Plugin.Shared;

/// <summary>
/// LYBox 路径布局常量集中化(单一真相源)。
/// <para>
/// 所有"约定的子目录 / 文件名"统一在此声明,宿主与插件均引用同一组常量,
/// 避免在多处重复拼装 <c>Path.Combine(...,"logs")</c> 等字面量导致漂移。
/// </para>
/// <para>
/// 适用范围:
/// <list type="bullet">
/// <item><b>运行时落盘</b>:宿主主日志、插件隔离日志、SQLite 数据库、设置 JSON。</item>
/// <item><b>约定子目录</b>:downloads / exports / history / cache / config / logs(插件内)。</item>
/// </list>
/// </para>
/// <para>
/// 注意:
/// <list type="bullet">
/// <item>这里仅定义"名字"(<c>logs</c> / <c>cache</c> 等),具体路径拼接逻辑由
///       <see cref="LYBox.Plugin.Shared.Services.IPluginHostEnvironment"/> 与
///       <see cref="LYBox.Plugin.Shared.Services.IPluginDataDirectoryProvider"/> 负责。</item>
/// <item>不要在这里写绝对路径前缀(平台相关),仅放子目录 / 文件名常量。</item>
/// </list>
/// </para>
/// </summary>
public static class WellKnownPaths
{
    /// <summary>宿主主日志子目录(根于启动器目录)。如 <c>{AppBase}/logs/</c>。</summary>
    public const string LogsSubDir = "logs";

    /// <summary>插件隔离日志根目录: <c>{LogsSubDir}/plugins/</c>。</summary>
    public const string PluginsSubDir = "plugins";

    /// <summary>插件私有配置 JSON 目录(插件级 ISettingsService 兜底 / 兼容场景)。</summary>
    public const string ConfigSubDir = "config";

    /// <summary>通用缓存子目录(如 BTSou 资源池)。</summary>
    public const string CacheSubDir = "cache";

    /// <summary>下载产物子目录。</summary>
    public const string DownloadsSubDir = "downloads";

    /// <summary>导出产物子目录(聊天 / 消息 / 成员列表等)。</summary>
    public const string ExportsSubDir = "exports";

    /// <summary>执行历史 / 转发记录子目录。</summary>
    public const string HistorySubDir = "history";

    /// <summary>宿主共享数据库文件名(EF Core SQLite,位于数据根目录)。</summary>
    public const string HostDatabaseFileName = "appdata.db";

    /// <summary>插件私有 JSON 设置文件名(默认与 ISettingsService 不可用时回退路径)。</summary>
    public const string PluginSettingsFileName = "settings.json";

    /// <summary>滚动日志文件名前缀(配合日期与序号: <c>app-yyyy-MM-dd_NNN.log</c>)。</summary>
    public const string RollingLogFilePrefix = "app";

    /// <summary>滚动日志文件扩展名。</summary>
    public const string LogFileExtension = ".log";

    /// <summary>
    /// 拼接滚动日志完整文件名:
    /// <c>{RollingLogFilePrefix}-{yyyy-MM-dd}_{seq:000}{LogFileExtension}</c>。
    /// 接受 ZLogger 传入的 <see cref="DateTimeOffset"/>(已是 UTC)。
    /// </summary>
    public static string FormatRollingLogFileName(DateTimeOffset utcTimestamp, int sequence)
        => $"{RollingLogFilePrefix}-{utcTimestamp.UtcDateTime:yyyy-MM-dd}_{sequence:000}{LogFileExtension}";
}