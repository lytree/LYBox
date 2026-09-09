using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Generated;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using LYBox.Plugin.Shared.Web;
using LYBox.Layout.Core.Data;
using LYBox.Layout.Core.Services;
using LYBox.Layout.Ursa.Services;
using LYBox.Layout.Ursa.ViewModels;
using LYBox.Layout.Ursa.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace LYBox.Launcher.Desktop;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }

    // 保存 pluginLoader 引用用于退出时 ShutdownAsync 与 Dispose（Dispose 内部会再次调用 ShutdownAsync，幂等安全）
    private PluginLoader? _pluginLoader;
    private bool _isShuttingDown;

    public App()
    {
        // 全局异常处理：后台线程未观察到的异常
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogGlobalException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogGlobalException("UnhandledException", ex);
    }

    private static void LogGlobalException(string source, Exception ex)
    {
        try
        {
            var logger = ServiceProvider?.GetRequiredService<ILogger<App>>();
            logger?.LogError(ex, "[全局异常] {Source}: {Message}", source, ex.Message);
        }
        catch
        {
            Console.Error.WriteLine($"[全局异常] {source}: {ex}");
        }
    }

    private static void OnUIThreadUnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogGlobalException("UIThreadUnhandledException", e.Exception);
        Console.Error.WriteLine($"[UIThreadUnhandledException] {e.Exception}");
#if DEBUG
        // DEBUG 模式下不吞异常，让问题暴露
        e.Handled = false;
#else
        e.Handled = true;
#endif
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
        // 启动序列整体收敛到单一 async 入口，消除分散的 sync-over-async。
        // 安全说明：Avalonia 的 Application.Initialize() 在 UI 消息循环启动前执行（MainWindow
        // 尚未创建），此时 UI 线程无嵌套事件泵，因此单点 .GetResult() 阻塞不会死锁；
        // 插件生命周期方法（Discover/Initialize/Register）均不触碰 UI 控件，await 续体可安全执行。
        InitializeCoreAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeCoreAsync()
    {
        var services = new ServiceCollection();
        services.AddAvaloniaServices();
        // 注册 Ursa 宿主层服务：NavigationService / MenuConfigurationService / LocalizationService
        services.AddUrsaServices();

        // 阶段1：发现所有插件程序集，创建 IPlugin 实例
        var pluginLoader = new PluginLoader();
        _pluginLoader = pluginLoader;
        await pluginLoader.DiscoverAllPluginAssembliesAsync();

        // 阶段2：调用插件 InitializeAsync(IServiceCollection)，注册 DI 服务
        await pluginLoader.InitializeAllPluginsAsync(services);

        // 统一注入：将提前实例化的 pluginLoader 注册到 DI（避免双重注册产生孤立实例）
        services.AddSingleton<PluginLoader>(pluginLoader);
        services.AddSingleton<IPluginLoader>(pluginLoader);

        // 注册嵌入式 HTTP 资源服务（单例，随 ServiceProvider.Dispose 自动停止）
        services.AddSingleton<WebHostService>();

        ServiceProvider = services.BuildServiceProvider();
        ServiceLocator.Initialize(ServiceProvider);

        // 注入 logger 到 PluginLoader（构造期使用 NullLogger）
        PluginLoader.SetLogger(ServiceProvider.GetRequiredService<ILogger<PluginLoader>>());

        // 显式连接 NavigationService 与 PluginLoader（原嵌入在 DI 工厂中的副作用，移出以保证时序确定）
        if (ServiceProvider.GetRequiredService<INavigationService>() is LYBox.Layout.Ursa.Services.NavigationService ursaNav)
            ursaNav.AttachPluginLoader(pluginLoader);

        // 记录应用启动日志
        var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
        logger.ZLogInformation($"AvaloniaTemplate 应用启动");

        InitializeDatabase();
        InitializeLocalization();

        // 阶段3：调用插件 RegisterAsync(IServiceProvider)，执行多语言注册等
        await pluginLoader.RegisterAllPluginsAsync(ServiceProvider);

        // S2：宿主统一注册 Web 插件（依据清单 kind=Web），无需插件手动 MapPluginRoot
        var webHost = ServiceProvider.GetRequiredService<WebHostService>();
        pluginLoader.RegisterWebPlugins(webHost);

        await InitializeWebHostAsync();
        RegisterPluginNavigationAndMenus(pluginLoader);

        DataContext = new ApplicationViewModel();
    }

    private void InitializeLocalization()
    {
        if (ServiceLocator.TryGetService<ILocalizationService>(out var loc) && loc is not null)
        {
            var settingsService = ServiceProvider?.GetRequiredService<ISettingsService>();
            var savedLocale = settingsService?.GetValue("App.Locale");
            var culture = !string.IsNullOrEmpty(savedLocale) && !"Default".Equals(savedLocale)
                ? new System.Globalization.CultureInfo(savedLocale)
                : System.Globalization.CultureInfo.CurrentUICulture;
            loc.SetCulture(culture);
        }
    }

    private void InitializeDatabase()
    {
        var dbFactory = ServiceProvider?.GetRequiredService<IDbContextFactory<AppDbContext>>();
        if (dbFactory == null) return;

        // 通过 EF Core Migrations 演进数据库 schema（替代 EnsureCreated，支持后续增量迁移）
        ServiceProvider?.GetService<DatabaseMigrationService>()?.Migrate();

        var settingsService = ServiceProvider?.GetRequiredService<ISettingsService>() as SettingsService;
        settingsService?.InitializeDefaults();
    }

    /// <summary>
    /// 懒加载初始化嵌入式 HTTP 静态资源服务。Web 插件由宿主 <see cref="PluginLoader.RegisterWebPlugins"/>
    /// 统一注册其 wwwroot（S2 BC-3）；此处仅当存在已注册的 Web 插件时才初始化并启动服务，
    /// 否则直接跳过，不占用端口。启动失败不阻塞应用（Web 插件功能降级）。
    /// 在 <see cref="PluginLoader.RegisterAllPluginsAsync"/> 与 RegisterWebPlugins 之后、
    /// <see cref="RegisterPluginNavigationAndMenus"/> 之前调用，确保 Web 插件页面导航时 HTTP 服务已可用。
    /// </summary>
    private async Task InitializeWebHostAsync()
    {
        try
        {
            var webHost = ServiceProvider?.GetRequiredService<WebHostService>();
            if (webHost is null) return;

            // 懒加载：仅当插件显式注册了 Web 资源时才初始化并启用静态资源服务，否则保持关闭
            if (!webHost.HasRegisteredPlugins)
            {
                var skipLogger = ServiceProvider?.GetRequiredService<ILogger<App>>();
                skipLogger?.LogInformation("无插件显式注册 Web 资源，静态资源服务保持关闭（懒加载）");
                return;
            }

            // 已有插件注册路由，启动 Kestrel
            await webHost.StartAsync();

            var bootLogger = ServiceProvider?.GetRequiredService<ILogger<App>>();
            if (webHost.IsRunning)
                bootLogger?.LogInformation("WebHostService 已启动，监听 {BaseUrl}", webHost.BaseUrl);
            else
                bootLogger?.LogInformation("WebHostService 启动未生效，监听端口未就绪");
        }
        catch (Exception ex)
        {
            var logger = ServiceProvider?.GetRequiredService<ILogger<App>>();
            logger?.LogError(ex, "WebHostService 启动失败，Web 插件功能将不可用");
            // 不重新抛出：HTTP 服务失败不应阻塞传统插件与宿主 UI
        }
    }

    private void RegisterPluginNavigationAndMenus(IPluginLoader pluginLoader)
    {
        var navigationService = ServiceProvider?.GetRequiredService<INavigationService>();
        var menuConfigurationService = ServiceProvider?.GetRequiredService<IMenuConfigurationService>();

        if (navigationService == null || menuConfigurationService == null)
            return;

        // 修复 #12：原 catch 仅 Console.WriteLine，未持久化插件错误状态，UI 上仍显示为已加载，
        // 用户无法感知插件故障。改为：失败时调用 MarkPluginError 持久化状态，并记录结构化日志。
        var logger = ServiceProvider?.GetRequiredService<ILogger<App>>();

        foreach (var pluginInfo in pluginLoader.GetInstalledPlugins())
        {
            if (pluginInfo.State != PluginState.Loaded)
                continue;

            try
            {
                var plugin = pluginLoader.GetLoadedPlugin(pluginInfo.PluginId);
                if (plugin == null) continue;

                // 合并插件自定义图标资源到 Application.Resources。
                // 必须在 RegisterNavigations / RegisterMenuItems 之前执行，
                // 以保证后续 XAML 通过 {DynamicResource} 引用插件图标时能命中。
                var iconResources = plugin.GetIconResources();
                if (iconResources is not null && Application.Current is { } app)
                {
                    app.Resources.MergedDictionaries.Add(iconResources);
                }

                // 双轨收敛：UI（视图/导航/菜单）唯一注册轨道为 IGeneratedPluginModule.Ui 描述符。
                // 模块缺失 = 插件未走 [GenerateMetadata] 源生成器轨道，无生成 UI 可注册。
                var module = pluginLoader.GetGeneratedModule(pluginInfo.PluginId);
                if (module is null) continue;
                var ui = module.Ui;
                var services = ServiceProvider!;

                navigationService.RegisterNavigations(ui.ToNavigationFactories(services), pluginInfo.PluginId);
                menuConfigurationService.RegisterMenuItems(ui.ToMenuItems());

                ViewLocator.RegisterModule(module, services);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "注册插件 {PluginId} 导航/菜单失败", pluginInfo.PluginId);
                pluginLoader.MarkPluginError(pluginInfo.PluginId, $"Registration failed: {ex.Message}");
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 全局异常处理：UI 线程未处理异常
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += OnUIThreadUnhandledException;

            if (LYBox.Launcher.Desktop.Program.NoSplash)
            {
                // --no-splash：跳过闪屏，直接显示主窗口
                desktop.MainWindow = new MainWindow()
                {
                    DataContext = new MainWindowViewModel()
                };
            }
            else
            {
                desktop.MainWindow = new MvvmSplashWindow()
                {
                    DataContext = new SplashViewModel()
                };
            }

            // 退出时检测是否有正在运行的任务
            desktop.ShutdownRequested += OnShutdownRequested;

            InitializeTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 创建系统托盘图标（跨平台，使用 Avalonia 12.1 内置 TrayIcon）。
    /// 图标加载自 LYBox.Layout.Ursa 的 AvaloniaResource：avares://LYBox.Layout.Ursa/Assets/lybox.ico。
    /// 菜单项命令挂接到 ApplicationViewModel 的 ShowMainWindow/ExitApplication 命令。
    /// Avalonia 在应用退出时自动 Dispose 托盘图标，无需手动清理。
    /// </summary>
    private void InitializeTrayIcon()
    {
        var loc = ServiceLocator.TryGetService<ILocalizationService>(out var locSvc) ? locSvc : null;
        var tooltip = loc?.GetString("TRAY_TOOLTIP", "LYBox") ?? "LYBox";
        var showText = loc?.GetString("TRAY_SHOW_WINDOW", "Show Window") ?? "Show Window";
        var exitText = loc?.GetString("TRAY_EXIT", "Exit") ?? "Exit";

        var vm = DataContext as ApplicationViewModel;

        WindowIcon? trayWindowIcon = null;
        try
        {
            var iconUri = new Uri("avares://LYBox.Layout.Ursa/Assets/lybox.ico");
            using var stream = AssetLoader.Open(iconUri);
            trayWindowIcon = new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            // 图标加载失败不阻塞托盘创建（ToolTipText 仍可见）
            Console.WriteLine($"Failed to load tray icon: {ex.Message}");
        }

        var trayIcon = new TrayIcon
        {
            Icon = trayWindowIcon,
            ToolTipText = tooltip,
            IsVisible = true,
            Menu = new NativeMenu
            {
                new NativeMenuItem(showText) { Command = vm?.ShowMainWindowCommand },
                new NativeMenuItemSeparator(),
                new NativeMenuItem(exitText) { Command = vm?.ExitApplicationCommand }
            }
        };

        var icons = new TrayIcons { trayIcon };
        TrayIcon.SetIcons(this, icons);
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        e.Cancel = true;

        if (ServiceLocator.TryGetService<ITaskRegistry>(out var registry) && registry.HasRunningTasks)
        {
            var tasks = registry.GetRunningTasks();
            var taskNames = string.Join(", ", tasks.Select(t => t.TaskName));
            var logger = ServiceProvider?.GetRequiredService<ILogger<App>>();
            logger?.LogWarning("应用退出时仍有正在运行的任务: {Tasks}", taskNames);
        }

        // 修复：异步清理 + 超时兜底，避免原生资源释放（TdLib/ZLogger）阻塞导致进程无法退出
        // 先取消关闭请求，然后在线程池线程上执行清理，完成后调用 Environment.Exit 强制退出
        _ = Task.Run(async () =>
        {
            var cleanupTask = PerformCleanupAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var completed = await Task.WhenAny(cleanupTask, timeoutTask);

            if (completed == timeoutTask)
            {
                Console.Error.WriteLine("[Shutdown] Cleanup timed out after 10s, forcing exit.");
            }

            Environment.Exit(0);
        });
    }

    private async Task PerformCleanupAsync()
    {
        try
        {
            if (_pluginLoader is not null)
            {
                await _pluginLoader.ShutdownAllPluginsAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ShutdownAllPluginsAsync failed on exit: {ex.Message}");
        }

        try
        {
            (ServiceProvider as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ServiceProvider.Dispose failed on exit: {ex.Message}");
        }

        try
        {
            _pluginLoader?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginLoader.Dispose failed on exit: {ex.Message}");
        }

        // 取消订阅全局异常处理，避免在 ServiceProvider Dispose 后日志器失效导致异常处理再抛异常
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException -= OnUIThreadUnhandledException;
    }
}
