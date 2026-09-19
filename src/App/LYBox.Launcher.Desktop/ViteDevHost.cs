using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace LYBox.Launcher.Desktop;

/// <summary>
/// Vite 开发服务器宿主托管（<c>--web-vite</c>）：由 C# 端拉起 <c>npm run dev</c> 并托管其生命周期，
/// 开发者无需手动开第二个终端。端口经 <c>LYBOX_WEB_PORT</c> 环境变量与 WebHostService 自动联动
/// （<see cref="Program.ApplyWebViteEnvironment"/> 在 Main 首部保证该变量存在，子进程自然继承）。
/// </summary>
/// <remarks>
/// 输出以 <c>[vite]</c> 前缀转发宿主日志；就绪后输出联调摘要（Vite URL + 代理目标）。
/// 退出时 <see cref="Stop"/> 终止整个进程树（npm → node vite）。
/// </remarks>
internal static class ViteDevHost
{
    private const string DefaultTemplateRelativePath = "templates/web-plugin-ui";

    private static Process? _process;
    private static ILogger? _logger;
    private static bool _readyLogged;

    /// <summary>
    /// 启动 Vite dev server。非阻塞：进程后台运行，输出异步转发。
    /// 失败（目录无效 / npm 缺失）只记录日志，不影响宿主主流程。
    /// </summary>
    /// <param name="explicitDir">
    /// <c>--web-vite=&lt;dir&gt;</c> 显式指定的工程目录；null 时按缺省规则搜索
    /// （环境变量 <c>LYBOX_VITE_DIR</c>，或从 CWD / 程序集目录向上查找 templates/web-plugin-ui）。
    /// </param>
    /// <param name="logger">宿主日志器。</param>
    public static void Start(string? explicitDir, ILogger? logger)
    {
        _logger = logger;
        var workingDir = ResolveWorkingDirectory(explicitDir);
        if (workingDir is null)
        {
            logger?.LogError(
                "Vite 工程目录未找到（--web-vite 参数或缺省搜索 templates/web-plugin-ui 均未命中），Vite 托管未启动");
            return;
        }

        if (!File.Exists(Path.Combine(workingDir, "package.json")))
        {
            logger?.LogError("目录 {Dir} 缺少 package.json，不是有效的 Vite 工程", workingDir);
            return;
        }

        var hostPort = Environment.GetEnvironmentVariable("LYBOX_WEB_PORT") ?? "(随机)";
        try
        {
            var info = new ProcessStartInfo
            {
                // Windows 下 npm 是 npm.cmd，须经 cmd.exe 启动；Unix 直接 npm
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                Arguments = OperatingSystem.IsWindows() ? "/c npm run dev" : "run dev",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _process = Process.Start(info);
            if (_process is null)
            {
                logger?.LogError("Vite 进程启动失败（Process.Start 返回 null）");
                return;
            }

            logger?.LogInformation(
                "Vite dev server 托管启动：目录 {Dir}，宿主端口 {Port}（LYBOX_WEB_PORT 自动联动）",
                workingDir, hostPort);

            _process.OutputDataReceived += (_, e) => OnOutput(e.Data, isError: false);
            _process.ErrorDataReceived += (_, e) => OnOutput(e.Data, isError: true);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
                logger?.LogInformation("Vite dev server 已退出（ExitCode={ExitCode}）", _process.ExitCode);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Vite dev server 启动失败（请确认 node/npm 已安装并在 PATH 中）");
            _process = null;
        }
    }

    /// <summary>终止托管的 Vite 进程树。幂等；未运行时为空操作。</summary>
    public static void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null || process.HasExited)
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // npm → node(vite) 是子进程树，须整树终止，否则 vite 残留占用 5173 端口
                using var killer = Process.Start(new ProcessStartInfo("taskkill", $"/PID {process.Id} /T /F")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                killer?.WaitForExit(5000);
            }
            else
            {
                process.Kill(entireProcessTree: true);
            }
            _logger?.LogInformation("Vite dev server 已随宿主退出终止");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Vite 进程树终止失败（可能已自行退出）");
        }
    }

    /// <summary>
    /// 解析 Vite 工程目录：显式参数（相对 CWD）优先；其次环境变量 <c>LYBOX_VITE_DIR</c>；
    /// 最后从 CWD 与程序集基目录向上逐级查找 <c>templates/web-plugin-ui</c>（最多 6 级，
    /// 覆盖 dotnet run 的 CWD=项目目录 与 仓库根 两种启动位置）。均未命中返回 null。
    /// </summary>
    internal static string? ResolveWorkingDirectory(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return Directory.Exists(Path.GetFullPath(explicitDir)) ? Path.GetFullPath(explicitDir) : null;

        var env = Environment.GetEnvironmentVariable("LYBOX_VITE_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(Path.GetFullPath(env)))
            return Path.GetFullPath(env);

        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var candidate = root;
            for (var i = 0; i < 6 && candidate is not null; i++)
            {
                var probe = Path.Combine(candidate, DefaultTemplateRelativePath);
                if (Directory.Exists(probe))
                    return probe;
                candidate = Directory.GetParent(candidate)?.FullName;
            }
        }
        return null;
    }

    /// <summary>Vite 就绪输出行判定（"Local:" 或 "ready in"，Vite 5 输出格式）。</summary>
    internal static bool IsViteReadyLine(string? line) =>
        line is not null
        && (line.Contains("Local:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ready in", StringComparison.OrdinalIgnoreCase));

    private static void OnOutput(string? line, bool isError)
    {
        if (line is null)
            return;

        var text = $"[vite] {line}";
        if (isError)
            Console.Error.WriteLine(text);
        else
            Console.Out.WriteLine(text);

        if (!_readyLogged && IsViteReadyLine(line))
        {
            _readyLogged = true;
            var hostPort = Environment.GetEnvironmentVariable("LYBOX_WEB_PORT") ?? "(随机)";
            _logger?.LogInformation(
                "Vite 就绪：浏览器打开 http://localhost:5173 开发（完整 HMR）；RPC/SSE 经同源代理访问宿主 127.0.0.1:{Port}；宿主退出时 Vite 自动终止",
                hostPort);
        }
    }
}
