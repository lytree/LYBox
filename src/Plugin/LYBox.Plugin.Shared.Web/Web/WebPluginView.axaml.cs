using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using LYBox.Plugin.Shared.Rpc;
using LYBox.Plugin.Shared.Services;
using Microsoft.Extensions.Logging;

namespace LYBox.Plugin.Shared.Web;

/// <summary>
/// 封装 <see cref="WebView"/> 的 UserControl，提供 Web 插件的页面承载 + IPC 集成 + 开发展示层。
/// </summary>
/// <remarks>
/// <para>
/// 使用方式：在插件页面 XAML 中放置 <c>&lt;web:WebPluginView PluginId="{Binding PluginId}" /&gt;</c>。
/// 控件附加到视觉树后自动完成：
/// <list type="number">
/// <item>从 <see cref="ServiceLocator"/> 获取 <see cref="WebHostService"/> 单例。</item>
/// <item>构造 <see cref="WebViewIpcTransport"/>（桥接 WebView 原生 IPC）+ <see cref="WebViewIpcHost"/>（注入 SSE 推送器）。</item>
/// <item>导航到 <c>{BaseUrl}/{PluginId}/index.html</c>。</item>
/// <item><see cref="WebView.NavigationCompleted"/> 后注入 ipc.js → 调用 <c>startSse(pluginId)</c> → 注册 [RpcCommand] 绑定 → 注入绑定清单。</item>
/// </list>
/// </para>
/// <para>
/// <see cref="RpcHost"/> 属性在初始化完成后暴露创建的 <see cref="WebViewIpcHost"/>，
/// 供外部（如插件 ViewModel）调用 <see cref="IRpcHost.EmitEventAsync"/> 主动推送事件。
/// </para>
/// <para>
/// 展示层（自公司版 PluginWebViewPage 移植）：
/// 设置 <see cref="ShowDevelopmentToolbar"/> 为 <c>true</c> 后显示开发工具栏
/// （Back/Forward/Refresh + Route/Status），并对导航执行授权域校验：
/// 越权导航会被拦截并在开发模式下展示错误页（可 Retry）。
/// </para>
/// </remarks>
public partial class WebPluginView : UserControl
{
    private static readonly Uri ErrorPageBaseUri = new("about:blank");

    private WebViewIpcTransport? _transport;
    private WebViewIpcHost? _host;
    private WebHostService? _webHost;
    private NativeWebView? _webView;
    private string? _webSession;
    private bool _initialized;
    private bool _isAttached;

    // ---- 展示层状态（移植自 PluginWebViewPage） ----
    private Border? _devToolbar;
    private Button? _backButton;
    private Button? _forwardButton;
    private TextBlock? _modeText;
    private TextBlock? _routeText;
    private TextBlock? _statusText;
    private Uri? _authorizedBaseUri;
    private Uri? _targetUri;
    private string _routeBasePath = string.Empty;
    private bool _isErrorPageActive;
    private ILogger? _consoleLogger;

    public static readonly StyledProperty<string?> PluginIdProperty =
        AvaloniaProperty.Register<WebPluginView, string?>(nameof(PluginId));

    static WebPluginView()
    {
        PluginIdProperty.Changed.AddClassHandler<WebPluginView>((v, e) => v.OnPluginIdChanged(e));
    }

    public WebPluginView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 是否显示开发工具栏（Back/Forward/Refresh + Route/Status）并启用导航信任校验与开发错误页。
    /// 默认关闭；调试 Web 插件页面时可全局开启。
    /// </summary>
    public static bool ShowDevelopmentToolbar { get; set; }

    /// <summary>
    /// 插件 ID（Kestrel 路由前缀 + SSE 通道 key）。
    /// 必须在控件附加到视觉树前或后通过 XAML 绑定设置。
    /// </summary>
    public string? PluginId
    {
        get => GetValue(PluginIdProperty);
        set => SetValue(PluginIdProperty, value);
    }

    /// <summary>
    /// 初始化完成后创建的 RPC 主机。供外部调用 <see cref="IRpcHost.EmitEventAsync"/> 等方法。
    /// </summary>
    public IRpcHost? RpcHost => _host;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        base.OnAttachedToVisualTree(e);
        _ = TryInitialize();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        if (_webView is not null)
        {
            _webView.NavigationStarted -= OnNavigationStarted;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.WebMessageReceived -= OnDevWebMessageReceived;
            _webView.EnvironmentRequested -= OnWebViewEnvironmentRequested;

            // 摘除控件：下次挂树时重新走一遍注册门控 + 创建流程（懒加载状态不跨挂载周期残留）
            if (this.FindControl<Border>("PART_WebHost") is { } host
                && ReferenceEquals(host.Child, _webView))
            {
                try
                {
                    host.Child = null;
                }
                catch
                {
                    // 卸载 WebView2 适配器失败不应影响控件脱离视觉树
                }
            }
        }

        _webHost?.RevokeSession(_webSession);
        _webSession = null;
        _host?.Dispose();
        _host = null;
        _transport?.Detach();
        _transport = null;
        _webHost = null;
        _webView = null;
        _initialized = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPluginIdChanged(AvaloniaPropertyChangedEventArgs e)
    {
        // 绑定可能在控件附加后生效：此时若已附加且未初始化，触发初始化
        if (_isAttached && !_initialized && e.NewValue is string pid && !string.IsNullOrEmpty(pid))
        {
            _ = TryInitialize();
        }
    }

    private async Task TryInitialize()
    {
        if (_initialized) return;
        var pluginId = PluginId;
        if (string.IsNullOrEmpty(pluginId)) return;
        _initialized = true;

        // Linux WPE WebKit 后端为实验性（EGL 支持未完成，issue #14 open）
        if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Debug.WriteLine(
                "[WebPluginView] 警告：Linux WPE WebKit 后端为实验性，嵌入式 WebView 可能不稳定。" +
                "如遇渲染问题，可降级为 NativeWebDialog（WebKitGTK 独立窗口）。");
        }

        // 1. 获取 WebHostService 单例
        if (!ServiceLocator.TryGetService<WebHostService>(out var webHost) || webHost is null)
            return;
        _webHost = webHost;

        // 2. 懒加载门控必须在任何 WebView 创建之前完成：
        //    NativeWebView 只要挂到视觉树，就会在 OnAttached 里创建 WebView2 环境
        //    （WebViewAdapter.CreateFactory → WebView2HwndAdapter.CreateBuilder），与 IsVisible 无关。
        //    未主动注册 Web 资源的插件因此完全不触碰 WebView2 运行时，也就不会在 UI 线程上抛初始化异常。
        if (!webHost.IsRunning || !webHost.IsRegistered(pluginId))
        {
            ShowPlaceholder($"插件 {pluginId} 未主动注册 Web 资源，Web 界面未启用");
            return;
        }

        // 3. 此时才创建并挂载 NativeWebView（WebView2 环境在此初始化；失败降级为占位）
        if (!TryAttachWebView(pluginId, out var webView))
            return;
        _webView = webView;

        // 4. 构造 IPC 传输层 + 主机（注入 SSE pusher + pluginId + webHost 以启用 SSE 推送 + HTTP RPC 桥）
        _transport = new WebViewIpcTransport(webView);
        _host = new WebViewIpcHost(_transport, webHost.EventPusher, pluginId, webHost);

        // 5. 注册插件的 [RpcCommand] 绑定（需在 InjectBindingsAsync 前完成）
        RegisterPluginBindings(pluginId);

        // 5b. 注册系统级命令（文件选择器 + 对话框），所有 web 插件共享
        SystemCommands.Register(_host, () => TopLevel.GetTopLevel(this));

        var pluginSegment = Uri.EscapeDataString(pluginId);
        var pluginBaseUri = new Uri(
            $"{webHost.BaseUrl.TrimEnd('/')}/{pluginSegment}/",
            UriKind.Absolute);
        _targetUri = new Uri(pluginBaseUri, "index.html");

        // 5c. 展示层：开发工具栏 + 导航信任校验 + 开发错误页
        InitializeDevTools(webView, pluginBaseUri);

        // 6. 订阅 NavigationCompleted 注入引导脚本
        webView.NavigationCompleted += OnNavigationCompleted;

        // 7. 导航到插件入口页
        webView.Source = _targetUri;
    }

    /// <summary>
    /// 预检 WebView2 运行时 → 创建 <see cref="NativeWebView"/> → 挂入 <c>PART_WebHost</c>。
    /// </summary>
    /// <remarks>
    /// 任一步骤失败都回落为占位提示，不把异常抛给 UI 线程：
    /// 该异常在 DEBUG 下会被全局处理器上抛（<c>e.Handled = false</c>）从而终止整个宿主，
    /// 而它本质上是环境问题（UDF 无写权限 0x80070005、运行时缺失、安全软件拦截），页面降级即可。
    /// </remarks>
    /// <param name="pluginId">插件 ID（仅用于日志与占位文案）。</param>
    /// <param name="webView">创建成功时返回挂载后的控件，失败时为 <c>null</c>。</param>
    private bool TryAttachWebView(string pluginId, out NativeWebView webView)
    {
        webView = null!;

        var host = this.FindControl<Border>("PART_WebHost");
        if (host is null)
            return false;

        if (!IsWebView2RuntimeAvailable(out var unavailableReason))
        {
            LogWebViewWarning("WebView2 运行时不可用（{Reason}），插件 {PluginId} 的 Web 界面降级为占位",
                unavailableReason, pluginId);
            ShowPlaceholder($"WebView2 运行时不可用：{unavailableReason}");
            return false;
        }

        var view = new NativeWebView();

        // UDF 必须在控件挂树（即 WebView2 环境创建）之前指定：
        // 默认目录可能落在只读位置（exe 同级、%TEMP% 受限等），是 0x80070005 的常见来源。
        view.EnvironmentRequested += OnWebViewEnvironmentRequested;

        try
        {
            host.Child = view;
        }
        catch (Exception ex)
        {
            view.EnvironmentRequested -= OnWebViewEnvironmentRequested;
            LogWebViewWarning("插件 {PluginId} 的 WebView2 初始化失败：{Message}", pluginId, ex.Message);
            ShowPlaceholder($"WebView2 初始化失败：{ex.Message}");
            return false;
        }

        host.IsVisible = true;
        if (this.FindControl<Border>("PART_Placeholder") is { } placeholder)
            placeholder.IsVisible = false;

        webView = view;
        return true;
    }

    /// <summary>
    /// WebView2 环境参数回调（仅 Windows 后端触发；其他平台传入各自的参数类型，此处不干预）。
    /// 显式钦定每用户可写的用户数据目录（UDF），避免默认目录无写权限导致的
    /// <c>0x80070005 (E_ACCESSDENIED)</c>。
    /// </summary>
    private static void OnWebViewEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (e is WindowsWebView2EnvironmentRequestedEventArgs win)
            win.UserDataFolder = ResolveUserDataFolder();
    }

    /// <summary>
    /// 解析 WebView2 用户数据目录：优先 <c>%LOCALAPPDATA%\LYBox\WebView2</c>，
    /// 创建失败时回落 <c>%TEMP%\LYBox-WebView2</c>（同样确保目录已存在）。
    /// </summary>
    private static string ResolveUserDataFolder()
    {
        foreach (var root in new[]
                 {
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "LYBox",
                         "WebView2"),
                     Path.Combine(Path.GetTempPath(), "LYBox-WebView2"),
                 })
        {
            try
            {
                if (string.IsNullOrEmpty(root)) continue;
                Directory.CreateDirectory(root);
                return root;
            }
            catch
            {
                // 该候选目录不可用，尝试下一个
            }
        }

        // 两个候选都不可用时返回空串，交回 WebView2 自身默认策略，由创建失败路径兜底降级
        return string.Empty;
    }

    /// <summary>
    /// 预检 WebView2 适配器是否已安装。仅当能明确判定“未安装”时返回 <c>false</c>；
    /// 预检本身异常（无法判定）一律按可用处理，真正的兜底由创建失败路径承担。
    /// </summary>
    private static bool IsWebView2RuntimeAvailable(out string reason)
    {
        reason = string.Empty;
        if (!OperatingSystem.IsWindows())
            return true;

        try
        {
            var info = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebView2);
            if (info.IsInstalled)
                return true;

            reason = info.UnavailableReason ?? "未安装 Microsoft Edge WebView2 Runtime";
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>将插件未主动注册 Web 服务（或 WebView 不可用）的状态呈现为占位面板，隐藏 WebView 宿主。</summary>
    private void ShowPlaceholder(string message)
    {
        if (_webView is not null)
            _webView.IsVisible = false;
        if (this.FindControl<Border>("PART_WebHost") is { } host)
            host.IsVisible = false;
        var placeholder = this.FindControl<Border>("PART_Placeholder");
        if (placeholder is not null)
            placeholder.IsVisible = true;
        var msg = this.FindControl<TextBlock>("PART_PlaceholderMessage");
        if (msg is not null)
            msg.Text = message;
    }

    /// <summary>WebView 相关的降级告警（宿主未配置 LoggerFactory 时静默）。</summary>
    private void LogWebViewWarning(string template, params object?[] args)
        => (_consoleLogger ??= CreateConsoleLogger())?.LogWarning(template, args);

    private void RegisterPluginBindings(string pluginId)
    {
        if (_host is null) return;
        if (!ServiceLocator.TryGetService<IPluginLoader>(out var loader) || loader is null) return;

        if (loader.GetGeneratedModule(pluginId) is not IGeneratedPluginWebModule module)
            return;

        WebPluginBindings.Register(_host, module, ServiceLocator.GetServiceProvider());
    }

    // ==================== 展示层（移植自 PluginWebViewPage） ====================

    private void InitializeDevTools(NativeWebView webView, Uri pluginBaseUri)
    {
        _authorizedBaseUri = pluginBaseUri;
        _routeBasePath = pluginBaseUri.AbsolutePath.TrimEnd('/');

        if (ShowDevelopmentToolbar)
        {
            _devToolbar = this.FindControl<Border>("PART_DevToolbar");
            _backButton = this.FindControl<Button>("PART_BackButton");
            _forwardButton = this.FindControl<Button>("PART_ForwardButton");
            _modeText = this.FindControl<TextBlock>("PART_ModeText");
            _routeText = this.FindControl<TextBlock>("PART_RouteText");
            _statusText = this.FindControl<TextBlock>("PART_StatusText");

            if (_devToolbar is not null)
                _devToolbar.IsVisible = true;
            if (_modeText is not null)
                _modeText.Text = "DEV";
            if (_statusText is not null)
                _statusText.Text = "Idle";

            if (_routeText is not null && _targetUri is not null)
                _routeText.Text = PluginWebViewDevTools.GetRouteText(_targetUri, _routeBasePath);

#if DEBUG
            // 调试面板按钮仅在 Debug 构建显示（Release 隐藏，避免给终端用户暴露开发者入口）
            // 端点本身在 Release 也可用，外部脚本/测试可直接调用。
            if (this.FindControl<Button>("PART_DebugPanelButton") is { } debugButton)
                debugButton.IsVisible = true;
#endif
        }

        // 导航信任校验在开发模式下启用；生产模式同样拦截越权导航但直接取消（不显示错误页）
        webView.NavigationStarted += OnNavigationStarted;

        // 错误页 Retry 消息（JS invokeCSharpAction）与 Rpc 传输共用 WebMessageReceived，多订阅者互不影响
        webView.WebMessageReceived += OnDevWebMessageReceived;
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        _host?.ResetDocument();
        _webHost?.RevokeSession(_webSession);
        _webSession = null;

        var request = e.Request;
        if (request is null)
        {
            e.Cancel = true;
            return;
        }
        if (_isErrorPageActive)
        {
            if (!request.Equals(ErrorPageBaseUri))
                e.Cancel = true;
            return;
        }

        if (_authorizedBaseUri is null)
            return;

        if (!PluginWebViewDevTools.IsAllowedNavigation(request, _authorizedBaseUri))
        {
            e.Cancel = true;
            HandleNavigationFailure(request, "Navigation URL is outside this plugin's authorized namespace.");
            return;
        }

        if (_devToolbar is not null && _statusText is not null)
        {
            _statusText.Text = "Loading";
            if (_routeText is not null)
                _routeText.Text = PluginWebViewDevTools.GetRouteText(request, _routeBasePath);
        }
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        try
        {
            UpdateDevStatus(args.IsSuccess ? "Loaded" : "Failed", args);
            if (!args.IsSuccess || args.Request is null)
            {
                HandleNavigationFailure(args.Request ?? _targetUri ?? ErrorPageBaseUri, "Navigation failed.");
                return;
            }

            var pluginId = PluginId;
            if (_host is null || _transport is null || _webHost is null || string.IsNullOrEmpty(pluginId))
                return;
            if (_authorizedBaseUri is null || !PluginWebViewDevTools.IsAllowedNavigation(args.Request, _authorizedBaseUri))
                return;

            _webHost.RevokeSession(_webSession);
            _webSession = _webHost.CreateSession(pluginId);

            await _host.InitializeAsync().ConfigureAwait(false);
            var pidJson = JsonSerializer.Serialize(pluginId);
            var sessionJson = JsonSerializer.Serialize(_webSession);
            await _transport.ExecuteScriptAsync(
                $"window.__lybox && window.__lybox.configureRuntime({pidJson},{sessionJson});" +
                $"window.__lybox && window.__lybox.startSse({pidJson},{sessionJson});").ConfigureAwait(false);
            await _host.InjectBindingsAsync().ConfigureAwait(false);
        }
        catch
        {
            // 页面已销毁、导航已切换或 WebView 未就绪，忽略旧文档回调
        }
    }

    private void OnDevWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Body))
            return;

        // console 桥：ipc.js 的 'L' 信封 → 宿主日志（前端 console 与 C# 日志同流，便于排障）
        if (WebConsoleBridge.TryParse(e.Body) is { } console)
        {
            _consoleLogger ??= CreateConsoleLogger();
            _consoleLogger?.Log(
                WebConsoleBridge.ToLogLevel(console.Level),
                "[Web:{PluginId}] {Message}",
                PluginId ?? "unknown",
                console.Message);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(e.Body);
            if (doc.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == PluginWebViewDevTools.RetryMessageKind)
            {
                RetryTarget();
            }
        }
        catch (JsonException)
        {
            // 非重试消息（如 Rpc 信封），交由 Rpc 传输层处理
        }
    }

    /// <summary>懒创建 console 桥日志器（分类 LYBox.Web.Console）。宿主未配置 LoggerFactory 时返回 null。</summary>
    private ILogger? CreateConsoleLogger()
        => ServiceLocator.TryGetService<ILoggerFactory>(out var factory) && factory is not null
            ? factory.CreateLogger("LYBox.Web.Console")
            : null;

    private void UpdateDevStatus(string status, WebViewNavigationCompletedEventArgs args)
    {
        if (_statusText is null)
            return;

        _statusText.Text = status;
        if (_routeText is not null && args.Request is not null)
            _routeText.Text = PluginWebViewDevTools.GetRouteText(args.Request, _routeBasePath);

        if (_backButton is not null && _webView is not null)
            _backButton.IsEnabled = _webView.CanGoBack;
        if (_forwardButton is not null && _webView is not null)
            _forwardButton.IsEnabled = _webView.CanGoForward;
    }

    private void HandleNavigationFailure(Uri failedUri, string reason)
    {
        if (!ShowDevelopmentToolbar)
            return;

        _isErrorPageActive = true;
        if (_statusText is not null)
            _statusText.Text = "Failed";
        if (_webView is null)
            return;

        var html = PluginWebViewDevTools.CreateDevelopmentErrorHtml(
            PluginId ?? "unknown",
            failedUri.ToString(),
            reason);

        try
        {
            _webView.NavigateToString(html, ErrorPageBaseUri);
            if (_routeText is not null)
                _routeText.Text = PluginWebViewDevTools.GetRouteText(failedUri, _routeBasePath);
            if (_statusText is not null)
                _statusText.Text = "Failed - retry available";
        }
        catch
        {
            if (_statusText is not null)
                _statusText.Text = "Failed";
        }
    }

    private void RetryTarget()
    {
        if (_webView is null || _targetUri is null)
            return;

        _isErrorPageActive = false;
        _webView.Source = _targetUri;
        if (_statusText is not null)
            _statusText.Text = "Loading";
    }

    private void OnDevBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_webView is not null && _webView.CanGoBack)
            _webView.GoBack();
    }

    private void OnDevForwardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_webView is not null && _webView.CanGoForward)
            _webView.GoForward();
    }

    private void OnDevRefreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isErrorPageActive)
        {
            RetryTarget();
            return;
        }

        _webView?.Refresh();
    }

    /// <summary>
    /// 在系统浏览器中打开 Web 调试面板（<c>/__lybox/debug</c>）。
    /// 面板端点仅在 Debug 配置编译；Release 下按钮隐藏且此方法为 no-op。
    /// </summary>
    private void OnDevDebugPanelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
#if DEBUG
        if (_webHost is not { IsRunning: true })
            return;

        var debugUri = new Uri($"{_webHost.BaseUrl}/__lybox/debug");
        _ = LaunchUriInSystemBrowserAsync(debugUri);
#endif
    }

#if DEBUG
    private async Task LaunchUriInSystemBrowserAsync(Uri uri)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null)
                await launcher.LaunchUriAsync(uri).ConfigureAwait(true);
        }
        catch
        {
            // 系统浏览器启动失败不阻塞开发工具栏
        }
    }
#endif
}
