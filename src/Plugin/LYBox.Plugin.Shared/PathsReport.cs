using System.Text;

namespace LYBox.Plugin.Shared;

/// <summary>
/// 启动期 / 运行期路径 dump 统一报告器。
/// <para>
/// 早期(DI 尚未构建)与运行期(Logger 已就绪)两套 dump 共用同一份布局,
/// 避免两处分别维护 <c>LYBOX_DATA_ROOT</c> / <c>LocalApplicationData</c> 解析逻辑。
/// </para>
/// <para>
/// 输出格式:多行,每行 <c>[标签] Key = Value</c>。前缀标签:
/// <list type="bullet">
/// <item><see cref="EarlyPrefix"/> —— DI 之前(DumpEarlyPaths / stderr)。</item>
/// <item><see cref="BootPrefix"/> —— DI 之后(启动 banner / ILogger)。</item>
/// </list>
/// </para>
/// </summary>
public static class PathsReport
{
    /// <summary>早期 dump 前缀(便于过滤)。</summary>
    public const string EarlyPrefix = "[LYBox.Early]";

    /// <summary>运行期启动 banner 前缀。</summary>
    public const string BootPrefix = "[LYBox.Boot]";

    /// <summary>
    /// 解析宿主数据根目录(无副作用、不创建目录):
    /// 优先级 = <see cref="LYBoxEnv.DataRootKey"/> 环境变量 > 平台默认目录 + "LYBox"。
    /// 供 DI 之前的 <c>DumpEarlyPaths</c> 与 DI 之后的启动 banner 共用。
    /// </summary>
    public static string ResolveHostDataRoot()
    {
        var envOverride = LYBoxEnv.Get(LYBoxEnv.DataRootKey);
        if (!string.IsNullOrWhiteSpace(envOverride))
            return Path.GetFullPath(envOverride);

        var special = OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ApplicationData;
        return Path.Combine(Environment.GetFolderPath(special), "LYBox");
    }

    /// <summary>当前进程的宿主数据根目录(已解析)。</summary>
    public static string CurrentHostDataRoot => ResolveHostDataRoot();

    /// <summary>当前进程的宿主主日志目录(根于启动器目录)。</summary>
    public static string CurrentLogsDirectory
        => Path.Combine(AppContext.BaseDirectory, WellKnownPaths.LogsSubDir);

    /// <summary>
    /// 构造早期 dump 行(不写 IO,调用方按需输出到 stderr 或 Console)。
    /// 包含:命令行、是否控制台模式、启动器目录、平台 LocalAppData、<c>LYBOX_DATA_ROOT</c>、
    /// 解析后的 <see cref="CurrentHostDataRoot"/>。
    /// 任意一处异常都不能让 dump 自身崩溃,逐项保护。
    /// </summary>
    public static IReadOnlyList<string> BuildEarlyLines(string[] args, bool consoleMode)
    {
        var lines = new List<string>(8);
        try
        {
            var envDataRoot = LYBoxEnv.Get(LYBoxEnv.DataRootKey);
            var localAppData = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            lines.Add($"{EarlyPrefix} Args                      = {string.Join(' ', args)}");
            lines.Add($"{EarlyPrefix} ConsoleMode               = {consoleMode}");
            lines.Add($"{EarlyPrefix} AppContext.BaseDirectory  = {AppContext.BaseDirectory}");
            lines.Add($"{EarlyPrefix} LocalAppData              = {localAppData}");
            lines.Add($"{EarlyPrefix} {LYBoxEnv.DataRootKey} (env)     = {(envDataRoot ?? "<not set>")}");
            lines.Add($"{EarlyPrefix} HostDataRoot (resolved)   = {CurrentHostDataRoot}");
        }
        catch
        {
            // dump 自身不能抛异常影响主流程
        }
        return lines;
    }

    /// <summary>
    /// 把早期 dump 行写到 <see cref="TextWriter"/>(典型为 <see cref="Console.Error"/>)。
    /// 任何 IO 异常被吞掉。
    /// </summary>
    public static void WriteEarlyLines(TextWriter writer, string[] args, bool consoleMode)
    {
        if (writer is null) return;
        try
        {
            foreach (var line in BuildEarlyLines(args, consoleMode))
                writer.WriteLine(line);
        }
        catch { /* IO 异常不影响主流程 */ }
    }

    /// <summary>
    /// 构造运行期启动 banner 行(由宿主在 DI 完成后输出到 ILogger)。
    /// 相比早期 dump 额外包含宿主主日志目录、插件日志目录样例(LYBox.Host)。
    /// </summary>
    public static IReadOnlyList<string> BuildBootLines(string? hostDataRootOverride = null)
    {
        var hostDataRoot = hostDataRootOverride ?? ResolveHostDataRoot();
        var logsDir = CurrentLogsDirectory;
        var pluginLogsDir = Path.Combine(logsDir, WellKnownPaths.PluginsSubDir, "LYBox.Host");

        var envDataRoot = LYBoxEnv.Get(LYBoxEnv.DataRootKey);
        var localAppData = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return
        [
            $"{BootPrefix} AppBaseDirectory         = {AppContext.BaseDirectory}",
            $"{BootPrefix} LocalApplicationData    = {localAppData}",
            $"{BootPrefix} {LYBoxEnv.DataRootKey} (env) = {(envDataRoot ?? "<not set>")}",
            $"{BootPrefix} HostDataRoot (resolved) = {hostDataRoot}",
            $"{BootPrefix} LogsDir                  = {logsDir} (roll: {WellKnownPaths.FormatRollingLogFileName(DateTimeOffset.UtcNow, 0)})",
            $"{BootPrefix} PluginLogsDir            = {pluginLogsDir} (LYBox.Host 独立通道)",
        ];
    }
}