using System.Collections.Generic;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using LYBox.Plugin.Shared.ViewModels;
using LYBox.Layout.Core.Services;
using LYBox.Layout.Core.ViewModels;
using LYBox.Layout.Ursa.Pages;
using LYBox.Layout.Ursa.ViewModels;

namespace LYBox.Layout.Ursa.Services;

public sealed class NavigationService : INavigationService
{
    private const int MaxCacheSize = 5;

    /// <summary>「最近使用」最多保留的工具数量。</summary>
    private const int MaxRecentTools = 6;

    /// <summary>宿主内置页面，不计入「最近使用」。</summary>
    private static readonly HashSet<string> HostPageKeys = ["Introduction", "Settings", "PluginManagement"];

    private readonly List<string> _recentKeys = [];

    private readonly Dictionary<string, ViewModelFactory> _viewModelFactories = [];
    // 导航 key → 归属插件 PluginId（宿主内置导航不记录，卸载事件不涉及宿主）
    private readonly Dictionary<string, string> _factoryOwners = [];
    // 强引用 LRU 缓存：保留最近访问的 ViewModel，避免来回切换时反复重建。
    // LinkedListNode 同时作为 Dictionary 的 Value，实现 O(1) 的插入/删除/访问。
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _viewModelCache = [];
    private readonly LinkedList<CacheEntry> _lruList = new();

    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IPluginManagementService _pluginManagementService;
    private readonly IMenuConfigurationService _menuConfigurationService;

    private sealed class CacheEntry(string key, object viewModel)
    {
        public string Key { get; } = key;
        public object ViewModel { get; } = viewModel;
    }

    public NavigationService(
        ISettingsService settingsService,
        ILocalizationService localizationService,
        IPluginManagementService pluginManagementService,
        IMenuConfigurationService menuConfigurationService)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _pluginManagementService = pluginManagementService;
        _menuConfigurationService = menuConfigurationService;
        RegisterDefaultNavigations();
    }

    public void AttachPluginLoader(IPluginLoader pluginLoader)
    {
        pluginLoader.PluginUnloaded += OnPluginUnloaded;
    }

    private void OnPluginUnloaded(object? sender, PluginInfo pluginInfo)
    {
        InvalidateOwner(pluginInfo.PluginId);
    }

    private void RegisterDefaultNavigations()
    {
        RegisterNavigation("Introduction", () => new IntroductionDemoViewModel());
        RegisterNavigation("Settings", () => new SettingsPageViewModel(_settingsService, _localizationService));
        RegisterNavigation("PluginManagement", () => new PluginManagementViewModel(_pluginManagementService));

        ViewLocator.Register<IntroductionDemoViewModel, IntroductionDemo>();
        ViewLocator.Register<SettingsPageViewModel, SettingsPage>();
        ViewLocator.Register<PluginManagementViewModel, PluginManagementPage>();
    }

    public void RegisterNavigation(string key, ViewModelFactory factory)
    {
        _viewModelFactories[key] = factory;
    }

    public void RegisterNavigations(Dictionary<string, ViewModelFactory> navigations, string? pluginId = null)
    {
        foreach (var (key, factory) in navigations)
        {
            _viewModelFactories[key] = factory;
            if (pluginId != null)
                _factoryOwners[key] = pluginId;
        }
    }

    public object CreateViewModel(string key)
    {
        // LRU 命中：移到链表头部（最近使用）
        if (_viewModelCache.TryGetValue(key, out var node))
        {
            _lruList.Remove(node);
            _lruList.AddFirst(node);
            return node.Value.ViewModel;
        }

        object viewModel;
        if (_viewModelFactories.TryGetValue(key, out var factory))
        {
            viewModel = factory();
        }
        else if (FindMenuItem(_menuConfigurationService.GetMenuStructure(), key) is { Children.Count: > 0 })
        {
            // 父级菜单没有注册真实页面：以该分类的工具目录页承接
            // （参考 DevToys 分类页，复用首页卡片布局）
            viewModel = new IntroductionDemoViewModel(key);
        }
        else
        {
            throw new System.ArgumentOutOfRangeException(nameof(key), key, null);
        }

        // 淘汰最久未使用的条目
        if (_lruList.Count >= MaxCacheSize)
        {
            var lru = _lruList.Last!;
            _lruList.RemoveLast();
            _viewModelCache.Remove(lru.Value.Key);
            ViewLocator.InvalidateViewCache(lru.Value.ViewModel);
            (lru.Value.ViewModel as IDisposable)?.Dispose();
        }

        var newNode = _lruList.AddFirst(new CacheEntry(key, viewModel));
        _viewModelCache[key] = newNode;
        return viewModel;
    }

    private static MenuItemViewModel? FindMenuItem(IEnumerable<MenuItemViewModel> items, string key)
    {
        foreach (var item in items)
        {
            if (item.Key == key)
                return item;
            if (FindMenuItem(item.Children, key) is { } match)
                return match;
        }
        return null;
    }

    public void InvalidateCache(string key)
    {
        if (_viewModelCache.TryGetValue(key, out var node))
        {
            _lruList.Remove(node);
            _viewModelCache.Remove(key);
            ViewLocator.InvalidateViewCache(node.Value.ViewModel);
            (node.Value.ViewModel as IDisposable)?.Dispose();
        }
    }

    /// <summary>按归属插件失效其全部导航缓存（修复：原实现误用 PluginId 当导航 key 匹配）</summary>
    public void InvalidateOwner(string? pluginId)
    {
        if (pluginId == null) return;
        foreach (var key in _factoryOwners.Where(kv => kv.Value == pluginId)
                                          .Select(kv => kv.Key)
                                          .ToArray())
        {
            InvalidateCache(key);
            _factoryOwners.Remove(key);
        }

        // 插件卸载后其工具不应再出现在「最近使用」中
        _recentKeys.RemoveAll(key => !_viewModelFactories.ContainsKey(key));
        RecentNavigationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InvalidateAllCache()
    {
        foreach (var node in _lruList)
        {
            ViewLocator.InvalidateViewCache(node.ViewModel);
            (node.ViewModel as IDisposable)?.Dispose();
        }
        _lruList.Clear();
        _viewModelCache.Clear();
    }

    public IEnumerable<string> GetNavigationKeys()
    {
        return _viewModelFactories.Keys;
    }

    public IReadOnlyList<string> GetRecentNavigationKeys() => _recentKeys;

    public void RecordNavigationVisit(string key)
    {
        if (string.IsNullOrEmpty(key) || HostPageKeys.Contains(key))
            return;

        // 只有真实工具的叶子节点才进入「最近使用」；父级分类节点没有独立页面
        var item = FindMenuItem(_menuConfigurationService.GetMenuStructure(), key);
        if (item is null || item.Children is { Count: > 0 })
            return;

        _recentKeys.Remove(key);
        _recentKeys.Insert(0, key);

        if (_recentKeys.Count > MaxRecentTools)
            _recentKeys.RemoveRange(MaxRecentTools, _recentKeys.Count - MaxRecentTools);

        RecentNavigationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? RecentNavigationsChanged;
}
