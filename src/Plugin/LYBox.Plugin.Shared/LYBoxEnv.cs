namespace LYBox.Plugin.Shared;

/// <summary>
/// LYBox 全局环境变量门面。
/// <para>
/// 集中管理所有 <c>LYBOX_*</c>(及个别跨生态相关)环境变量:
/// <list type="bullet">
/// <item>Key 常量化 —— 调用方不再用裸字符串,避免拼写漂移。</item>
/// <item>读取 / 写入集中 —— 便于后续接入日志埋点、配置中心、跨平台差异。</item>
/// <item>解析包装 —— 端口、布尔等带类型安全的便捷方法,失败可回退。</item>
/// </list>
/// </para>
/// <para>
/// 约定:
/// <list type="bullet">
/// <item>所有方法都对 <c>null</c> / 空串做静默处理(返回 <c>null</c> 或默认值),不抛异常。</item>
/// <item>不在此处缓存读取结果:每次调用读一次进程级环境变量,
///       以便宿主 / 插件启动期动态注入 <see cref="SetWebPort"/> 等值能立即生效。</item>
/// </list>
/// </para>
/// </summary>
public static class LYBoxEnv
{
    /// <summary>数据根目录覆盖(便携 / CI 场景)。详见 [PluginDataDirectoryProvider]。</summary>
    public const string DataRootKey = "LYBOX_DATA_ROOT";

    /// <summary>宿主 WebHostService 监听端口(开发期固定)。</summary>
    public const string WebPortKey = "LYBOX_WEB_PORT";

    /// <summary>Vite 工程目录覆盖(--web-vite 启动托管时使用)。</summary>
    public const string ViteDirKey = "LYBOX_VITE_DIR";

    /// <summary>开发来源白名单(WebView CORS / Origin 校验用,分号/逗号分隔的绝对 URI)。</summary>
    public const string DevOriginsKey = "LYBOX_DEV_ORIGINS";

    /// <summary>宿主版本覆盖(CI/脚本注入)。详见根 Directory.Build.props。</summary>
    public const string HostVersionKey = "LYBOX_HOST_VERSION";

    /// <summary>SDK NuGet 本地 feed 路径(插件仓库还原使用)。</summary>
    public const string SdkFeedKey = "LYBOX_SDK_FEED";

    /// <summary>插件开发期源码目录前缀:完整 Key = <c>LYBOX_PLUGIN_SRC_{PluginId}</c>(连字符转下划线)。</summary>
    public const string PluginSrcPrefix = "LYBOX_PLUGIN_SRC_";

    /// <summary>Avalonia 插件扩展搜索路径(框架级约定,这里只再导出常量以便集中引用)。</summary>
    public const string ExtraPluginsKey = "AVALONIA_EXTRA_PLUGINS_PATH";

    /// <summary>WebView2 远程调试参数(由宿主 --web-devtools 写入)。</summary>
    public const string WebView2DebugArgsKey = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";

    /// <summary>读取进程级环境变量;空串视作未设置返回 <c>null</c>。</summary>
    public static string? Get(string key)
        => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)) ? null : Environment.GetEnvironmentVariable(key);

    /// <summary>写入进程级环境变量。<paramref name="value"/> 为 <c>null</c> 时表示清除。</summary>
    public static void Set(string key, string? value)
    {
        if (value is null)
            Environment.SetEnvironmentVariable(key, null);
        else
            Environment.SetEnvironmentVariable(key, value);
    }

    /// <summary>解析 <see cref="WebPortKey"/> 为整型。无效 / 未设置返回 0(随机端口)。</summary>
    public static int GetWebPortOrZero()
    {
        var raw = Get(WebPortKey);
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        return int.TryParse(raw, out var port) && port is > 0 and <= 65535 ? port : 0;
    }

    /// <summary>便捷写入:把 <paramref name="port"/> 写入 <see cref="WebPortKey"/>。</summary>
    public static void SetWebPort(int port) => Set(WebPortKey, port.ToString());

    /// <summary>
    /// 解析 <see cref="DevOriginsKey"/> 文本(分号 / 逗号分隔)为字符串数组;
    /// 自动 <see cref="string.Trim"/> 与剔除空项。
    /// </summary>
    public static string[] GetDevOrigins()
    {
        var raw = Get(DevOriginsKey);
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// 读取 <c>LYBOX_PLUGIN_SRC_{PluginId}</c>:Key 中连字符替换为下划线以兼容 shell / Windows 环境变量命名。
    /// </summary>
    public static string? GetPluginSrcDir(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var key = PluginSrcPrefix + pluginId.Replace('-', '_');
        return Get(key);
    }

    /// <summary>写入 <c>LYBOX_PLUGIN_SRC_{PluginId}</c>。</summary>
    public static void SetPluginSrcDir(string pluginId, string? directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var key = PluginSrcPrefix + pluginId.Replace('-', '_');
        Set(key, directory);
    }
}