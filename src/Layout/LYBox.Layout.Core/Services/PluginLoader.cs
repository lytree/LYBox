using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Generated;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using LYBox.Plugin.Shared.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LYBox.Layout.Core.Services;

public sealed class PluginLoader : IPluginLoader, IDisposable
{
    public const string ExtraPluginEnvironmentVariableName = "AVALONIA_EXTRA_PLUGINS_PATH";

    private readonly Dictionary<string, PluginEntry> _entries = [];
    private readonly string _pluginsDirectory;
    private readonly string? _extraPluginPath;
    private readonly bool _loadExtraPlugins;
    private readonly bool _persistManifestChanges;
    private readonly object _sync = new();
    private List<PluginInfo>? _cachedPluginList;
    private static ILogger _logger = NullLogger<PluginLoader>.Instance;

    /// <summary>
    /// 升级场景下保留的"上次 SchemaVersion"暂存表。
    /// 在 <see cref="ProcessSinglePendingUpgrade"/> 步骤 2（重命名旧目录之前）写入，
    /// 在 <see cref="LoadAllPluginManifests"/> 之后回填到对应 PluginInfo。
    /// 加载完成后即清空（仅用于单次启动的 in-memory 透传，不持久化）。
    /// </summary>
    private readonly Dictionary<string, string?> _pendingPreviousSchemaVersions = new(StringComparer.Ordinal);

    public event EventHandler<PluginInfo>? PluginLoaded;
    public event EventHandler<PluginInfo>? PluginUnloaded;
    public event EventHandler<PluginInfo>? PluginStateChanged;

    public PluginLoader(string? pluginsDirectory = null)
        : this(pluginsDirectory, seedPlugins: null, loadExtraPlugins: true, persistManifestChanges: true)
    {
    }

    private PluginLoader(
        string? pluginsDirectory,
        IEnumerable<PluginInfo>? seedPlugins,
        bool loadExtraPlugins,
        bool persistManifestChanges)
    {
        _pluginsDirectory = Path.GetFullPath(
            pluginsDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"));
        _loadExtraPlugins = loadExtraPlugins;
        _persistManifestChanges = persistManifestChanges;
        _extraPluginPath = loadExtraPlugins
            ? Environment.GetEnvironmentVariable(ExtraPluginEnvironmentVariableName)
            : null;

        if (seedPlugins is not null)
        {
            foreach (var plugin in seedPlugins)
            {
                if (string.IsNullOrWhiteSpace(plugin.PluginId))
                    throw new ArgumentException("Transient plugins must have a non-empty PluginId.", nameof(seedPlugins));
                if (!_entries.TryAdd(plugin.PluginId, new PluginEntry { Info = plugin }))
                    throw new ArgumentException($"Duplicate transient plugin id '{plugin.PluginId}'.", nameof(seedPlugins));
            }
            return;
        }

        Directory.CreateDirectory(_pluginsDirectory);
        // 顺序：先迁移待升级（可能覆盖 plugins/{PluginId}/ 整个目录），再处理待卸载，
        // 最后扫描 manifests。任何 .pending 目录都不应被后续两步误识别为插件目录
        // （其根目录无 plugin.json，故 LoadAllPluginManifests/ProcessPendingUninstalls 会自动跳过）。
        ProcessPendingUpgrades();
        ProcessPendingUninstalls();
        LoadAllPluginManifests();
        RestorePendingUpgradeStates();
    }

    /// <summary>
    /// Creates an in-memory loader for an already resolved plugin dependency closure.
    /// It does not create directories, scan manifests, process pending operations, load
    /// extra plugins, or persist runtime state back to plugin.json.
    /// </summary>
    public static PluginLoader CreateTransient(
        IEnumerable<PluginInfo> plugins,
        string? pluginsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return new PluginLoader(
            pluginsDirectory,
            plugins,
            loadExtraPlugins: false,
            persistManifestChanges: false);
    }

    /// <summary>
    /// Creates a manifest-backed management loader without running desktop startup work.
    /// Pending upgrades/uninstalls are left untouched, extra plugins are not loaded, and
    /// management state changes are persisted to the supplied plugin manifests.
    /// </summary>
    public static PluginLoader CreateManagement(
        IEnumerable<PluginInfo> plugins,
        string? pluginsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return new PluginLoader(
            pluginsDirectory,
            plugins,
            loadExtraPlugins: false,
            persistManifestChanges: true);
    }

    /// <summary>
    /// 在 ServiceProvider 构建后注入 logger，替换构造期使用的 NullLogger。
    /// PluginLoader 在 DI 容器构建前提前实例化，无法通过构造函数注入 ILogger。
    /// </summary>
    public static void SetLogger(ILogger logger) => _logger = logger;

    public IReadOnlyList<PluginInfo> GetInstalledPlugins()
    {
        lock (_sync)
        {
            return _cachedPluginList ??= _entries.Values.Select(e => e.Info).ToList();
        }
    }

    public PluginInfo? GetPlugin(string pluginId)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(pluginId, out var entry) ? entry.Info : null;
        }
    }

    #region Two-phase loading

    /// <summary>
    /// 阶段1：发现并加载所有插件程序集，创建 IPlugin 实例，但不调用 InitializeAsync。
    /// 插件状态变为 Discovered。
    /// </summary>
    public async Task DiscoverAllPluginAssembliesAsync()
    {
        List<PluginInfo> toLoad;

        lock (_sync)
        {
            toLoad = _entries.Values
                .Where(e => e.Info.State == PluginState.Installed)
                .Select(e => e.Info)
                .ToList();
        }

        foreach (var info in toLoad)
        {
            var result = await DiscoverPluginAssemblyAsync(info);
            if (result.Success)
            {
                PluginStateChanged?.Invoke(this, result.PluginInfo ?? info);
            }
        }

        // 修复：原在 lock(_sync) 内调用同步的 LoadExtraPlugins()，其内部又通过
        // DiscoverPluginAssemblyAsync(...).GetAwaiter().GetResult() 实现 sync-over-async，
        // 在 UI 线程上易导致死锁。改为在锁外异步等待。
        if (_loadExtraPlugins)
            await LoadExtraPluginsAsync();
    }

    /// <summary>
    /// 阶段2：调用每个已发现插件的 InitializeAsync(IServiceCollection)，
    /// 插件在初始化时向 ServiceCollection 注册服务。
    /// 必须在 DiscoverAllPluginAssembliesAsync 之后、BuildServiceProvider 之前调用。
    /// </summary>
    public async Task InitializeAllPluginsAsync(IServiceCollection services)
    {
        List<PluginEntry> toInitialize;

        lock (_sync)
        {
            toInitialize = _entries.Values
                .Where(e => e.Info.State == PluginState.Loaded && e.Plugin is not null && !e.IsInitialized)
                .ToList();
        }

        foreach (var entry in toInitialize)
        {
            List<PluginInfo> eventsToFire = [];

            try
            {
                await entry.Plugin!.InitializeAsync(services);
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    var errInfo = entry.Info.WithState(PluginState.Error, $"Plugin initialization failed: {ex.Message}");
                    var errEntry = GetOrCreateEntry(errInfo.PluginId);
                    errEntry.Info = errInfo;
                    SavePluginManifest(errInfo);
                    InvalidateSnapshot();
                    eventsToFire.Add(errInfo);
                }
                FireEventsOutsideLock(eventsToFire);
                continue;
            }

            lock (_sync)
            {
                entry.IsInitialized = true;
                eventsToFire.Add(entry.Info);
            }

            FireEventsOutsideLock(eventsToFire);
        }
    }

    /// <summary>
    /// 阶段3：调用每个已加载插件的 RegisterAsync()，
    /// 插件在注册时执行多语言注册、SQL 初始化等操作。
    /// 必须在 ServiceProvider 构建完成之后调用。
    /// </summary>
    public async Task RegisterAllPluginsAsync(IServiceProvider serviceProvider)
    {
        List<PluginEntry> toRegister;

        lock (_sync)
        {
            toRegister = _entries.Values
                .Where(e => e.Info.State == PluginState.Loaded && e.Plugin is not null && e.IsInitialized)
                .ToList();
        }

        // 批量本地化注册：所有插件注册期间延迟 LocalizationService 缓存重建，
        // 结束后统一重建一次（O-14），同时自动扫描各插件资源并注册（O-6）。
        var loc = serviceProvider.GetService<ILocalizationService>();
        loc?.BeginBatchRegistration();
        try
        {
            foreach (var entry in toRegister)
            {
                List<PluginInfo> eventsToFire = [];

                try
                {
                    await entry.Plugin!.RegisterAsync(serviceProvider);
                    AutoRegisterLocalization(entry, loc);
                }
                catch (Exception ex)
                {
                    lock (_sync)
                    {
                        var errInfo = entry.Info.WithState(PluginState.Error, $"Plugin registration failed: {ex.Message}");
                        var errEntry = GetOrCreateEntry(errInfo.PluginId);
                        errEntry.Info = errInfo;
                        SavePluginManifest(errInfo);
                        InvalidateSnapshot();
                        eventsToFire.Add(errInfo);
                    }
                    FireEventsOutsideLock(eventsToFire);
                    continue;
                }

                eventsToFire.Add(entry.Info);
                FireEventsOutsideLock(eventsToFire);
            }
        }
        finally
        {
            loc?.EndBatchRegistration();
        }
    }

    /// <summary>
    /// 按命名约定自动注册插件本地化资源：静态类 <c>{PluginNs}.Resources.Strings</c> 暴露
    /// 静态 <c>ResourceManager</c> 属性。扫描失败不阻塞插件注册（本地化降级为显示原始 key）。
    /// </summary>
    private static void AutoRegisterLocalization(PluginEntry entry, ILocalizationService? loc)
    {
        if (loc is null || entry.Plugin is null) return;

        try
        {
            var assembly = entry.Plugin.GetType().Assembly;
            var stringsType = assembly.GetTypes()
                .FirstOrDefault(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name == "Strings"
                    && t.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static) is not null);

            if (stringsType is null) return;

            var rm = stringsType.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (rm is System.Resources.ResourceManager resourceManager)
                loc.RegisterResourceManager(resourceManager);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动注册插件 {PluginId} 本地化资源失败", entry.Info.PluginId);
        }
    }

    /// <summary>
    /// 发现单个插件程序集：加载 Assembly，创建 IPlugin/IPluginMetadata 实例，状态设为 Loaded。
    /// </summary>
    private async Task<PluginLoadResult> DiscoverPluginAssemblyAsync(PluginInfo pluginInfo)
    {
        List<PluginInfo> eventsToFire = [];

        bool entryExisted;
        lock (_sync)
        {
            entryExisted = _entries.ContainsKey(pluginInfo.PluginId);

            if (_entries.TryGetValue(pluginInfo.PluginId, out var existing) && existing.Plugin is not null)
            {
                return new PluginLoadResult
                {
                    Success = true,
                    Plugin = existing.Plugin,
                    Metadata = existing.Metadata,
                    GeneratedModule = existing.Module
                };
            }

            if (pluginInfo.State == PluginState.Disabled || pluginInfo.State == PluginState.PendingUninstall)
            {
                return new PluginLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Plugin is {pluginInfo.State}, cannot load"
                };
            }

            if (!File.Exists(pluginInfo.AssemblyPath))
            {
                var errInfo = pluginInfo.WithState(PluginState.Error, $"Assembly not found: {pluginInfo.AssemblyPath}");
                if (entryExisted)
                {
                    UpdateEntry(errInfo);
                    SavePluginManifest(errInfo);
                    InvalidateSnapshot();
                    eventsToFire.Add(errInfo);
                }
                FireEventsOutsideLock(eventsToFire);
                return new PluginLoadResult { Success = false, ErrorMessage = errInfo.ErrorMessage };
            }

            // SDK 契约版本校验：插件声明所需最低版本，若高于当前宿主链接的 PluginSdkVersion 则拒绝加载。
            // 缺省 MinPluginSdkVersion 视为 "0.0.0"，向后兼容未声明版本要求的旧插件。
            if (!IsPluginSdkCompatible(pluginInfo.MinPluginSdkVersion))
            {
                var required = string.IsNullOrWhiteSpace(pluginInfo.MinPluginSdkVersion) ? "0.0.0" : pluginInfo.MinPluginSdkVersion!;
                var errInfo = pluginInfo.WithState(
                    PluginState.Error,
                    $"Plugin requires Plugin SDK >= {required}, but host provides {PluginSdkContract.CurrentVersion}. " +
                    "Update the host application or contact the plugin author.");
                lock (_sync)
                {
                    if (entryExisted)
                    {
                        UpdateEntry(errInfo);
                        SavePluginManifest(errInfo);
                        InvalidateSnapshot();
                    }
                    eventsToFire.Add(errInfo);
                }
                FireEventsOutsideLock(eventsToFire);
                return new PluginLoadResult { Success = false, ErrorMessage = errInfo.ErrorMessage };
            }
        }

        AssemblyLoadContext loadContext;
        IPlugin? plugin = null;
        IPluginMetadata? metadata = null;
        IGeneratedPluginModule? generatedModule = null;

        try
        {
            loadContext = new PluginLoadContext(pluginInfo.AssemblyPath, pluginInfo.SharedAssemblies);
            var assembly = loadContext.LoadFromAssemblyPath(pluginInfo.AssemblyPath);
            generatedModule = GeneratedPluginModuleResolver.Resolve(assembly);
            if (generatedModule is not null)
            {
                plugin = generatedModule.CreatePlugin();
                metadata = generatedModule.Metadata;
                if (plugin.GetType() != generatedModule.PluginType)
                    throw new InvalidOperationException("Generated plugin module returned an unexpected plugin type.");
            }
            else
            {
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;

                    if (plugin is null && typeof(IPlugin).IsAssignableFrom(type))
                    {
                        plugin = (IPlugin)Activator.CreateInstance(type)!;
                        metadata ??= plugin as IPluginMetadata;
                    }
                    else if (metadata is null && typeof(IPluginMetadata).IsAssignableFrom(type))
                    {
                        metadata = (IPluginMetadata)Activator.CreateInstance(type)!;
                    }

                    if (plugin is not null && metadata is not null) break;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                var errInfo = pluginInfo.WithState(PluginState.Error, $"Failed to load plugin: {ex.Message}");
                if (entryExisted)
                {
                    UpdateEntry(errInfo);
                    SavePluginManifest(errInfo);
                    InvalidateSnapshot();
                    eventsToFire.Add(errInfo);
                }
            }
            FireEventsOutsideLock(eventsToFire);
            return new PluginLoadResult { Success = false, ErrorMessage = pluginInfo.ErrorMessage };
        }

        if (plugin == null)
        {
            loadContext.Unload();
            PluginInfo errInfo;
            lock (_sync)
            {
                errInfo = pluginInfo.WithState(PluginState.Error, "No IPlugin implementation found in assembly");
                if (entryExisted)
                {
                    UpdateEntry(errInfo);
                    SavePluginManifest(errInfo);
                    InvalidateSnapshot();
                    eventsToFire.Add(errInfo);
                }
            }
            FireEventsOutsideLock(eventsToFire);
            return new PluginLoadResult { Success = false, ErrorMessage = errInfo.ErrorMessage };
        }

        lock (_sync)
        {
            var entry = GetOrCreateEntry(pluginInfo.PluginId);
            entry.Context = loadContext;
            entry.Plugin = plugin;
            entry.Metadata = metadata;
            entry.Module = generatedModule;
            if (metadata != null)
            {
                pluginInfo = pluginInfo.WithMetadata(true);
            }

            pluginInfo = pluginInfo.WithState(PluginState.Loaded);
            entry.Info = pluginInfo;
            entry.IsInitialized = false;
            SavePluginManifest(pluginInfo);
            InvalidateSnapshot();

            return new PluginLoadResult { Success = true, Plugin = plugin, Metadata = metadata, GeneratedModule = generatedModule, PluginInfo = pluginInfo };
        }
    }

    #endregion

    #region Legacy full-load (kept for IPluginLoader compatibility)

    /// <summary>
    /// IPluginLoader 显式实现：完整加载流程（不带 ServiceCollection）。
    /// </summary>
    Task IPluginLoader.LoadAllPluginsAsync() => LoadAllPluginsAsync(null);

    /// <summary>
    /// IPluginLoader 显式实现：完整加载单个插件（不带 ServiceCollection）。
    /// </summary>
    Task<PluginLoadResult> IPluginLoader.LoadPluginAsync(PluginInfo pluginInfo) => LoadPluginAsync(pluginInfo, null);

    /// <summary>
    /// 完整加载流程：发现 + 初始化 + 注册。
    /// </summary>
    public async Task LoadAllPluginsAsync(IServiceCollection? services = null)
    {
        await DiscoverAllPluginAssembliesAsync();

        if (services is not null)
        {
            await InitializeAllPluginsAsync(services);
        }

        // 触发 PluginLoaded 事件
        List<PluginInfo> loaded;
        lock (_sync)
        {
            loaded = _entries.Values
                .Where(e => e.Info.State == PluginState.Loaded)
                .Select(e => e.Info)
                .ToList();
        }

        foreach (var info in loaded)
        {
            PluginLoaded?.Invoke(this, info);
        }
    }

    /// <summary>
    /// 完整加载单个插件：发现 + 初始化 + 注册。
    /// </summary>
    public async Task<PluginLoadResult> LoadPluginAsync(PluginInfo pluginInfo, IServiceCollection? services = null)
    {
        var result = await DiscoverPluginAssemblyAsync(pluginInfo);
        if (!result.Success) return result;

        // 立即初始化
        var discoveredPlugin = result.Plugin;
        if (discoveredPlugin == null) return result;

        List<PluginInfo> eventsToFire = [];

        try
        {
            await discoveredPlugin.InitializeAsync(services ?? new ServiceCollection());
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                var errInfo = pluginInfo.WithState(PluginState.Error, $"Plugin initialization failed: {ex.Message}");
                UpdateEntry(errInfo);
                SavePluginManifest(errInfo);
                InvalidateSnapshot();
                eventsToFire.Add(errInfo);
            }
            FireEventsOutsideLock(eventsToFire);
            return new PluginLoadResult { Success = false, ErrorMessage = $"Plugin initialization failed: {ex.Message}" };
        }

        lock (_sync)
        {
            var entry = GetOrCreateEntry(pluginInfo.PluginId);
            entry.IsInitialized = true;

            return new PluginLoadResult { Success = true, Plugin = discoveredPlugin, Metadata = result.Metadata };
        }
    }

    #endregion

    /// <summary>将插件标记为禁用并持久化。返回是否真正改变了禁用状态（插件不存在或已禁用时返回 false）。</summary>
    public bool DisablePlugin(string pluginId)
    {
        PluginInfo? info = null;

        lock (_sync)
        {
            if (!_entries.TryGetValue(pluginId, out var entry)) return false;
            if (entry.Info.State == PluginState.Disabled) return false;

            info = entry.Info.WithState(PluginState.Disabled);
            entry.Info = info;
            SavePluginManifest(info);
            InvalidateSnapshot();
        }

        PluginUnloaded?.Invoke(this, info);
        PluginStateChanged?.Invoke(this, info);
        return true;
    }

    public bool EnablePlugin(string pluginId)
    {
        // 与 DisablePlugin 对称：仅修改状态字段并持久化到 manifest，下次启动时按新状态加载。
        // AGENTS.md 声明"当前项目不支持运行时插件增删"，热加载会违反约束且导航/菜单/视图不会被注册。
        PluginInfo? info = null;

        lock (_sync)
        {
            if (!_entries.TryGetValue(pluginId, out var entry)) return false;
            if (entry.Info.State != PluginState.Disabled) return false;

            info = entry.Info.WithState(PluginState.Installed);
            entry.Info = info;
            SavePluginManifest(info);
            InvalidateSnapshot();
        }

        if (info is not null)
        {
            PluginStateChanged?.Invoke(this, info);
        }
        return true;
    }

    public void MarkForUninstall(string pluginId)
    {
        PluginInfo? info = null;

        lock (_sync)
        {
            if (!_entries.TryGetValue(pluginId, out var entry)) return;
            if (entry.Info.IsBuiltIn) return;

            info = entry.Info.WithState(PluginState.PendingUninstall);
            entry.Info = info;
            SavePluginManifest(info);
            InvalidateSnapshot();
        }

        if (info is not null)
        {
            PluginUnloaded?.Invoke(this, info);
            PluginStateChanged?.Invoke(this, info);
        }
    }

    public void RegisterPlugin(PluginInfo pluginInfo)
    {
        lock (_sync)
        {
            UpdateEntry(pluginInfo);
            SavePluginManifest(pluginInfo);
            InvalidateSnapshot();
        }
    }

    public void UnregisterPlugin(string pluginId)
    {
        lock (_sync)
        {
            _entries.Remove(pluginId);
            InvalidateSnapshot();
            DeletePluginManifest(pluginId);
        }
    }

    public IPlugin? GetLoadedPlugin(string pluginId)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(pluginId, out var entry) ? entry.Plugin : null;
        }
    }

    public IPluginMetadata? GetLoadedMetadata(string pluginId)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(pluginId, out var entry) ? entry.Metadata : null;
        }
    }

    public IGeneratedPluginModule? GetGeneratedModule(string pluginId)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(pluginId, out var entry) ? entry.Module : null;
        }
    }

    /// <summary>
    /// S2 BC-3：宿主统一注册 Web 插件。输入为清单（<see cref="PluginInfo.Kind"/> == "Web"
    /// 且 <see cref="PluginState.Loaded"/>），由宿主拼接 <c>{InstallPath}/{web.wwwroot}</c>
    /// 后调用 <see cref="WebHostService.MapPluginRoot"/>。
    /// 插件代码不再包含任何注册调用（声明即注册，消除"忘写注册"类错误）。
    /// 必须在 <see cref="InitializeAllPluginsAsync"/> / <see cref="RegisterAllPluginsAsync"/>
    /// 之后、WebHostService.StartAsync 之前调用。
    /// </summary>
    public void RegisterWebPlugins(WebHostService webHost)
    {
        ArgumentNullException.ThrowIfNull(webHost);

        List<PluginEntry> snapshot;
        lock (_sync)
        {
            snapshot = _entries.Values
                .Where(e => string.Equals(e.Info.Kind, "Web", StringComparison.OrdinalIgnoreCase)
                    && e.Info.State == PluginState.Loaded)
                .ToList();
        }

        foreach (var entry in snapshot)
        {
            try
            {
                var wwwrootName = entry.Info.Web?.Wwwroot ?? "wwwroot";
                var wwwrootPath = Path.Combine(entry.Info.InstallPath, wwwrootName);
                webHost.MapPluginRoot(entry.Info.PluginId, wwwrootPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册 Web 插件 {PluginId} 的 wwwroot 失败", entry.Info.PluginId);
            }
        }
    }

    private PluginEntry GetOrCreateEntry(string pluginId)
    {
        if (!_entries.TryGetValue(pluginId, out var entry))
        {
            entry = new PluginEntry { Info = new PluginInfo { PluginId = pluginId } };
            _entries[pluginId] = entry;
        }
        return entry;
    }

    private void UpdateEntry(PluginInfo pluginInfo)
    {
        var entry = GetOrCreateEntry(pluginInfo.PluginId);
        entry.Info = pluginInfo;
    }

    private void InvalidateSnapshot()
    {
        _cachedPluginList = null;
    }

    private void FireEventsOutsideLock(List<PluginInfo> events)
    {
        foreach (var info in events)
        {
            PluginStateChanged?.Invoke(this, info);
        }
    }

    private async Task LoadExtraPluginsAsync()
    {
        if (string.IsNullOrWhiteSpace(_extraPluginPath) || !Directory.Exists(_extraPluginPath))
            return;

        var dllPaths = new List<string>(
            Directory.GetFiles(_extraPluginPath, "*.dll", SearchOption.TopDirectoryOnly));

        foreach (var subDir in Directory.GetDirectories(_extraPluginPath))
        {
            var dirName = Path.GetFileName(subDir);
            var candidateDll = Path.Combine(subDir, $"{dirName}.dll");
            if (File.Exists(candidateDll))
            {
                dllPaths.Add(candidateDll);
            }
        }

        foreach (var dllPath in dllPaths)
        {
            await TryLoadExtraPluginDllAsync(dllPath);
        }
    }

    private async Task TryLoadExtraPluginDllAsync(string dllPath)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(dllPath);
            var pluginId = assemblyName.Name ?? Path.GetFileNameWithoutExtension(dllPath);

            lock (_sync)
            {
                if (_entries.TryGetValue(pluginId, out var existing) && existing.Plugin is not null)
                    return;
            }

            var pluginInfo = new PluginInfo
            {
                PluginId = pluginId,
                Name = assemblyName.Name ?? pluginId,
                Version = assemblyName.Version?.ToString() ?? "0.0.0",
                AssemblyPath = dllPath,
                InstallPath = Path.GetDirectoryName(dllPath) ?? _extraPluginPath!,
                State = PluginState.Installed,
                IsBuiltIn = false
            };

            await DiscoverPluginAssemblyAsync(pluginInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load extra plugin from '{DllPath}'", dllPath);
        }
    }

    /// <summary>
    /// 应用启动早期：扫描 plugins/.pending/*.upgrade.json，逐个完成"先复制后删除"安全迁移。
    /// 实现依据：docs/Plugin-Upgrade-Evaluation.md
    ///   - 潜在问题 2：先复制后删除，每步失败可回滚
    ///   - 潜在问题 3：多插件同时待升级，逐个处理，单个失败不影响其他
    ///   - 潜在问题 5：迁移前再次 IsPluginSdkCompatible 校验，不兼容则保留旧版本
    ///   - 潜在问题 6：.upgrade.json 存在但 .new/ 缺失 → 告警并清理
    ///   - 潜在问题 4：状态合并（PreserveState=true 时迁移旧 manifest 的 State 到新 manifest）
    /// </summary>
    private void ProcessPendingUpgrades()
    {
        var pendingDir = Path.Combine(_pluginsDirectory, ".pending");
        if (!Directory.Exists(pendingDir)) return;

        foreach (var upgradeJson in Directory.GetFiles(pendingDir, "*.upgrade.json"))
        {
            try
            {
                ProcessSinglePendingUpgrade(upgradeJson, pendingDir);
            }
            catch (Exception ex)
            {
                // 单个失败不影响其他插件
                _logger.LogError(ex, "[PluginUpgrade] Failed to process '{UpgradeJson}'", upgradeJson);
            }
        }
    }

    private void ProcessSinglePendingUpgrade(string upgradeJsonPath, string pendingDir)
    {
        var json = File.ReadAllText(upgradeJsonPath);
        var info = JsonSerializer.Deserialize<PendingUpgradeInfo>(json, PluginUtilities.JsonOptions);
        if (info == null || string.IsNullOrEmpty(info.PluginId))
        {
            _logger.LogError("[PluginUpgrade] Invalid upgrade json '{UpgradeJsonPath}', deleting.", upgradeJsonPath);
            TryDeleteFile(upgradeJsonPath);
            return;
        }

        var pluginId = info.PluginId;
        try
        {
            PluginPathValidator.ValidatePluginId(pluginId);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "[PluginUpgrade] Invalid plugin id in '{UpgradeJsonPath}'.", upgradeJsonPath);
            TryDeleteFile(upgradeJsonPath);
            return;
        }

        var newVersionDir = info.NewVersionPath;
        if (string.IsNullOrEmpty(newVersionDir))
        {
            // 兼容旧字段：回退到约定路径
            newVersionDir = Path.Combine(pendingDir, $"{pluginId}.new");
        }
        newVersionDir = Path.GetFullPath(newVersionDir);
        if (!PluginPathValidator.IsWithinDirectory(pendingDir, newVersionDir))
        {
            _logger.LogError(
                "[PluginUpgrade] '{PluginId}': pending version path '{NewVersionDir}' escapes the pending directory.",
                pluginId,
                newVersionDir);
            TryDeleteFile(upgradeJsonPath);
            return;
        }

        // 潜在问题 6：.new/ 缺失 → 告警并清理 .upgrade.json
        if (!Directory.Exists(newVersionDir))
        {
            _logger.LogError("[PluginUpgrade] '{PluginId}': new version directory '{NewVersionDir}' missing. Manual cleanup may be required.", pluginId, newVersionDir);
            TryDeleteFile(upgradeJsonPath);
            return;
        }

        var targetDir = PluginPathValidator.GetDirectChildPath(_pluginsDirectory, pluginId);
        var stagingDir = targetDir + ".new";
        var oldBackupDir = targetDir + ".old";

        // 清理可能残留的临时目录（上次失败留下的）
        TryDeleteDirectory(stagingDir);
        TryDeleteDirectory(oldBackupDir);

        // 潜在问题 5：迁移前再次 SDK 校验，若失败保留旧版本，仅清理 .pending/
        if (!TryValidateSdkCompatibility(newVersionDir, out var newManifest, out var sdkError))
        {
            _logger.LogError("[PluginUpgrade] '{PluginId}': new version is SDK-incompatible ({SdkError}). Old version retained.", pluginId, sdkError);
            TryDeleteDirectory(newVersionDir);
            TryDeleteFile(upgradeJsonPath);
            return;
        }

        // ============== 安全迁移顺序：先复制后删除 ==============
        // 步骤 1：复制 .pending/{PluginId}.new/ → plugins/{PluginId}.new/
        try
        {
            PluginUtilities.CopyDirectory(newVersionDir, stagingDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PluginUpgrade] '{PluginId}': step 1 (copy to staging) failed", pluginId);
            TryDeleteDirectory(stagingDir);
            // 旧版本未触碰，保持原状，仅清理 .pending
            TryDeleteDirectory(newVersionDir);
            TryDeleteFile(upgradeJsonPath);
            return;
        }

        // 步骤 2：重命名 plugins/{PluginId}/ → plugins/{PluginId}.old/
        // 若旧版本不存在（首次安装被误标为升级），跳过此步。
        var oldManifestExists = Directory.Exists(targetDir) && File.Exists(Path.Combine(targetDir, "plugin.json"));
        // 升级前先读取旧 manifest 的 SchemaVersion，用于后续透传给新插件的 RegisterAsync。
        var previousSchemaVersion = oldManifestExists
            ? TryReadPreviousSchemaVersion(Path.Combine(targetDir, "plugin.json"))
            : null;
        // 记入暂存表，由 LoadAllPluginManifests 完成加载后回填到 in-memory PluginInfo。
        _pendingPreviousSchemaVersions[pluginId] = previousSchemaVersion;
        if (oldManifestExists)
        {
            try
            {
                Directory.Move(targetDir, oldBackupDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PluginUpgrade] '{PluginId}': step 2 (rename old → .old) failed", pluginId);
                // 回滚步骤 1
                TryDeleteDirectory(stagingDir);
                TryDeleteDirectory(newVersionDir);
                TryDeleteFile(upgradeJsonPath);
                _pendingPreviousSchemaVersions.Remove(pluginId);
                return;
            }
        }

        // 步骤 3：重命名 plugins/{PluginId}.new/ → plugins/{PluginId}/
        try
        {
            Directory.Move(stagingDir, targetDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PluginUpgrade] '{PluginId}': step 3 (rename staging → target) failed", pluginId);
            // 回滚：把 .old 移回原位
            if (oldManifestExists)
            {
                try { Directory.Move(oldBackupDir, targetDir); }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "[PluginUpgrade] '{PluginId}': FATAL rollback failed. Manual intervention required.", pluginId);
                }
            }
            TryDeleteDirectory(stagingDir);
            TryDeleteDirectory(newVersionDir);
            TryDeleteFile(upgradeJsonPath);
            return;
        }

        // 步骤 4：删除 .old/
        if (oldManifestExists)
        {
            try
            {
                Directory.Delete(oldBackupDir, true);
            }
            catch (Exception ex)
            {
                // 非致命：新版本已就位，旧版本残留 .old 不会影响加载
                _logger.LogError(ex, "[PluginUpgrade] '{PluginId}': step 4 (delete .old) failed (non-fatal)", pluginId);
            }
        }

        // 步骤 5：状态合并 —— 把旧 manifest 的 State 字段写到新 manifest
        if (info.PreserveState && newManifest != null)
        {
            try
            {
                ApplyPreservedState(targetDir, newManifest, info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PluginUpgrade] '{PluginId}': state merge failed (non-fatal)", pluginId);
            }
        }

        // 步骤 6：清理 .pending/{PluginId}.new/ 和 .upgrade.json
        TryDeleteDirectory(newVersionDir);
        TryDeleteFile(upgradeJsonPath);

        _logger.LogInformation("[PluginUpgrade] '{PluginId}': successfully upgraded to v{NewVersion}.", pluginId, info.NewVersion);
    }

    /// <summary>
    /// 读取新版本 plugin.json，校验 MinPluginSdkVersion 与当前宿主是否兼容。
    /// </summary>
    private bool TryValidateSdkCompatibility(string newVersionDir, out PluginManifest? manifest, out string error)
    {
        manifest = null;
        error = string.Empty;
        var manifestPath = Path.Combine(newVersionDir, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            error = "plugin.json not found in new version";
            return false;
        }

        var json = File.ReadAllText(manifestPath);
        manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginUtilities.JsonOptions);
        if (manifest == null)
        {
            error = "plugin.json is invalid";
            return false;
        }

        if (!IsPluginSdkCompatible(manifest.MinPluginSdkVersion))
        {
            var required = string.IsNullOrWhiteSpace(manifest.MinPluginSdkVersion) ? "0.0.0" : manifest.MinPluginSdkVersion!;
            error = $"requires Plugin SDK >= {required}, host provides {PluginSdkContract.CurrentVersion}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 状态合并：根据 .upgrade.json 的 OldStateToPreserve 字段，把新 manifest 的 State
    /// 设置为旧状态（仅 Disabled/Installed 合法；Error/PendingUninstall 重置为 Installed）。
    /// </summary>
    private void ApplyPreservedState(string targetDir, PluginManifest newManifest, PendingUpgradeInfo info)
    {
        var preserved = info.OldStateToPreserve;
        string finalState;

        if (!string.IsNullOrEmpty(preserved) &&
            Enum.TryParse<PluginState>(preserved, out var oldState))
        {
            finalState = oldState switch
            {
                PluginState.Disabled => nameof(PluginState.Disabled),
                PluginState.Installed => nameof(PluginState.Installed),
                PluginState.Loaded => nameof(PluginState.Installed), // 升级后必须重新加载，不能直接进 Loaded
                _ => nameof(PluginState.Installed) // Error/PendingUninstall/PendingUpgrade 重置
            };
        }
        else
        {
            finalState = nameof(PluginState.Installed);
        }

        newManifest.State = finalState;

        // 保留旧 InstallTime（若新包未自带）
        newManifest.InstallTime ??= info.ScheduledAt;

        var manifestPath = Path.Combine(targetDir, "plugin.json");
        var json = JsonSerializer.Serialize(newManifest, PluginUtilities.JsonOptions);
        File.WriteAllText(manifestPath, json);
    }

    /// <summary>
    /// 启动期：扫描所有插件目录，若某个 plugin.json 的 state=PendingUpgrade（说明上次运行时
    /// 用户已调度升级但未重启即崩溃，且 .pending/ 还在），把对应 PluginInfo 状态恢复为 PendingUpgrade。
    /// 这样 UI 能继续显示"待升级"并提供取消按钮。
    /// </summary>
    private void RestorePendingUpgradeStates()
    {
        // 在 LoadAllPluginManifests 之后调用：检查 .pending/*.upgrade.json 仍存在的插件，
        // 把 _entries 中对应项的状态覆盖为 PendingUpgrade。
        var pendingDir = Path.Combine(_pluginsDirectory, ".pending");
        if (!Directory.Exists(pendingDir)) return;

        foreach (var upgradeJson in Directory.GetFiles(pendingDir, "*.upgrade.json"))
        {
            try
            {
                var json = File.ReadAllText(upgradeJson);
                var info = JsonSerializer.Deserialize<PendingUpgradeInfo>(json, PluginUtilities.JsonOptions);
                if (info == null || string.IsNullOrEmpty(info.PluginId)) continue;

                lock (_sync)
                {
                    if (_entries.TryGetValue(info.PluginId, out var entry))
                    {
                        var upgraded = entry.Info.WithPendingUpgrade(
                            info.NewVersion,
                            $"Upgrade to v{info.NewVersion} scheduled; restart to apply.") with
                        { State = PluginState.PendingUpgrade };
                        entry.Info = upgraded;
                        InvalidateSnapshot();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PluginUpgrade] Failed to restore pending state from '{UpgradeJson}'", upgradeJson);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogError(ex, "[PluginUpgrade] Failed to delete file '{Path}'", path); }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) { _logger.LogError(ex, "[PluginUpgrade] Failed to delete directory '{Path}'", path); }
    }

    public void MarkPendingUpgrade(string pluginId, PendingUpgradeInfo info)
    {
        PluginPathValidator.ValidatePluginId(pluginId);
        PluginInfo? newInfo = null;
        lock (_sync)
        {
            if (!_entries.TryGetValue(pluginId, out var entry)) return;
            if (entry.Info.IsBuiltIn) return;

            newInfo = entry.Info.WithPendingUpgrade(
                info.NewVersion,
                $"Upgrade to v{info.NewVersion} scheduled; restart to apply.") with
            { State = PluginState.PendingUpgrade };
            entry.Info = newInfo;
            SavePluginManifest(newInfo);
            InvalidateSnapshot();
        }

        if (newInfo is not null)
        {
            PluginStateChanged?.Invoke(this, newInfo);
        }
    }

    public bool CancelPendingUpgrade(string pluginId)
    {
        PluginPathValidator.ValidatePluginId(pluginId);
        var pendingInfo = GetPendingUpgrade(pluginId);
        PluginInfo? restoredInfo = null;
        bool cleared;

        lock (_sync)
        {
            if (!_entries.TryGetValue(pluginId, out var entry)) return false;
            if (entry.Info.State != PluginState.PendingUpgrade) return false;

            // 计算恢复后的状态：升级前必然是 Loaded 或 Error（见 InstallationManager 调度条件）
            // 这里无法精确还原 Loaded（程序集仍加载中），所以统一恢复为 Installed。
            var restoredState = Enum.TryParse<PluginState>(pendingInfo?.OldStateToPreserve, out var oldState)
                && oldState == PluginState.Disabled
                    ? PluginState.Disabled
                    : PluginState.Installed;
            restoredInfo = entry.Info with
            {
                State = restoredState,
                ErrorMessage = null,
                PendingUpgradeVersion = null
            };
            entry.Info = restoredInfo;
            SavePluginManifest(restoredInfo);
            InvalidateSnapshot();
        }

        // 清理 .pending/{PluginId}.upgrade.json 与 .pending/{PluginId}.new/
        var pendingDir = Path.Combine(_pluginsDirectory, ".pending");
        var upgradeJson = Path.Combine(pendingDir, $"{pluginId}.upgrade.json");
        var newVersionDir = Path.Combine(pendingDir, $"{pluginId}.new");
        cleared = TryClearPending(upgradeJson, newVersionDir);

        if (restoredInfo is not null)
        {
            PluginStateChanged?.Invoke(this, restoredInfo);
        }
        return cleared;
    }

    private static bool TryClearPending(string upgradeJson, string newVersionDir)
    {
        var any = false;
        try
        {
            if (File.Exists(upgradeJson)) { File.Delete(upgradeJson); any = true; }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PluginUpgrade] Failed to delete '{UpgradeJson}'", upgradeJson);
        }
        try
        {
            if (Directory.Exists(newVersionDir)) { Directory.Delete(newVersionDir, true); any = true; }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PluginUpgrade] Failed to delete '{NewVersionDir}'", newVersionDir);
        }
        return any;
    }

    public PendingUpgradeInfo? GetPendingUpgrade(string pluginId)
    {
        PluginPathValidator.ValidatePluginId(pluginId);
        var pendingDir = Path.Combine(_pluginsDirectory, ".pending");
        var upgradeJson = Path.Combine(pendingDir, $"{pluginId}.upgrade.json");
        if (!File.Exists(upgradeJson)) return null;

        try
        {
            var json = File.ReadAllText(upgradeJson);
            return JsonSerializer.Deserialize<PendingUpgradeInfo>(json, PluginUtilities.JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void ProcessPendingUninstalls()
    {
        if (!Directory.Exists(_pluginsDirectory)) return;

        foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory))
        {
            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginUtilities.JsonOptions);
                if (manifest == null) continue;

                if (manifest.State == nameof(PluginState.PendingUninstall))
                {
                    try
                    {
                        Directory.Delete(pluginDir, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete plugin directory '{PluginDir}'", pluginDir);
                    }

                    // 卸载时一并清理 Data/{PluginId}/ 目录。
                    // 若插件作者声明需要保留用户数据，可在 plugin.json 中后续追加
                    // PreserveDataOnUninstall 字段（暂未实现，TODO）。
                    if (!string.IsNullOrWhiteSpace(manifest.PluginId))
                    {
                        var dataDir = Path.Combine(PluginDataDirectoryProvider.ResolveHostDataRoot(), manifest.PluginId);
                        try
                        {
                            if (Directory.Exists(dataDir))
                            {
                                Directory.Delete(dataDir, true);
                                _logger.LogInformation("[PluginUninstall] Cleared data directory '{DataDir}'", dataDir);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[PluginUninstall] Failed to delete data directory '{DataDir}'", dataDir);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process plugin manifest '{ManifestPath}'", manifestPath);
            }
        }
    }

    private void LoadAllPluginManifests()
    {
        if (!Directory.Exists(_pluginsDirectory)) return;

        foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory))
        {
            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginUtilities.JsonOptions);
                if (manifest == null) continue;

                var pluginInfo = ManifestToPluginInfo(manifest, pluginDir);

                if (pluginInfo.State == PluginState.Loaded)
                {
                    pluginInfo = pluginInfo.WithState(PluginState.Installed);
                }

                // 为已安装插件补齐数据目录路径（首次迁移：旧版本可能在 AppContext.BaseDirectory 等位置有遗留数据，
                // 后续插件升级或重装时建议通过 PluginDataDirectoryProvider 主动迁移）。
                if (string.IsNullOrEmpty(pluginInfo.DataDirectory) &&
                    !string.IsNullOrWhiteSpace(pluginInfo.PluginId))
                {
                    var hostRoot = PluginDataDirectoryProvider.ResolveHostDataRoot();
                    var dataDir = Path.Combine(hostRoot, pluginInfo.PluginId);
                    Directory.CreateDirectory(dataDir);
                    pluginInfo = pluginInfo.WithDataDirectory(dataDir);
                }

                // 若本次是升级场景，回填旧 SchemaVersion（仅本次启动有效，不持久化到新 manifest）。
                if (_pendingPreviousSchemaVersions.TryGetValue(pluginInfo.PluginId, out var previousSchema))
                {
                    pluginInfo = pluginInfo.WithUpgradeAwareness(
                        previousSchemaVersion: previousSchema,
                        currentSchemaVersion: pluginInfo.CurrentSchemaVersion,
                        requiresDataMigration: pluginInfo.RequiresDataMigration);
                }

                var entry = GetOrCreateEntry(pluginInfo.PluginId);
                entry.Info = pluginInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin manifest '{ManifestPath}'", manifestPath);
            }
        }
    }

    private PluginInfo ManifestToPluginInfo(PluginManifest manifest, string pluginDir)
    {
        var assemblyPath = !string.IsNullOrEmpty(manifest.Assembly)
            ? Path.Combine(pluginDir, manifest.Assembly)
            : string.Empty;

        return new PluginInfo
        {
            PluginId = manifest.PluginId ?? string.Empty,
            Name = manifest.Name ?? string.Empty,
            Version = manifest.Version ?? "1.0.0",
            Author = manifest.Author ?? string.Empty,
            Description = manifest.Description ?? string.Empty,
            Dependencies = manifest.Dependencies ?? [],
            SharedAssemblies = manifest.SharedAssemblies ?? [],
            InstallPath = pluginDir,
            AssemblyPath = assemblyPath,
            State = Enum.TryParse<PluginState>(manifest.State, out var state) ? state : PluginState.Installed,
            InstallTime = manifest.InstallTime,
            IsBuiltIn = manifest.IsBuiltIn,
            HasMetadata = !string.IsNullOrEmpty(manifest.PluginId),
            MinPluginSdkVersion = manifest.MinPluginSdkVersion,
            Kind = string.IsNullOrWhiteSpace(manifest.Kind) ? "Avalonia" : manifest.Kind,
            Web = manifest.Web,
            CurrentSchemaVersion = string.IsNullOrWhiteSpace(manifest.SchemaVersion) ? "0" : manifest.SchemaVersion,
            RequiresDataMigration = manifest.RequiresDataMigration
        };
    }

    /// <summary>
    /// 读取旧 manifest 的 SchemaVersion（若存在），用于在升级场景下透传给新插件，
    /// 由插件 <c>RegisterAsync</c> 自决定是否迁移本地数据库 schema。
    /// 旧 manifest 已不在磁盘上时返回 null。
    /// </summary>
    private static string? TryReadPreviousSchemaVersion(string oldManifestJsonPath)
    {
        try
        {
            if (!File.Exists(oldManifestJsonPath)) return null;
            var old = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(oldManifestJsonPath), PluginUtilities.JsonOptions);
            return old?.SchemaVersion;
        }
        catch
        {
            return null;
        }
    }

    private void SavePluginManifest(PluginInfo pluginInfo)
    {
        if (!_persistManifestChanges) return;

        try
        {
            var pluginDir = pluginInfo.InstallPath;
            if (string.IsNullOrEmpty(pluginDir))
            {
                pluginDir = Path.Combine(_pluginsDirectory, pluginInfo.PluginId);
                var updated = pluginInfo.WithInstallPath(pluginDir);
                var entry = GetOrCreateEntry(pluginInfo.PluginId);
                entry.Info = updated;
                pluginInfo = updated;
            }

            Directory.CreateDirectory(pluginDir);

            var manifest = new PluginManifest
            {
                PluginId = pluginInfo.PluginId,
                Name = pluginInfo.Name,
                Version = pluginInfo.Version,
                Author = pluginInfo.Author,
                Description = pluginInfo.Description,
                Assembly = !string.IsNullOrEmpty(pluginInfo.AssemblyPath)
                    ? Path.GetFileName(pluginInfo.AssemblyPath)
                    : $"{pluginInfo.Name}.dll",
                Dependencies = pluginInfo.Dependencies,
                State = pluginInfo.State.ToString(),
                InstallTime = pluginInfo.InstallTime,
                IsBuiltIn = pluginInfo.IsBuiltIn,
                MinPluginSdkVersion = pluginInfo.MinPluginSdkVersion,
                Kind = pluginInfo.Kind,
                Web = pluginInfo.Kind == "Web" ? pluginInfo.Web : null,
                SchemaVersion = string.IsNullOrWhiteSpace(pluginInfo.CurrentSchemaVersion) ? null : pluginInfo.CurrentSchemaVersion,
                RequiresDataMigration = pluginInfo.RequiresDataMigration
            };

            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            var json = JsonSerializer.Serialize(manifest, PluginUtilities.JsonOptions);
            File.WriteAllText(manifestPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save plugin manifest for '{PluginId}'", pluginInfo.PluginId);
        }
    }

    private void DeletePluginManifest(string pluginId)
    {
        if (!_persistManifestChanges) return;
        if (!_entries.TryGetValue(pluginId, out var entry)) return;

        try
        {
            var manifestPath = Path.Combine(entry.Info.InstallPath, "plugin.json");
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete plugin manifest for '{PluginId}'", pluginId);
        }
    }

    /// <summary>
    /// 优雅关闭所有已加载插件：调用 IPlugin.ShutdownAsync()。
    /// 应在应用退出流程中、ServiceProvider Dispose 之前调用。
    /// 单个插件 ShutdownAsync 抛异常不会中断其他插件的关闭。
    /// </summary>
    public async Task ShutdownAllPluginsAsync()
    {
        List<IPlugin> pluginsToShutdown;
        lock (_sync)
        {
            pluginsToShutdown = _entries.Values
                .Where(e => e.Plugin is not null && e.Info.State == PluginState.Loaded)
                .Select(e => e.Plugin!)
                .Reverse()
                .ToList();
        }

        foreach (var plugin in pluginsToShutdown)
        {
            try
            {
                await plugin.ShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin ShutdownAsync failed");
            }
        }
    }

    /// <summary>
    /// 标记插件为错误状态并持久化 manifest。
    /// 用于插件初始化/注册之外（如导航注册）发现插件不可恢复故障时调用。
    /// </summary>
    public void MarkPluginError(string pluginId, string errorMessage)
    {
        PluginInfo? info = null;
        lock (_sync)
        {
            if (!_entries.TryGetValue(pluginId, out var entry)) return;
            info = entry.Info.WithState(PluginState.Error, errorMessage);
            entry.Info = info;
            SavePluginManifest(info);
            InvalidateSnapshot();
        }

        if (info is not null)
        {
            PluginStateChanged?.Invoke(this, info);
        }
    }

    public void Dispose()
    {
        // 修复：原 Dispose 仅 ALC.Unload，未调用 IPlugin.ShutdownAsync，
        // 插件持有的原生资源（如 TdLib 客户端、文件句柄）无法被显式释放。
        // 改为同步等待 ShutdownAllPluginsAsync 完成后再卸载 ALC。
        // Dispose 是同步方法，使用 Task.Run 避免在 UI 线程上 sync-over-async 死锁。
        try
        {
            Task.Run(() => ShutdownAllPluginsAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ShutdownAllPluginsAsync failed during Dispose");
        }

        lock (_sync)
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.Context is not null)
                {
                    try { entry.Context.Unload(); } catch { }
                }
            }
            _entries.Clear();
            _cachedPluginList = null;
        }
    }

    /// <summary>
    /// 校验插件声明的 MinPluginSdkVersion 是否被当前宿主链接的 Plugin SDK 满足。
    /// 规则遵循 SemVer：
    /// - 主版本号必须相等（主版本递增代表破坏性变更，跨主版本不兼容）。
    /// - 次版本号：宿主 &gt;= 插件要求即兼容（次版本递增代表向后兼容的新增功能）。
    /// - 修订号：宿主 &gt;= 插件要求即兼容。
    /// null/空视为无约束（通过）。仅比较 Major.Minor.Build 三段；预发布标签忽略。
    /// 解析失败：拒绝加载（fail-closed）。版本不明不应放行，避免不兼容插件运行时崩溃。
    /// </summary>
    public static bool IsPluginSdkCompatible(string? required)
    {
        if (string.IsNullOrWhiteSpace(required)) return true;

        if (!TryParseSemVer(required, out var reqMajor, out var reqMinor, out var reqBuild))
            return false; // 解析失败拒绝放行（fail-closed），避免误判不兼容插件

        if (!TryParseSemVer(PluginSdkContract.CurrentVersion, out var curMajor, out var curMinor, out var curBuild))
            return false; // 宿主 SDK 版本无法解析时拒绝（fail-closed）

        // 主版本号不同视为不兼容：SemVer 规范下主版本递增代表破坏性变更
        if (curMajor != reqMajor) return false;
        if (curMinor != reqMinor) return curMinor > reqMinor;
        return curBuild >= reqBuild;
    }

    private static bool TryParseSemVer(string? version, out int major, out int minor, out int build)
    {
        major = minor = build = 0;
        if (string.IsNullOrWhiteSpace(version)) return false;

        // 取 '-' 之前的稳定版本部分
        var stable = version.IndexOf('-', StringComparison.Ordinal);
        var core = stable >= 0 ? version.Substring(0, stable) : version;

        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return false;

        return int.TryParse(parts[0], out major)
            && (parts.Length < 2 || int.TryParse(parts[1], out minor))
            && (parts.Length < 3 || int.TryParse(parts[2], out build));
    }

    private sealed class PluginEntry
    {
        public PluginInfo Info { get; set; } = new();
        public IPlugin? Plugin { get; set; }
        public IPluginMetadata? Metadata { get; set; }
        public IGeneratedPluginModule? Module { get; set; }
        public AssemblyLoadContext? Context { get; set; }
        public bool IsInitialized { get; set; }
    }
}
