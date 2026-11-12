using System.Globalization;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Sinks.SystemConsole.Themes;

namespace LYBox.Layout.Core.Logging;

/// <summary>
/// 宿主日志基础设施：构造 Serilog 日志器，供 Microsoft.Extensions.Logging 通过
/// <c>AddSerilog</c> 桥接使用（插件与业务代码继续使用 <see cref="Microsoft.Extensions.Logging.ILogger{T}"/>）。
/// <para>
/// 输出 sink：控制台 + 按目录隔离的滚动文件（<see cref="RollingFileSink"/>）。
/// 时间戳统一为 UTC，前缀格式与既有 ZLogger 纯文本格式保持一致：
/// <c>[2026-07-10 07:39:43.123] [Information] [分类名] 消息内容</c>。
/// </para>
/// </summary>
internal static class SerilogLogging
{
    /// <summary>
    /// 控制台与文件共用输出模板：UTC 时间戳 + 级别 + 分类（SourceContext）+ 消息 + 异常。
    /// </summary>
    internal const string OutputTemplate =
        "[{UtcTimestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// 为指定日志目录创建独立的 Serilog 日志器（控制台 + 该目录下的滚动文件）。
    /// <para>
    /// 最低级别设为 Verbose：级别过滤交由 Microsoft.Extensions.Logging 的
    /// <c>SetMinimumLevel</c> / <c>AddFilter</c> 处理，保持与改造前一致的过滤语义。
    /// </para>
    /// </summary>
    internal static Serilog.Core.Logger CreateLogger(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(outputTemplate: OutputTemplate, theme: ConsoleTheme.None)
            .WriteTo.Sink(
                new RollingFileSink(
                    logDirectory,
                    new MessageTemplateTextFormatter(OutputTemplate, CultureInfo.InvariantCulture)),
                LogEventLevel.Verbose)
            .CreateLogger();
    }
}
