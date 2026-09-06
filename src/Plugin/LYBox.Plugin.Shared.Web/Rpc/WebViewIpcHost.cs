using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using LYBox.Plugin.Shared.Web;

namespace LYBox.Plugin.Shared.Rpc;

/// <summary>
/// IPC 运行时主机（transport-agnostic）。实现 <see cref="IRpcHost"/>，
/// 在 <see cref="IRpcTransport.MessageReceived"/> 上按 Wails v2 前缀信封分发：
/// <c>C</c> 调用、<c>E</c> 事件、<c>X</c> 通道关闭。结果经
/// <c>window.__lybox.resolve</c> 回推 Promise（弥补 Avalonia WebView 的 fire-and-forget 缺陷）。
/// </summary>
/// <remarks>
/// <para>
/// C# → JS 推送有两套通道：
/// <list type="bullet">
/// <item><c>InvokeScript</c>：RPC resolve 同步回推（必须，因 Promise 需要返回值）。事件分发 / 通道数据降级走此通道。</item>
/// <item>SSE（<see cref="IEventPusher"/>）：高频推送通道。注入后 <see cref="EmitEventAsync"/> 与 <see cref="Channel{T}"/>.WriteAsync 优先走此通道。</item>
/// </list>
/// </para>
/// <para>
/// 构造时可选注入 <paramref name="eventPusher"/> + <paramref name="pluginId"/> 启用 SSE 推送；
/// 不注入则保持原有 <c>InvokeScript</c> 行为，向后兼容。
/// </para>
/// </remarks>
public sealed class WebViewIpcHost : IRpcHost, ICanonicalRpcHost, IDisposable
{
    private readonly IRpcTransport _transport;
    private readonly IEventPusher? _eventPusher;
    private readonly string? _pluginId;
    private readonly bool _usesSharedDispatcher;
    private readonly PluginRpcDispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, Channel> _channels = new();
    private readonly ConcurrentDictionary<string, List<Action<JsonElement?>>> _eventListeners = new();
    private TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource _documentLifetime = new();
    private readonly string _bootstrapJs;
    private bool _bootstrapInjected;
    private int _documentVersion;
    private bool _disposed;

    private const string ReadyEvent = "__lybox:ready";
    private const string StandalonePluginId = "local";

    /// <param name="transport">底层双向传输。</param>
    /// <param name="eventPusher">可选 SSE 推送器。注入后事件分发 / 通道数据优先走 SSE，避免 InvokeScript 队列堆积。</param>
    /// <param name="pluginId">与 SSE 路由 <c>/sse/{pluginId}</c> 对应的插件 ID。<paramref name="eventPusher"/> 非 null 时必须提供。</param>
    /// <param name="webHost">可选 WebHostService。注入后命令按 pluginId 隔离注册到受会话保护的 HTTP RPC 桥。</param>
    public WebViewIpcHost(IRpcTransport transport, IEventPusher? eventPusher = null, string? pluginId = null, WebHostService? webHost = null)
    {
        _transport = transport;
        _eventPusher = eventPusher;
        _pluginId = pluginId;
        _usesSharedDispatcher = webHost is not null;
        _dispatcher = webHost?.RpcDispatcher ?? new PluginRpcDispatcher();
        _transport.MessageReceived += OnMessage;
        _bootstrapJs = LoadBootstrap();
    }

    /// <summary>前端 __lybox 运行时就绪（握手完成）。注入引导脚本后等待此任务完成。</summary>
    public Task WhenReady => _readyTcs.Task;

    /// <summary>注入 ipc.js 引导脚本到页面。幂等。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_bootstrapInjected) return;
        await _transport.ExecuteScriptAsync(_bootstrapJs, cancellationToken);
        _bootstrapInjected = true;
    }

    /// <summary>
    /// 把已注册命令的清单注入页面。调用前应完成所有 RegisterCommand。
    /// 当前实现：manifest 仅含命令短名列表，供调试面板展示。前端调用统一走 <c>window.__lybox.rpc(name, args)</c>，无需构建 window.go。
    /// </summary>
    public async Task InjectBindingsAsync(CancellationToken cancellationToken = default)
    {
        var manifest = _dispatcher.GetMethods(_pluginId).Select(name => new { name });
        var json = JsonSerializer.Serialize(manifest, RpcEnvelope.JsonOptions);
        // 直接把 JSON 对象拼进脚本，避免对序列化结果二次序列化变成带引号的字符串字面量
        var js = $"window.__lybox && window.__lybox.setBindings({json});";
        await _transport.ExecuteScriptAsync(js, cancellationToken);
    }

    /// <summary>
    /// 注册一个 RPC 命令。命令名建议用短名（如 <c>GreetAsync</c>），与前端 <c>window.__lybox.rpc(name, ...)</c> 的 name 一致。
    /// 若注入了 <see cref="WebHostService"/>，命令同步注册到按 pluginId 隔离的 HTTP RPC 桥。
    /// </summary>
    public void RegisterCommand(string name, RpcCommandHandler handler)
    {
        _dispatcher.RegisterLegacy(RequirePluginId(), name, handler);
    }

    public void RegisterPayloadCommand(string name, RpcPayloadCommandHandler handler)
    {
        _dispatcher.RegisterPayload(RequirePluginId(), name, handler);
    }

    public void RegisterClientArtifact(RpcClientArtifact artifact)
    {
        _dispatcher.RegisterArtifact(RequirePluginId(), artifact);
    }

    public async Task EmitEventAsync(string name, object? data, CancellationToken cancellationToken = default)
    {
        var nameJson = JsonSerializer.Serialize(name, RpcEnvelope.JsonOptions);
        var dataJson = data is null ? "null" : JsonSerializer.Serialize(data, RpcEnvelope.JsonOptions);

        // 优先走 SSE（高频推送场景），无 pusher 时降级到 InvokeScript
        if (_eventPusher is not null && _pluginId is not null)
        {
            // SSE 负载格式：{"name":"...","data":...}
            var payload = $"{{\"name\":{nameJson},\"data\":{dataJson}}}";
            await _eventPusher.PushAsync(_pluginId, "dispatch", payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        var js = $"window.__lybox && window.__lybox.dispatch({nameJson},{dataJson});";
        await _transport.ExecuteScriptAsync(js, cancellationToken).ConfigureAwait(false);
    }

    public Channel<T> CreateChannel<T>(string? id = null)
    {
        var cid = id ?? Guid.NewGuid().ToString("N");
        var ch = new Channel<T>(cid, _transport, _eventPusher, _pluginId);
        _channels[cid] = ch;
        return ch;
    }

    /// <summary>订阅来自前端的事件（JS 经 __lybox.emit 发送）。</summary>
    public Action OnEvent(string name, Action<JsonElement?> handler)
    {
        var list = _eventListeners.GetOrAdd(name, _ => new List<Action<JsonElement?>>());
        lock (list) list.Add(handler);
        return () => { lock (list) list.Remove(handler); };
    }

    private void OnMessage(string? body)
    {
        if (string.IsNullOrEmpty(body)) return;
        var prefix = body[0];
        var payload = body.Substring(1);
        switch (prefix)
        {
            case RpcEnvelope.PrefixCall:
                _ = HandleCallAsync(payload);
                break;
            case RpcEnvelope.PrefixEvent:
                HandleEvent(payload);
                break;
            case RpcEnvelope.PrefixChannelClose:
                HandleChannelClose(payload);
                break;
        }
    }

    private async Task HandleCallAsync(string payload)
    {
        CallMessage msg;
        try { msg = JsonSerializer.Deserialize<CallMessage>(payload, RpcEnvelope.JsonOptions)!; }
        catch (Exception) { return; /* 无法解析的调用，丢弃 */ }

        if (msg.Name == ReadyEvent) return; // 握手由事件路径处理

        var documentVersion = _documentVersion;
        var documentToken = _documentLifetime.Token;
        var requestId = msg.IsCanonical ? msg.Id ?? msg.CallbackId : msg.CallbackId;
        var method = msg.IsCanonical ? msg.Method ?? msg.Name : msg.Name;
        PluginRpcResult result;
        if (msg.IsCanonical)
        {
            if (!string.IsNullOrEmpty(msg.PluginId)
                && !string.Equals(msg.PluginId, _pluginId, StringComparison.Ordinal))
            {
                result = new PluginRpcResult(
                    requestId,
                    false,
                    Error: new PluginRpcError(
                        PluginRpcErrorCodes.PluginMismatch,
                        "RPC plugin does not match the active document."));
            }
            else
            {
                result = await _dispatcher.InvokePayloadAsync(
                    new PluginRpcCall(requestId, RequirePluginId(), method, msg.Payload ?? RpcJson.Null),
                    documentToken).ConfigureAwait(false);
            }
        }
        else
        {
            result = await _dispatcher.InvokeLegacyAsync(
                requestId,
                RequirePluginId(),
                method,
                msg.Args,
                documentToken).ConfigureAwait(false);
        }

        if (documentToken.IsCancellationRequested || documentVersion != _documentVersion)
            return;

        var responsePayload = result.Payload;
        if (responsePayload is Channel channel)
        {
            var itemType = channel.GetType().IsGenericType
                ? channel.GetType().GetGenericArguments()[0].Name
                : "any";
            responsePayload = new { __channel = true, id = channel.Id, itemType };
        }

        await ResolveAsync(requestId, result.Error, responsePayload).ConfigureAwait(false);
    }

    /// <summary>清除上一文档的信任与运行状态，并取消其所有正在执行的 RPC。</summary>
    public void ResetDocument()
    {
        if (_disposed) return;

        Interlocked.Increment(ref _documentVersion);
        var previous = _documentLifetime;
        _documentLifetime = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
        _readyTcs.TrySetCanceled();
        _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _bootstrapInjected = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.MessageReceived -= OnMessage;
        _documentLifetime.Cancel();
        _documentLifetime.Dispose();
        foreach (var channel in _channels.Values)
            _ = channel.CloseAsync();
        _channels.Clear();
    }

    private void HandleEvent(string payload)
    {
        EventMessage? msg;
        try { msg = JsonSerializer.Deserialize<EventMessage>(payload, RpcEnvelope.JsonOptions); }
        catch { return; }
        if (msg is null) return;

        if (msg.Name == ReadyEvent)
        {
            _readyTcs.TrySetResult();
            return;
        }

        if (_eventListeners.TryGetValue(msg.Name, out var list))
        {
            List<Action<JsonElement?>> snapshot;
            lock (list) snapshot = list.ToList();
            foreach (var cb in snapshot)
            {
                try { cb(msg.Data); } catch { /* 单个监听器异常不影响其他 */ }
            }
        }
    }

    private void HandleChannelClose(string channelId)
    {
        if (_channels.TryRemove(channelId, out var ch))
        {
            _ = ch.CloseAsync();
        }
    }

    private async Task ResolveAsync(string callbackId, PluginRpcError? error, object? result)
    {
        var errJson = error is null ? "null" : JsonSerializer.Serialize(error, RpcEnvelope.JsonOptions);
        var resultJson = result is null ? "null" : JsonSerializer.Serialize(result, RpcEnvelope.JsonOptions);
        var js = $"window.__lybox && window.__lybox.resolve({JsonSerializer.Serialize(callbackId)},{errJson},{resultJson});";
        try { await _transport.ExecuteScriptAsync(js); }
        catch { /* 页面已销毁等，忽略 */ }
    }

    /// <summary>
    /// 返回用于命令注册 / 分派的插件 ID。优先使用显式注入的 <see cref="_pluginId"/>；
    /// 独立模式（未注入共享分发器的 <see cref="WebHostService"/>）时回退到本地占位 ID，
    /// 保证 standalone WebView IPC 无需 pluginId 也能正常注册与分发（向后兼容）。
    /// 注入共享分发器时 pluginId 是命令隔离所必需，缺失则抛错。
    /// </summary>
    private string RequirePluginId()
    {
        if (!string.IsNullOrWhiteSpace(_pluginId)) return _pluginId;
        if (_usesSharedDispatcher)
            throw new InvalidOperationException("A pluginId is required for RPC registration.");
        return StandalonePluginId;
    }

    private static string LoadBootstrap()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("ipc.js", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("找不到嵌入式 ipc.js 资源");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
