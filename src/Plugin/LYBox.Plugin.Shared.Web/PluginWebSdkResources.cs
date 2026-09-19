using System.Reflection;

namespace LYBox.Plugin.Shared.Web;

/// <summary>
/// LYBox Web SDK 浏览器资源访问入口。
/// <para>
/// 嵌入式资源 <c>LYBox.Plugin.Shared.Web.Assets.lybox-plugin-sdk.js</c>、
/// <c>lybox-plugin-theme.css</c> 与 <c>Rpc.Assets.ipc.js</c>（WebView IPC 引导脚本）
/// 通过 <see cref="Assembly"/> 在运行时读取，前两者可经 <see cref="WebHostService"/> 的
/// <c>/sdk/</c> 路径提供给前端页面；后者由 <c>WebViewIpcHost</c> 经
/// <c>IRpcTransport.ExecuteScriptAsync</c> 注入页面。
/// </para>
/// <para>
/// 提供与 windit-toolbox <c>Avalonia.Plugin.Shared.Web.PluginWebSdkResources</c> 一致的契约，
/// 让前端 SDK 引用层（<c>&lt;script type="module" src="/sdk/lybox-plugin-sdk.js"&gt;</c>、
/// <c>&lt;link rel="stylesheet" href="/sdk/lybox-plugin-theme.css"&gt;</c>）与宿主实现解耦，
/// 由宿主经 <c>WebHostService</c> 在 <c>/sdk/</c> 路径下对外提供。
/// </para>
/// </summary>
public static class PluginWebSdkResources
{
    /// <summary>资源 HTTP 路径前缀（与 WebHostService 中注册的路径一致）。</summary>
    public const string RequestPathPrefix = "/sdk/";

    /// <summary>嵌入资源 logical name 命名空间前缀。</summary>
    public const string ResourceNamePrefix = "LYBox.Plugin.Shared.Web.Assets";

    /// <summary>SDK JS 嵌入资源名。</summary>
    public const string SdkScriptResourceName = ResourceNamePrefix + ".lybox-plugin-sdk.js";

    /// <summary>SDK TypeScript 类型声明嵌入资源名。与 <see cref="SdkScriptResourceName"/> 一一对应。</summary>
    public const string SdkTypeDeclarationsResourceName = ResourceNamePrefix + ".lybox-plugin-sdk.d.ts";

    /// <summary>主题 CSS 嵌入资源名。</summary>
    public const string ThemeStylesheetResourceName = ResourceNamePrefix + ".lybox-plugin-theme.css";

    /// <summary>WebView IPC 引导脚本（ipc.js）嵌入资源名，由 WebViewIpcHost 启动时注入页面。</summary>
    public const string IpcBootstrapResourceName = "LYBox.Plugin.Shared.Web.Rpc.Assets.ipc.js";

    /// <summary>当前 SDK 所在程序集（用于读取嵌入式资源）。</summary>
    public static Assembly Assembly => typeof(PluginWebSdkResources).Assembly;

    /// <summary>从嵌入式资源中读取 JS / CSS 内容。</summary>
    public static string ReadResource(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}