using System.Collections.Generic;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using LYBox.Layout.Core.Services;
using LYBox.Layout.Core.ViewModels;
using LYBox.Layout.Ursa.Pages;
using LYBox.Layout.Ursa.ViewModels;

namespace LYBox.Layout.Ursa.Services;

public sealed class NavigationService : INavigationService
{
    private const int MaxCacheSize = 5;

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

    private sealed class CacheEntry(string key, object viewModel)
    {
        public string Key { get; } = key;
        public object ViewModel { get; } = viewModel;
    }

    public NavigationService(
        ISettingsService settingsService,
        ILocalizationService localizationService,
        IPluginManagementService pluginManagementService)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _pluginManagementService = pluginManagementService;
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
        RegisterNavigation("FluentControls", () => new FluentNavigationDemoViewModel());
        RegisterNavigation("Settings", () => new SettingsPageViewModel(_settingsService, _localizationService));
        RegisterNavigation("PluginManagement", () => new PluginManagementViewModel(_pluginManagementService));

        ViewLocator.Register<IntroductionDemoViewModel, IntroductionDemo>();
        ViewLocator.Register<FluentNavigationDemoViewModel, FluentNavigationDemo>();
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

        if (!_viewModelFactories.TryGetValue(key, out var factory))
            throw new System.ArgumentOutOfRangeException(nameof(key), key, null);

        var viewModel = factory();

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
}
