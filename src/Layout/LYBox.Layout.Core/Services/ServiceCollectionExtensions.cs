using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using LYBox.Layout.Core.Data;
using LYBox.Layout.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

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
        var logPath = Path.Combine(AppContext.BaseDirectory, WellKnownPaths.LogsSubDir);
        Directory.CreateDirectory(logPath);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

            // 结构化日志统一由 Serilog 承接（控制台 + 滚动文件），并保留分类名（SourceContext）、
            // 日志级别、消息模板与 BeginScope 作用域能力。
            // 输出格式见 SerilogLogging.OutputTemplate：
            //   [2026-07-10 07:39:43.123] [Information] [LYBox.Layout.Core.Services.NavigationService] 消息内容
            // 时间戳统一为 UTC；异常信息追加在消息之后（换行 + 异常类型:消息 + 堆栈）。
            // 直接注册 SerilogLoggerProvider（而非 AddSerilog）：AddSerilog 会追加一条
            // 针对该 provider 的 Trace 级过滤器，覆盖上面的全局 MinimumLevel / AddFilter 规则，
            // 导致 Microsoft / EF Core 的 Debug 日志穿透。这里由 DI 工厂创建 provider，
            // 既能保留全局过滤语义，也能在容器释放时同步释放 Serilog 日志器与文件流。
            builder.Services.AddSingleton<ILoggerProvider>(_ =>
                new SerilogLoggerProvider(SerilogLogging.CreateLogger(logPath), dispose: true));
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
            var dbPath = Path.Combine(hostDataRoot, WellKnownPaths.HostDatabaseFileName);
            options.UseSqlite($"Data Source={dbPath}");
        });

        // 插件数据目录提供者:必须在 db factory 之后注册,便于其他服务解析它。
        services.AddSingleton<IPluginDataDirectoryProvider, PluginDataDirectoryProvider>();

        // 跨平台 / 用户目录画像:供插件读取 UserHome / Downloads / Documents / IsPortableMode。
        services.AddSingleton<IRuntimeProfile, RuntimeProfile>();

        // 宿主运行时环境信息（路径 / 日志 / 版本）。插件通过 IPluginHostEnvironment 统一获取。
        // 每个插件一个实例（含独立的 ILoggerFactory），不注册为单例，
        // 而是由插件入口在 RegisterAsync 中按 PluginId 调用工厂创建。
        services.AddSingleton<IPluginHostEnvironmentFactory, PluginHostEnvironmentFactory>();

        services.AddSingleton<DatabaseMigrationService>();

        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddSingleton<IWindowInfoService, WindowInfoService>();

        services.AddLocalization();
        services.AddSingleton<ITaskRegistry, TaskRegistry>();

        return services;
    }
}
