using System.Collections.Generic;
using LYBox.Plugin.Shared;

namespace LYBox.Layout.Core.Services;

public interface INavigationService
{
    void RegisterNavigation(string key, ViewModelFactory factory);

    void RegisterNavigations(Dictionary<string, ViewModelFactory> navigations, string? pluginId = null);

    object CreateViewModel(string key);

    void InvalidateCache(string key);

    void InvalidateOwner(string? pluginId);

    void InvalidateAllCache();

    IEnumerable<string> GetNavigationKeys();

    /// <summary>最近打开过的工具页面 Key（新的在前，已按容量截断）。</summary>
    IReadOnlyList<string> GetRecentNavigationKeys();

    /// <summary>记录一次工具页面访问；宿主内置页与分类节点会被忽略。</summary>
    void RecordNavigationVisit(string key);

    /// <summary>最近访问列表发生变化（首页据此刷新「最近使用」）。</summary>
    event EventHandler? RecentNavigationsChanged;
}
