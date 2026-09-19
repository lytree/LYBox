using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using LYBox.Plugin.Shared.Rpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace LYBox.Plugin.Shared.Web;

/// <summary>
/// 嵌入式 Kestrel HTTP 资源服务。单进程内全局唯一，启动在 <c>127.0.0.1</c> 自动分配端口。
/// 提供：
/// <list type="bullet">
/// <item><c>GET /{pluginId}/{**path}</c>：服务插件的 <c>wwwroot/</c> 静态资源</item>
/// <item><c>GET /sse/{pluginId}</c>：SSE 长连接，C# 主动推送事件 / 通道数据</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// 生命周期：在 <c>App.Initialize()</c> 中通过 DI 注册为单例，<c>ServiceProvider</c> 构建后由宿主调用
/// <see cref="StartAsync"/>；但懒加载——只有插件主动调用 <see cref="MapPluginRoot"/> 注册资源根目录后
/// 才真正启动 Kestrel，否则保持关闭。应用退出时随 <c>ServiceProvider.Dispose()</c> 自动停止
/// （实现 <see cref="IAsyncDisposable"/>）。
/// </para>
/// <para>
/// 路由注册：每个 Web 插件在 <c>RegisterAsync</c> 阶段主动调用 <see cref="MapPluginRoot"/> 注册其
/// <c>wwwroot</c> 目录；未主动注册的插件不会被服务，其 <c>WebPluginView</c> 也不会渲染 WebView。
/// 请求 <c>/{pluginId}/foo.css</c> 时按 pluginId 查找目录后通过 <c>StaticFiles</c> 中间件返回。
/// </para>
/// <para>
/// 端口分配：默认使用 <c>http://127.0.0.1:0</c> 让系统自动分配端口；开发调试可用环境变量
/// <c>LYBOX_WEB_PORT</c> 指定固定端口（被占用时自动回退随机端口）。启动后通过
/// <see cref="Port"/> 暴露实际端口；<c>WebPluginView</c> 据此构造 <c>WebView.Source</c>。
/// </para>
/// </remarks>
public sealed class WebHostService : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly ConcurrentDictionary<string, string> _pluginRoots = new();
    private readonly PluginRpcDispatcher _rpcDispatcher = new();
    private readonly ConcurrentDictionary<string, WebSession> _sessions = new(StringComparer.Ordinal);
    private readonly SseEventPusher _ssePusher = new();
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();
    private readonly IReadOnlyList<Uri> _allowedOrigins;
    private readonly int? _requestedPort;
    private readonly Dictionary<string, FileSystemWatcher> _devWatchers = new();
    private readonly Dictionary<string, System.Threading.Timer> _reloadTimers = new();

    /// <summary>热刷新防抖窗口：聚合编辑器保存产生的连续文件事件。</summary>
    private static readonly TimeSpan ReloadDebounceDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 创建 WebHostService。默认放行来源为自身 <see cref="BaseUrl"/>；宿主可按需注入
    /// <paramref name="allowedOrigins"/> 扩展来源白名单（如开发代理的 <c>http://localhost:5173</c>）。
    /// </summary>
    /// <param name="allowedOrigins">可选来源白名单。为空时回退到 <see cref="BaseUrl"/> 单一来源（P7）。</param>
    public WebHostService(IReadOnlyList<Uri>? allowedOrigins = null)
    {
        _allowedOrigins = allowedOrigins ?? Array.Empty<Uri>();
    }

    /// <summary>
    /// 测试注入构造函数：绕过环境变量直接指定监听端口（<c>0</c> 表示随机分配）。
    /// </summary>
    internal WebHostService(int requestedPort)
    {
        _requestedPort = requestedPort;
        _allowedOrigins = Array.Empty<Uri>();
    }

    /// <summary>当前监听端口。0 表示尚未启动。</summary>
    public int Port { get; private set; }

    /// <summary>是否已启动 Kestrel 监听。仅当有插件主动注册后才会启动。</summary>
    public bool IsRunning => _app is not null;

    /// <summary>是否有插件已显式注册 Web 资源根目录。无注册则静态资源服务保持关闭。</summary>
    public bool HasRegisteredPlugins => _pluginRoots.Count > 0;

    /// <summary>指定插件是否已主动注册其 Web 资源根目录。</summary>
    public bool IsRegistered(string pluginId) =>
        !string.IsNullOrEmpty(pluginId) && _pluginRoots.ContainsKey(pluginId);

    /// <summary>资源服务 BaseUrl，例如 <c>http://127.0.0.1:54321</c>。</summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>SSE 推送器（供 <c>WebViewIpcHost</c> 注入）。</summary>
    public IEventPusher EventPusher => _ssePusher;

    /// <summary>Single RPC registry used by both the WebView and HTTP transports.</summary>
    public PluginRpcDispatcher RpcDispatcher => _rpcDispatcher;

    /// <summary>
    /// 注册一个插件范围内的 RPC 命令处理器。生产 HTTP 桥要求 pluginId 与短期会话同时匹配。
    /// </summary>
    /// <param name="pluginId">命令所属插件。</param>
    /// <param name="name">命令短名（如 <c>GreetAsync</c>），需与前端 <c>window.__lybox.rpc(name, ...)</c> 的 name 参数一致。</param>
    /// <param name="handler">命令处理器。</param>
    public void RegisterRpcHandler(string pluginId, string name, RpcCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);

        _rpcDispatcher.RegisterLegacy(pluginId, name, handler);
    }

    /// <summary>为一个可信 WebView 文档创建短期会话。会话只允许访问指定插件的 RPC 与 SSE。</summary>
    public string CreateSession(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        _sessions[token] = new WebSession(pluginId);
        return token;
    }

    /// <summary>撤销 WebView 文档会话。控件卸载或重建后旧页面不能继续访问宿主。</summary>
    public void RevokeSession(string? token)
    {
        if (!string.IsNullOrEmpty(token) && _sessions.TryRemove(token, out var session))
            session.Dispose();
    }

    /// <summary>获取所有已注册 RPC 命令名（供调试面板展示）。</summary>
    public IReadOnlyCollection<string> GetRegisteredRpcCommands() =>
        _rpcDispatcher.GetMethods();

    /// <summary>
    /// 注册一个 Web 插件的静态资源根目录。
    /// 必须在 <see cref="StartAsync"/> 之前或之后均可调用（线程安全的字典写入），
    /// 但建议在启动前注册所有插件以保证首请求即可服务。
    /// </summary>
    /// <param name="pluginId">插件 ID（路由前缀）。</param>
    /// <param name="wwwrootPath">插件 <c>wwwroot/</c> 绝对路径。</param>
    /// <remarks>
    /// S2 BC-3：收缩为内部 API，仅宿主 <c>PluginLoader.RegisterWebPlugins</c> 可调用
    /// （经 <see cref="System.Runtime.CompilerServices.InternalsVisibleTo"/> 授权）。
    /// 插件代码不再直接调用注册，消除"忘写注册"类错误。
    /// </remarks>
    internal void MapPluginRoot(string pluginId, string wwwrootPath)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("pluginId 不能为空", nameof(pluginId));
        if (string.IsNullOrWhiteSpace(wwwrootPath))
            throw new ArgumentException("wwwrootPath 不能为空", nameof(wwwrootPath));

        // 开发模式回退：bin 目录下无 wwwroot 时，尝试源码目录
        var resolvedPath = wwwrootPath;
        var isDevFallback = false;
        if (!Directory.Exists(resolvedPath))
        {
            var devPath = ResolveDevWwwroot(pluginId);
            if (devPath is not null && Directory.Exists(devPath))
            {
                resolvedPath = devPath;
                isDevFallback = true;
                Console.Error.WriteLine(
                    "[WebHostService] 插件 {0} 使用开发模式 wwwroot: {1}", pluginId, devPath);
            }
        }

        if (!Directory.Exists(resolvedPath))
            throw new DirectoryNotFoundException($"wwwroot 目录不存在: {wwwrootPath}");

        _pluginRoots[pluginId] = resolvedPath;

        // dev 回退被采用时启用热刷新：文件变化 → SSE 推 __lybox:reload → 前端自动刷新
        if (isDevFallback)
            StartDevWatcher(pluginId, resolvedPath);
    }

    /// <summary>
    /// 开发模式热刷新：监听插件 dev wwwroot 文件变化，经 SSE <c>dispatch</c> 推送
    /// <c>__lybox:reload</c> 事件（ipc.js 内置监听并 <c>location.reload()</c>）。
    /// 每插件一个 <see cref="FileSystemWatcher"/>；防抖 300ms 聚合编辑器保存产生的多次事件。
    /// </summary>
    internal void StartDevWatcher(string pluginId, string devRoot)
    {
        lock (_devWatchers)
        {
            if (_devWatchers.ContainsKey(pluginId))
                return;

            try
            {
                var watcher = new FileSystemWatcher(devRoot)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                };
                watcher.Changed += (_, e) => OnDevFileChanged(pluginId, e.Name);
                watcher.Created += (_, e) => OnDevFileChanged(pluginId, e.Name);
                watcher.Deleted += (_, e) => OnDevFileChanged(pluginId, e.Name);
                watcher.Renamed += (_, e) => OnDevFileChanged(pluginId, e.Name);
                watcher.EnableRaisingEvents = true;
                _devWatchers[pluginId] = watcher;
                Console.Error.WriteLine(
                    "[WebHostService] 已启用插件 {0} 的开发模式热刷新（监听 {1}）", pluginId, devRoot);
            }
            catch (Exception ex)
            {
                // 监听创建失败（目录不可访问等）不阻断静态服务，仅失去热刷新
                Console.Error.WriteLine(
                    "[WebHostService] 插件 {0} 热刷新监听创建失败: {1}", pluginId, ex.Message);
            }
        }
    }

    /// <summary>文件变化防抖入口：临时文件忽略；300ms 窗口内多次事件合并为一次 reload 推送。</summary>
    private void OnDevFileChanged(string pluginId, string? relativeName)
    {
        if (IsTransientFile(relativeName))
            return;

        lock (_reloadTimers)
        {
            if (_reloadTimers.TryGetValue(pluginId, out var timer))
            {
                timer.Change(ReloadDebounceDelay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                System.Threading.TimerCallback push = _ => _ = PushDevReloadAsync(pluginId);
                _reloadTimers[pluginId] = new System.Threading.Timer(
                    push, null, ReloadDebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private Task PushDevReloadAsync(string pluginId) =>
        _ssePusher.PushAsync(pluginId, "dispatch", "{\"name\":\"__lybox:reload\"}");

    /// <summary>临时/编辑器工作文件判定（点前缀、.tmp/.swp/波浪号后缀）。</summary>
    private static bool IsTransientFile(string? relativeName)
    {
        if (string.IsNullOrEmpty(relativeName))
            return false;
        var fileName = Path.GetFileName(relativeName);
        return fileName.StartsWith('.')
            || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".swp", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("~", StringComparison.Ordinal);
    }

    /// <summary>
    /// 解析开发来源白名单文本（环境变量 <c>LYBOX_DEV_ORIGINS</c>，分号或逗号分隔的绝对 URI）。
    /// 无效条目跳过并输出 stderr 警告；未设置返回空数组（CORS 仅放行自身 <see cref="BaseUrl"/>）。
    /// 典型用途：放行 Vite 开发服务器来源（如 <c>http://localhost:5173</c>），前端跑在
    /// Vite dev server（HMR），RPC/SSE 经宿主代理。
    /// </summary>
    public static Uri[] ParseOrigins(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<Uri>();

        var origins = new List<Uri>();
        foreach (var part in raw.Split(';', ','))
        {
            var candidate = part.Trim();
            if (candidate.Length == 0)
                continue;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && !string.IsNullOrWhiteSpace(uri.Host))
            {
                origins.Add(uri);
            }
            else
            {
                Console.Error.WriteLine("[WebHostService] 忽略无效的开发来源白名单条目: {0}", candidate);
            }
        }
        return origins.ToArray();
    }

    /// <summary>
    /// 开发模式下定位插件源码目录的 wwwroot。
    /// 优先：环境变量 LYBOX_PLUGIN_SRC_{PluginId}（下划线替换连字符）。
    /// 回退：从 AVALONIA_EXTRA_PLUGINS_PATH 向上遍历查找含 wwwroot 子目录的祖先。
    /// </summary>
    private static string? ResolveDevWwwroot(string pluginId)
    {
        var envKey = $"LYBOX_PLUGIN_SRC_{pluginId.Replace("-", "_")}";
        var envPath = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(envPath))
        {
            var candidate = Path.Combine(envPath, "wwwroot");
            if (Directory.Exists(candidate)) return candidate;
        }

        var extraPath = Environment.GetEnvironmentVariable("AVALONIA_EXTRA_PLUGINS_PATH");
        if (!string.IsNullOrEmpty(extraPath))
        {
            var dir = new DirectoryInfo(extraPath);
            for (var i = 0; i < 6 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "wwwroot");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// 允许在无任何插件注册时启动 Kestrel（默认 false 保持懒加载语义）。
    /// 宿主 <c>--web-vite</c> 开发场景置 true：Vite dev server 需要 WebHost 作为
    /// RPC/SSE 代理目标与调试面板后端，即使本宿主实例未加载 Web 插件。
    /// </summary>
    public bool AllowStartWithoutPlugins { get; set; }

    /// <summary>
    /// 启动 Kestrel 监听。懒加载：若没有任何插件主动注册 Web 资源则不启动（保持关闭）。
    /// 端口：<see cref="ResolveRequestedPort"/> 指定固定端口（环境变量 <c>LYBOX_WEB_PORT</c>）；
    /// 固定端口启动失败（典型：被占用）时回退 OS 随机端口重试，保证 Web 功能可用。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null) return; // 幂等
        if (_pluginRoots.Count == 0 && !AllowStartWithoutPlugins) return; // 懒加载：无插件注册则不启动 Kestrel

        var port = ResolveRequestedPort();
        try
        {
            await StartCoreAsync(port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (port != 0)
        {
            // 固定端口启动失败：释放半启动状态后回退 OS 随机端口重试
            await DisposeAppAsync().ConfigureAwait(false);
            Console.Error.WriteLine(
                "[WebHostService] 固定端口 {0} 启动失败（{1}），回退随机端口", port, ex.Message);
            await StartCoreAsync(0, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 解析期望的固定监听端口。优先级：测试注入端口 &gt; 环境变量 <c>LYBOX_WEB_PORT</c>（1-65535）；
    /// 未设置或无效时返回 0（OS 随机分配）。
    /// </summary>
    private int ResolveRequestedPort() =>
        _requestedPort ?? ParsePort(Environment.GetEnvironmentVariable("LYBOX_WEB_PORT"));

    /// <summary>解析端口文本。有效范围 1-65535；空/非数字/超范围返回 0（随机分配）。</summary>
    internal static int ParsePort(string? raw) =>
        int.TryParse(raw, out var port) && port is > 0 and <= 65535 ? port : 0;

    private async Task StartCoreAsync(int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        _app = builder.Build();

        // —— SSE 端点 ——
        _app.MapGet("/sse/{pluginId}", async (string pluginId, HttpContext ctx) =>
        {
            if (!TryAuthorize(ctx, pluginId, allowQueryToken: true, out var session))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            ctx.Response.Headers["Connection"] = "keep-alive";
            ctx.Response.Headers["X-Accel-Buffering"] = "no"; // 禁用 Nginx 缓冲（兼容反向代理场景）

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                ctx.RequestAborted,
                session.CancellationToken);
            await using var client = new SseClient(ctx.Response.Body, lifetime.Token);
            _ssePusher.Subscribe(pluginId, client);
            try
            {
                var readyPayload = JsonSerializer.Serialize(new { pluginId }, RpcEnvelope.JsonOptions);
                client.TryEnqueue("ready", readyPayload);
                await client.Completion.ConfigureAwait(false);
            }
            finally
            {
                _ssePusher.Unsubscribe(pluginId, client);
            }
        });

        // —— HTTP 桥接端点（浏览器模式 / 调试面板使用）——
        // POST /__bridge/{pluginId}/{action}
        //   action = "rpc"             body = {name, args, callbackId?} → 响应 {result} 或 {error}
        //   action = "emit"            事件 emit（浏览器模式无 WebViewIpcHost 消费，返回 202 Accepted）
        //   action = "channel-close"   通道关闭（返回 202 Accepted）
        // 复用 WebViewIpcHost.RegisterCommand 同步注册到本服务的同一套 handler，确保 WebView 与浏览器行为一致。
        // S4 BC-6：将原 /__rpc、/__emit、/__channel/close 三个端点合并为单一 /__bridge 路由，缩小路由表攻击面。
        _app.MapPost("/__bridge/{pluginId}/{action}", async (string pluginId, string action, HttpContext ctx) =>
        {
            if (!TryAuthorize(ctx, pluginId, allowQueryToken: false, out _))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                if (action == "rpc")
                    await ctx.Response.WriteAsJsonAsync(new { error = "Web 会话无效或已过期" }, ctx.RequestAborted).ConfigureAwait(false);
                return;
            }

            switch (action)
            {
                case "rpc":
                    await HandleRpc(pluginId, ctx).ConfigureAwait(false);
                    break;
                case "emit":
                case "channel-close":
                    // 浏览器模式的事件 emit / 通道关闭一般无业务需求，返回 202 Accepted 即可。
                    ctx.Response.StatusCode = StatusCodes.Status202Accepted;
                    break;
                default:
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    break;
            }
        });

        // Generated clients are registered by source-generated bindings before the WebView navigates.
        // They live under the plugin path so every plugin can use a relative module import.
        _app.MapGet("/{pluginId}/.lybox/{artifact}", (string pluginId, string artifact, HttpContext ctx) =>
        {
            if (!_pluginRoots.ContainsKey(pluginId)
                || !_rpcDispatcher.TryGetArtifact(pluginId, artifact, out var generated))
            {
                return Results.NotFound();
            }

            ctx.Response.Headers["Cache-Control"] = "no-cache";
            return Results.Content(generated.Content, generated.ContentType);
        });

        // —— 调试面板端点 ——
        // GET /__lybox/debug 返回纯 HTML 调试器，列出所有已注册 RPC 命令 + SSE 事件流查看器。
        // 注意：这些端点暴露的是同进程内的 RPC + 会话签发能力，与宿主 UI 同权限；
        // 调用方需自行控制监听地址/Origin 白名单（已通过 LYBOX_DEV_ORIGINS 实现）。
        // 不再受 #if DEBUG 包裹，Release 下也开放——CI 在 Release 配置运行测试，
        // 且外部浏览器调试面板在生产宿主中也需要这些端点。
        _app.MapGet("/__lybox/debug", () => Results.Content(
            DebugPanelHtml.Render(GetRegisteredRpcCommands()),
            "text/html; charset=utf-8"));

        // 调试辅助：列出已注册 Web 插件（面板下拉候选，免去手填 pluginId）
        _app.MapGet("/__lybox/debug/plugins", () => Results.Json(_pluginRoots.Keys.ToArray()));

        // 调试辅助：为指定插件签发调试会话。解决"外部浏览器拿不到 WebView 内部 session，
        // 导致面板的生产 RPC 调用不可用"的问题——面板一键创建后即可调用真实命令。
        _app.MapPost("/__lybox/debug/session/{pluginId}", (string pluginId) =>
        {
            if (!_pluginRoots.ContainsKey(pluginId))
                return Results.NotFound(new { error = $"Plugin '{pluginId}' not registered." });
            return Results.Ok(new { pluginId, session = CreateSession(pluginId) });
        });

        // —— 静态资源端点：按 pluginId 路由分发 ——
        // 路由模板：/{pluginId}/{**path}，catch-all 参数 path 可能包含子目录分隔符
        _app.MapGet("/{pluginId}/{**path}", async (string pluginId, string path, HttpContext ctx) =>
        {
            if (!_pluginRoots.TryGetValue(pluginId, out var root))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsync($"Plugin '{pluginId}' not registered.", ctx.RequestAborted).ConfigureAwait(false);
                return;
            }

            // 防目录穿越：标准化路径后确保结果仍在 root 之下
            var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var combined = Path.GetFullPath(Path.Combine(rootFull, path.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(rootFull, combined);
            if (relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!File.Exists(combined))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            ctx.Response.ContentType = GetContentType(combined);
            await ctx.Response.SendFileAsync(combined, ctx.RequestAborted).ConfigureAwait(false);
        });

        // —— SDK 静态资源端点（S4）：浏览器模式下供前端 <script type="module" src="/sdk/lybox-plugin-sdk.js"> / <link href="/sdk/lybox-plugin-theme.css"> 引用 ——
        // 资源来自 LYBox.Plugin.Shared.Web.Assets 嵌入资源（见 PluginWebSdkResources），与宿主进程同生命周期。
        _app.MapGet("/sdk/lybox-plugin-sdk.js", (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/javascript; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=300";
            return ctx.Response.WriteAsync(PluginWebSdkResources.ReadResource(PluginWebSdkResources.SdkScriptResourceName), ctx.RequestAborted);
        });
        // —— SDK TypeScript 类型声明：Vite dev 经同源代理拉取本端点；WebView 生产直连宿主 ——
        // 业务侧 import '/sdk/lybox-plugin-sdk' 时拿到的类型提示来自此处。
        _app.MapGet("/sdk/lybox-plugin-sdk.d.ts", (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/typescript; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=300";
            return ctx.Response.WriteAsync(PluginWebSdkResources.ReadResource(PluginWebSdkResources.SdkTypeDeclarationsResourceName), ctx.RequestAborted);
        });
        _app.MapGet("/sdk/lybox-plugin-theme.css", (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/css; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=300";
            return ctx.Response.WriteAsync(PluginWebSdkResources.ReadResource(PluginWebSdkResources.ThemeStylesheetResourceName), ctx.RequestAborted);
        });

        // —— 根路径健康检查 ——
        _app.MapGet("/", () => Results.Ok(new { service = "LYBox.WebHostService", version = "1.0" }));

        await _app.StartAsync(cancellationToken).ConfigureAwait(false);

        // 从实际监听地址提取端口
        Port = ExtractPort(_app.Urls.FirstOrDefault());
    }

    /// <summary>
    /// 处理 <c>/__bridge/{pluginId}/rpc</c> 请求：反序列化请求体、解析命令、执行并回写结果。
    /// 从被合并的 <c>/__rpc</c> 端点迁入 <see cref="HandleRpc"/>，保持 WebView 与浏览器行为一致。
    /// </summary>
    private async Task HandleRpc(string pluginId, HttpContext ctx)
    {
        HttpRequestRpcBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<HttpRequestRpcBody>(ctx.Request.Body, RpcEnvelope.JsonOptions, ctx.RequestAborted).ConfigureAwait(false);
        }
        catch
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { error = "请求体无效：需 JSON 格式 {name, args, callbackId?}" }, ctx.RequestAborted).ConfigureAwait(false);
            return;
        }
        if (body is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(
                new PluginRpcResult(
                    string.Empty,
                    false,
                    Error: new PluginRpcError(PluginRpcErrorCodes.InvalidRequest, "RPC request is empty.")),
                RpcEnvelope.JsonOptions,
                cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        var requestId = body.Id ?? body.CallbackId ?? Guid.NewGuid().ToString("N");
        PluginRpcResult result;
        if (body.IsCanonical)
        {
            if (!string.IsNullOrEmpty(body.PluginId)
                && !string.Equals(body.PluginId, pluginId, StringComparison.Ordinal))
            {
                result = new PluginRpcResult(
                    requestId,
                    false,
                    Error: new PluginRpcError(
                        PluginRpcErrorCodes.PluginMismatch,
                        "RPC plugin does not match the request route."));
            }
            else
            {
                result = await _rpcDispatcher.InvokePayloadAsync(
                    new PluginRpcCall(
                        requestId,
                        pluginId,
                        body.Method ?? body.Name,
                        body.Payload ?? RpcJson.Null,
                        body.TraceId),
                    ctx.RequestAborted).ConfigureAwait(false);
            }
        }
        else
        {
            result = await _rpcDispatcher.InvokeLegacyAsync(
                requestId,
                pluginId,
                body.Name,
                body.Args ?? Array.Empty<JsonElement>(),
                ctx.RequestAborted).ConfigureAwait(false);
        }

        ctx.Response.StatusCode = GetRpcStatusCode(result);
        await ctx.Response.WriteAsJsonAsync(
            result,
            RpcEnvelope.JsonOptions,
            cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
    }

    private bool TryAuthorize(
        HttpContext context,
        string pluginId,
        bool allowQueryToken,
        out WebSession session)
    {
        session = null!;
        if (!_pluginRoots.ContainsKey(pluginId))
            return false;

        var token = context.Request.Headers["X-LYBox-Session"].FirstOrDefault();
        if (allowQueryToken && string.IsNullOrEmpty(token))
            token = context.Request.Query["session"].FirstOrDefault();

        if (string.IsNullOrEmpty(token)
            || !_sessions.TryGetValue(token, out var authorizedSession)
            || !string.Equals(authorizedSession.PluginId, pluginId, StringComparison.Ordinal)
            || authorizedSession.CancellationToken.IsCancellationRequested)
            return false;
        session = authorizedSession;

        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
            return true;

        // 来源白名单：显式注入的 AllowedOrigins（如开发代理）优先；否则回退到自身 BaseUrl（P7 默认）。
        foreach (var allowed in _allowedOrigins)
        {
            if (string.Equals(origin.TrimEnd('/'), allowed.GetLeftPart(UriPartial.Authority).TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return string.Equals(origin.TrimEnd('/'), BaseUrl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 显式停止 Kestrel 并释放会话。宿主 <c>App.OnShutdownRequested</c> 应优先于
    /// <c>ServiceProvider.Dispose()</c> 调用，确保 SSE 连接先于 Kestrel 关闭，避免推送器在
    /// 服务已释放后仍尝试写入。幂等，可安全重复调用。
    /// </summary>
    public async ValueTask EnsureStoppedAsync()
    {
        await DisposeAppAsync().ConfigureAwait(false);
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
        _ssePusher.Clear();
        Port = 0;

        // 释放热刷新资源（watcher 与防抖定时器）
        lock (_devWatchers)
        {
            foreach (var watcher in _devWatchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _devWatchers.Clear();
        }
        lock (_reloadTimers)
        {
            foreach (var timer in _reloadTimers.Values)
                timer.Dispose();
            _reloadTimers.Clear();
        }
    }

    /// <summary>停止并释放当前 <see cref="WebApplication"/>（若存在）。幂等。</summary>
    private async Task DisposeAppAsync()
    {
        if (_app is not null)
        {
            try
            {
                await _app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch { /* 关闭过程中止：忽略，继续 */ }
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => EnsureStoppedAsync();

    private static int ExtractPort(string? url)
    {
        if (string.IsNullOrEmpty(url)) return 0;
        // URL 形如 http://127.0.0.1:54321
        var colonIndex = url.LastIndexOf(':');
        if (colonIndex < 0 || colonIndex == url.Length - 1) return 0;
        return int.TryParse(url.AsSpan(colonIndex + 1), out var port) ? port : 0;
    }

    private string GetContentType(string filePath)
    {
        // 使用 AspNetCore.StaticFiles 内置内容类型表（约 400+ 扩展名），替代手写 25+ 扩展名。
        // 仅对 ASP.NET 未识别的扩展名回退到 application/octet-stream。
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext)
            && _contentTypeProvider.TryGetContentType(ext, out var contentType))
        {
            return contentType;
        }
        return "application/octet-stream";
    }

    private static int GetRpcStatusCode(PluginRpcResult result) => result.Error?.Code switch
    {
        null => StatusCodes.Status200OK,
        PluginRpcErrorCodes.InvalidRequest or PluginRpcErrorCodes.InvalidPayload => StatusCodes.Status400BadRequest,
        PluginRpcErrorCodes.MethodNotFound => StatusCodes.Status404NotFound,
        PluginRpcErrorCodes.PluginMismatch => StatusCodes.Status403Forbidden,
        PluginRpcErrorCodes.Cancelled => 499,
        PluginRpcErrorCodes.Timeout => StatusCodes.Status408RequestTimeout,
        PluginRpcErrorCodes.Busy => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };
}

internal sealed class WebSession(string pluginId) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    public string PluginId { get; } = pluginId;

    public CancellationToken CancellationToken => _lifetime.Token;

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

/// <summary>POST /__rpc 请求体。callbackId 在浏览器模式下由 ipc.js 内部使用，HTTP 响应直接返回 result。</summary>
internal sealed class HttpRequestRpcBody
{
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public int Version { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string? Id { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("pluginId")]
    public string? PluginId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public string? Method { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("args")]
    public JsonElement[]? Args { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("callbackId")]
    public string? CallbackId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCanonical => Version == 2 || string.Equals(Kind, "plugin-rpc-call", StringComparison.Ordinal);
}
