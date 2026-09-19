using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LYBox.Plugin.Shared.Web;

/// <summary>
/// WebView 前端 console 输出桥：解析 ipc.js 发送的 <c>'L'</c> 前缀信封
/// （<c>JSON: {"l":"log|info|warn|error","a":["..."]}</c>），映射为宿主 <see cref="LogLevel"/>。
/// 使前端 console.log/warn/error 与 C# 日志同流，便于宿主侧排障。
/// </summary>
/// <remarks>
/// ipc.js 仅在 WebView 模式安装 console hook（浏览器/mock 模式有原生 DevTools，无需桥接）。
/// 频率控制依赖前端截断（单参数 2000 字符、最多 8 参数）与宿主日志级别过滤，不做额外节流。
/// </remarks>
internal static class WebConsoleBridge
{
    /// <summary>
    /// 尝试解析 console 桥信封。
    /// 返回 (级别, 消息)；非 <c>'L'</c> 信封（RPC/事件/重试消息等）或格式非法时返回 null。
    /// </summary>
    public static (string Level, string Message)? TryParse(string? body)
    {
        if (string.IsNullOrEmpty(body) || body[0] != 'L')
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body[1..]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (!root.TryGetProperty("l", out var level) || level.ValueKind != JsonValueKind.String)
                return null;
            if (!root.TryGetProperty("a", out var args) || args.ValueKind != JsonValueKind.Array)
                return null;

            var parts = new List<string>();
            foreach (var arg in args.EnumerateArray())
                parts.Add(arg.ValueKind == JsonValueKind.String ? arg.GetString() ?? string.Empty : arg.GetRawText());

            return (level.GetString() ?? "log", string.Join(" ", parts));
        }
        catch (JsonException)
        {
            // 非法 'L' 信封：按未知消息忽略，交由其他订阅者（Rpc 传输层）继续处理
            return null;
        }
    }

    /// <summary>console 级别 → <see cref="LogLevel"/> 映射。未知级别按 Debug 处理。</summary>
    public static LogLevel ToLogLevel(string? level) => level switch
    {
        "error" => LogLevel.Error,
        "warn" => LogLevel.Warning,
        "info" => LogLevel.Information,
        _ => LogLevel.Debug,
    };
}
