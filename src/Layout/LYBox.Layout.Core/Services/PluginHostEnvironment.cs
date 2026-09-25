using LYBox.Plugin.Shared.Services;
using Microsoft.Extensions.Logging;

namespace LYBox.Layout.Core.Services;

/// <summary>
/// 宿主侧 <see cref="IPluginHostEnvironment"/> 实现：按插件粒度构造（一个 PluginId 一个实例）。
/// <para>
/// 关键设计：
/// <list type="bullet">
/// <item><see cref="LoggerFactory"/>：每个插件独立的 ZLogger 通道，文件落到
///       <c>{LogsDirectory}/plugins/{PluginId}/app-yyyy-MM-dd_NNN.log</c>，
///       不与宿主主日志混在一起，便于按插件归档 / 排查。</item>
/// <item><see cref="PluginLogsDirectory"/>：当前插件专属日志目录。</item>
/// </para>
/// </summary>
public sealed class PluginHostEnvironment : IPluginHostEnvironment, IDisposable
{
    private readonly ILoggerFactory _pluginLoggerFactory;
    private readonly string _pluginId;

    public string AppBaseDirectory => AppContext.BaseDirectory;
    public string HostDataRoot => PluginDataDirectoryProvider.ResolveHostDataRoot();

    public string LogsDirectory
    {
        get
        {
            var dir = Path.Combine(AppBaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public PluginHostEnvironment(string pluginId)
    {
        _pluginId = pluginId;
        _pluginLoggerFactory = BuildLoggerFactory(pluginId);
    }

    /// <summary>
    /// 该插件独立的 <see cref="ILoggerFactory"/>：
    /// 插件通过它创建的所有 <see cref="ILogger"/> 都会写入
    /// <c>{LogsDirectory}/plugins/{PluginId}/app-yyyy-MM-dd_NNN.log</c>。
    /// </summary>
    public ILoggerFactory LoggerFactory => _pluginLoggerFactory;

    /// <summary>
    /// 当前插件的日志目录：<c>{LogsDirectory}/plugins/{PluginId}/</c>。
    /// 由本类构造时创建并确保存在。
    /// </summary>
    public string PluginLogsDirectory
    {
        get
        {
            var dir = Path.Combine(LogsDirectory, "plugins", SafeDirName(_pluginId));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public string AppVersion
    {
        get
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrEmpty(fvi.ProductVersion)) return fvi.ProductVersion;
                }
            }
            catch { }
            return LYBox.Plugin.Shared.PluginSdkContract.CurrentVersion;
        }
    }

    public bool IsPortableMode =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PluginDataDirectoryProvider.DataRootEnvironmentVariable));

    /// <summary>
    /// 为指定插件构造独立的 <see cref="ILoggerFactory"/>：
    /// 只接 Console + 按插件隔离的 RollingFile，不与宿主主日志混淆。
    /// </summary>
    static ILoggerFactory BuildLoggerFactory(string pluginId)
    {
        var dir = Path.Combine(
            Path.Combine(AppContext.BaseDirectory, "logs"),
            "plugins",
            SafeDirName(pluginId));
        Directory.CreateDirectory(dir);

        var lf = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

            builder.AddZLoggerConsole(options =>
            {
                ConfigureFormatter(options);
            });
            builder.AddZLoggerRollingFile(options =>
            {
                options.FilePathSelector = (dt, seq) =>
                    Path.Combine(dir, $"app-{dt:yyyy-MM-dd}_{seq:000}.log");
                options.RollingInterval = RollingInterval.Day;
                options.RollingSizeKB = 10240;
                ConfigureFormatter(options);
            });
        });
        return lf;
    }

    static void ConfigureFormatter(ZLoggerOptions options)
    {
        options.IncludeScopes = true;
        options.UsePlainTextFormatter(formatter =>
        {
            formatter.SetPrefixFormatter(
                $"[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [{2}] ",
                static (in MessageTemplate template, in LogInfo info) =>
                {
                    template.Format(info.Timestamp.Utc, info.LogLevel, info.Category.Name);
                });
        });
    }

    static string SafeDirName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "_";
        var buf = new char[raw.Length];
        var len = 0;
        foreach (var c in raw)
        {
            buf[len++] = char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_';
        }
        return new string(buf, 0, len);
    }

    public void Dispose()
    {
        // LoggerFactory 实现通常为可释放；NullLoggerFactory 不需要 dispose。
        if (_pluginLoggerFactory is IDisposable d) d.Dispose();
    }
}