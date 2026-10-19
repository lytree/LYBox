using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LYBox.Plugin.Shared.Services;
using LYBox.Plugin.Shared.ViewModels;

namespace LYBox.Layout.Ursa.Services;

public sealed class MenuConfigurationService : IMenuConfigurationService
{
    private readonly LYBox.Plugin.Shared.ViewModels.MenuViewModel _menuViewModel;
    private readonly ConcurrentDictionary<string, MenuItemViewModel> _menuItemsMap = new();
    private bool _mapBuilt;

    public MenuConfigurationService()
    {
        _menuViewModel = new LYBox.Plugin.Shared.ViewModels.MenuViewModel();
        // 宿主只内置「主页」一项：其余目录/工具全部由插件通过 [Menu] 注册，
        // 侧边栏底部固定入口（管理扩展 / 设置）由 MainView 直接承载，不进菜单列表。
        _menuViewModel.MenuItems.Add(new MenuItemViewModel
        {
            MenuHeader = "NAV_Introduction",
            Key = "Introduction",
            MenuIconName = "FluentHome20Regular",
            IsSeparator = false
        });
    }

    private void BuildMenuItemsMap(IEnumerable<MenuItemViewModel> menuItems)
    {
        foreach (var menuItem in menuItems)
        {
            if (!string.IsNullOrEmpty(menuItem.Key))
            {
                _menuItemsMap[menuItem.Key] = menuItem;
            }

            if (menuItem.Children is { Count: > 0 })
            {
                BuildMenuItemsMap(menuItem.Children);
            }
        }
    }

    public ObservableCollection<MenuItemViewModel> GetMenuStructure()
    {
        if (!_mapBuilt)
        {
            BuildMenuItemsMap(_menuViewModel.MenuItems);
            _mapBuilt = true;
        }
        return _menuViewModel.MenuItems;
    }

    public void RegisterMenuItem(MenuItemViewModel menuItem, string? parentKey = null)
    {
        if (parentKey == null)
        {
            _menuViewModel.MenuItems.Add(menuItem);
        }
        else
        {
            var avaloniaParentMenuItem = FindAvaloniaMenuItem(parentKey);
            if (avaloniaParentMenuItem != null)
            {
                avaloniaParentMenuItem.Children ??= new();
                avaloniaParentMenuItem.Children.Add(menuItem);
            }
            if (!string.IsNullOrEmpty(menuItem.Key))
            {
                _menuItemsMap[menuItem.Key] = menuItem;
            }
        }
    }

    private MenuItemViewModel? FindAvaloniaMenuItem(string key)
    {
        if (_menuItemsMap.TryGetValue(key, out var mappedItem))
            return mappedItem;
        return FindAvaloniaMenuItemRecursive(_menuViewModel.MenuItems, key);
    }

    private static MenuItemViewModel? FindAvaloniaMenuItemRecursive(IEnumerable<MenuItemViewModel> menuItems, string key)
    {
        foreach (var menuItem in menuItems)
        {
            if (menuItem.Key == key)
                return menuItem;

            if (menuItem.Children != null)
            {
                var foundItem = FindAvaloniaMenuItemRecursive(menuItem.Children, key);
                if (foundItem != null)
                    return foundItem;
            }
        }
        return null;
    }

    public void RegisterMenuItems(List<KeyValuePair<string?, MenuItemViewModel>> menuItems)
    {
        foreach (var (parentKey, menuItem) in menuItems)
        {
            RegisterMenuItem(menuItem, parentKey);
        }
    }

    public IEnumerable<string> GetMenuItemKeys()
    {
        return _menuItemsMap.Keys;
    }

    public MenuItemViewModel? GetMenuItemByKey(string key)
    {
        _menuItemsMap.TryGetValue(key, out var item);
        return item;
    }
}
