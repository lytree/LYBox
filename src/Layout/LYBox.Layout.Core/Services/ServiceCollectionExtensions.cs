using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using LYBox.Layout.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;
using ZLogger.Formatters;
using ZLogger.Providers;

namespace LYBox.Layout.Core.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 LYBox.Layout.Core 提供的核心 DI 服务：日志、插件安装管理、EF Core、设置、窗口信息、任务注册表。
    /// NavigationService / MenuConfigurationService / LocalizationService 已移至 LYBox.Layout.Ursa
    /// （因为它们的实现依赖 Ursa 的 ViewModel/Page/Theme 类型），调用方应额外调用
    /// LYBox.Layout.Ursa.Services.ServiceCollectionExtensions.AddUrsaServices() 注册这些服务。
    /// </summary>
    public static IServiceCollection AddAvaloniaServices(this IServiceCollection services)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logPath);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

            // 控制台与文件使用相同的纯文本前缀格式：
            //   [2026-07-10 15:39:43.123] [INFO] [LYBox.Layout.Core.Services.NavigationService] 消息内容
            // 异常信息由 PlainTextZLoggerFormatter 默认行为追加到消息末尾（换行 + 异常类型:消息 + 堆栈）。
            builder.AddZLoggerConsole(options =>
            {
                ConfigurePlainTextFormatter(options);
            });
            builder.AddZLoggerRollingFile(options =>
            {
                options.FilePathSelector = (dt, seq) =>
                    Path.Combine(logPath, $"app-{dt:yyyy-MM-dd}_{seq:000}.log");
                options.RollingInterval = RollingInterval.Day;
                options.RollingSizeKB = 10240; // 10MB
                ConfigurePlainTextFormatter(options);
            });
        });

        // PluginLoader 由 App.Initialize() 提前实例化（阶段1/2需要 DI 尚未构建时使用），
        // 随后通过 services.AddSingleton(pluginLoader) 注入，此处不再注册以避免产生未使用的孤立实例。

        // 外部只读插件清单只会被扫描一次，供安装管理与插件管理共享，避免重复扫描 manifest。
        services.AddSingleton(
            _ => (IReadOnlyDictionary<string, PluginInfo>)PluginInventoryCatalog.ReadExternalPlugins());
        services.AddSingleton<IPluginInstallationManager>(serviceProvider =>
        {
            var externalPluginIds = serviceProvider
                .GetRequiredService<IReadOnlyDictionary<string, PluginInfo>>()
                .Keys
                .ToHashSet(StringComparer.Ordinal);
            return new PluginInstallationManager(
                serviceProvider.GetRequiredService<IPluginLoader>(),
                readOnlyPluginIds: externalPluginIds);
        });
        services.AddSingleton<IPluginManagementService>(serviceProvider => new PluginManagementService(
            serviceProvider.GetRequiredService<IPluginLoader>(),
            serviceProvider.GetRequiredService<IPluginInstallationManager>(),
            serviceProvider.GetRequiredService<IReadOnlyDictionary<string, PluginInfo>>()));

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            // 宿主共享数据库统一放在 Data/ 根目录下，与各插件数据并列：
            //   %LOCALAPPDATA%/LYBox/appdata.db
            // 历史版本曾位于 AppContext.BaseDirectory（启动器根目录），会因自包含发布/只读权限失败。
            var hostDataRoot = PluginDataDirectoryProvider.ResolveHostDataRoot();
            Directory.CreateDirectory(hostDataRoot);
            var dbPath = Path.Combine(hostDataRoot, "appdata.db");
            options.UseSqlite($"Data Source={dbPath}");
        });

        // 插件数据目录提供者：必须在 db factory 之后注册，便于其他服务解析它。
        services.AddSingleton<IPluginDataDirectoryProvider, PluginDataDirectoryProvider>();

        services.AddSingleton<DatabaseMigrationService>();

        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddSingleton<IWindowInfoService, WindowInfoService>();

        services.AddLocalization();
        services.AddSingleton<ITaskRegistry, TaskRegistry>();

        return services;
    }

    /// <summary>
    /// 配置纯文本日志格式器，统一控制台与文件的输出格式。
    /// 输出示例：[2026-07-10 15:39:43.123] [Information] [LYBox.Layout.Core.Services.NavigationService] 消息内容
    /// 异常信息自动追加到消息末尾（换行 + 异常类型:消息 + 堆栈 + 内部异常链）。
    /// </summary>
    private static void ConfigurePlainTextFormatter(ZLoggerOptions options)
    {
        options.IncludeScopes = true;
        options.UsePlainTextFormatter(formatter =>
        {
            formatter.SetPrefixFormatter(
                $"[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [{2}] ",
                static (in MessageTemplate template, in LogInfo info) =>
                {
                    template.Format(
                        info.Timestamp.Utc,
                        info.LogLevel,
                        info.Category.Name
                    );
                }
            );
        });
    }
}
