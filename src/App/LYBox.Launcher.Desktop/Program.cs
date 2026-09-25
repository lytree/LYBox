using Avalonia;
using Avalonia.Dialogs;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LYBox.Plugin.Shared;

namespace LYBox.Launcher.Desktop;

/// <summary>
/// 桌面启动入口。类型为 public 以允许控制台调试版（LYBox.Launcher.Console）复用
/// <see cref="BuildAvaloniaApp"/> 启动同一套应用。
/// </summary>
public sealed partial class Program
{
    private const string ConsoleModeArgument = "--console";

    public static string[]? LaunchArgs { get; private set; }

    public static bool NoSplash => HasArg("--no-splash");
    public static bool CollapsedSidebar => HasArg("--collapsed-sidebar");

    /// <summary>
    /// 是否显式启用 Web 插件开发工具栏（Release 构建下生效；DEBUG 构建默认开启，无需此参数）。
    /// </summary>
    public static bool WebDev => HasArg("--web-dev");

    /// <summary>
    /// WebView 远程调试端口（<c>--web-devtools[=port]</c>，默认 9222）。null = 未启用。
    /// 启用后可在 Chromium 系浏览器连接 <c>http://127.0.0.1:{port}</c>，对 WebView 内页面断点调试。
    /// </summary>
    public static int? WebDevToolsPort { get; private set; }

    /// <summary>
    /// Vite 开发服务器托管目录（<c>--web-vite[=dir]</c>）。null = 未启用。
    /// 启用后由宿主拉起 <c>npm run dev</c> 并托管其生命周期（退出时终止进程树），
    /// 端口经 <c>LYBOX_WEB_PORT</c> 环境变量与 WebHostService 自动联动。
    /// </summary>
    public static bool WebViteEnabled { get; private set; }

    /// <summary><c>--web-vite=&lt;dir&gt;</c> 显式指定的 Vite 工程目录；未指定时 ViteDevHost 按缺省规则搜索。</summary>
    public static string? WebViteDir { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var consoleMode = args.Any(argument =>
            string.Equals(argument, ConsoleModeArgument, StringComparison.OrdinalIgnoreCase));
        var applicationArgs = consoleMode
            ? args.Where(argument => !string.Equals(argument, ConsoleModeArgument, StringComparison.OrdinalIgnoreCase)).ToArray()
            : args;

        // LaunchArgs 必须先于 HasArg/GetArgValue 消费者赋值（含下方的环境预处理）
        LaunchArgs = applicationArgs;

        // 极早期 dump：DI / logger 都还没起来前，把最关键的路径打到 stderr，
        // 这样即使后续初始化失败也能在 stderr 中看到环境上下文。
        DumpEarlyPaths(args);

        // WebView2 远程调试开关必须在首个 WebView 创建前注入（环境变量方式），故在一切初始化之前处理
        ApplyWebDevToolsEnvironment(args);
        // Vite 托管：确保 LYBOX_WEB_PORT 存在（WebHostService 与 vite.config.ts 代理共用），早于 DI 初始化
        ApplyWebViteEnvironment(args);

        if (consoleMode)
        {
            StartWithConsole(applicationArgs);
            return;
        }

        StartDesktop(applicationArgs);
    }

    /// <summary>
    /// 解析 <c>--web-devtools[=port]</c> 并设置 WebView2 远程调试环境变量。
    /// 仅 Windows WebView2 后端生效；其他平台该环境变量被忽略（无害）。
    /// 追加语义：若 <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> 已有值则在其后追加，不覆盖。
    /// </summary>
    internal static void ApplyWebDevToolsEnvironment(string[] args)
    {
        WebDevToolsPort = ParseDevToolsPort(args);
        if (WebDevToolsPort is not int port)
            return;

        var existing = LYBoxEnv.Get(LYBoxEnv.WebView2DebugArgsKey);
        var debugArg = $"--remote-debugging-port={port}";
        LYBoxEnv.Set(LYBoxEnv.WebView2DebugArgsKey,
            string.IsNullOrEmpty(existing) ? debugArg : $"{existing} {debugArg}");
    }

    /// <summary>
    /// 解析 <c>--web-devtools</c>（默认端口 9222）或 <c>--web-devtools=&lt;port&gt;</c>；
    /// 未指定或端口无效（0/超范围/非数字）返回 null。
    /// </summary>
    internal static int? ParseDevToolsPort(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--web-devtools", StringComparison.OrdinalIgnoreCase))
                return 9222;
            if (arg.StartsWith("--web-devtools=", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(arg.Substring("--web-devtools=".Length), out var port) && port is > 0 and <= 65535
                    ? port
                    : null;
            }
        }
        return null;
    }

    /// <summary>
    /// 解析 <c>--web-vite</c> 启用与 <c>--web-vite=&lt;dir&gt;</c> 目录，并保证 <c>LYBOX_WEB_PORT</c> 存在：
    /// 未显式设置时探测一个空闲端口写入进程级环境变量——WebHostService（读取该变量固定端口）
    /// 与 Vite 子进程（vite.config.ts 读取该变量定位代理目标）共用，实现端口自动联动。
    /// </summary>
    internal static void ApplyWebViteEnvironment(string[] args)
    {
        WebViteDir = GetArgValue("--web-vite");
        WebViteEnabled = WebViteDir is not null || HasArg("--web-vite");
        if (!WebViteEnabled)
            return;

        if (!string.IsNullOrEmpty(LYBoxEnv.Get(LYBoxEnv.WebPortKey)))
            return; // 用户已固定端口，尊重现有设置

        var port = FindFreePort();
        if (port is int p)
            LYBoxEnv.SetWebPort(p);
    }

    /// <summary>探测一个可用 TCP 端口（监听 127.0.0.1:0 后立即释放）。失败返回 null。</summary>
    private static int? FindFreePort()
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch
        {
            return null;
        }
    }

    internal static void StartWithConsole(string[] args)
    {
        var ownsConsole = TryCreateConsole();

        try
        {
            StartDesktop(args);
        }
        finally
        {
            if (ownsConsole)
            {
                FreeConsole();
            }
        }
    }

    private static void StartDesktop(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// 极早期路径 dump：在 DI / logger 还没起来前，把环境变量 / BaseDirectory 等打到 stderr。
    /// 同时把 <c>LYBOX_DATA_ROOT</c> 环境变量解析结果打出来，方便排查插件数据根目录错位。
    /// </summary>
    private static void DumpEarlyPaths(string[] args)
    {
        // 行布局与解析逻辑统一经 PathsReport，避免早期 dump 与 DI 之后启动 banner 各写一份。
        var consoleMode = args.Any(a => string.Equals(a, ConsoleModeArgument, StringComparison.OrdinalIgnoreCase));
        PathsReport.WriteEarlyLines(Console.Error, args, consoleMode);
    }

    private static bool TryCreateConsole()
    {
        if (!OperatingSystem.IsWindows() || !AllocConsole())
        {
            // The process may already be attached to the invoking terminal.
            return false;
        }

        Console.OutputEncoding = Encoding.UTF8;
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        Console.SetOut(output);
        Console.SetError(output);
        return true;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
                .UseManagedSystemDialogs()
                .UsePlatformDetect()
                .With(new Win32PlatformOptions())
                .LogToTrace();
    }

    public static bool HasArg(string name) =>
        LaunchArgs?.Contains(name, StringComparer.OrdinalIgnoreCase) == true;

    public static string? GetArgValue(string prefix)
    {
        if (LaunchArgs == null) return null;
        foreach (var arg in LaunchArgs)
        {
            if (arg.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
                return arg.Substring(prefix.Length + 1);
        }
        return null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}

/// <summary>
/// 供控制台调试版（LYBox.Launcher.Console）调用的启动入口。
/// </summary>
public static class DesktopLauncher
{
    public static void StartWithConsole(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Program.StartWithConsole(args);
    }
}