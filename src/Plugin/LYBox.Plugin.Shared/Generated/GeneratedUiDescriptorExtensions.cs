using LYBox.Plugin.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LYBox.Plugin.Shared.Generated;

/// <summary>
/// <see cref="GeneratedPluginUiDescriptor"/> → 宿主注册模型的唯一转换入口。
/// UI 注册为单轨设计：导航/菜单/视图一律从 <c>IGeneratedPluginModule.Ui</c> 描述符读取，
/// 插件侧不再暴露 <c>IPlugin.GetNavigationItems()</c> 等实例方法。
/// </summary>
public static class GeneratedUiDescriptorExtensions
{
    /// <summary>
    /// 把导航描述符转换为宿主 <c>INavigationService.RegisterNavigations</c> 的注册字典。
    /// 工厂延迟到导航发生时才执行，经 DI 解析 ViewModel（缺失时回退 new）。
    /// </summary>
    public static Dictionary<string, ViewModelFactory> ToNavigationFactories(
        this GeneratedPluginUiDescriptor ui, IServiceProvider services)
    {
        var factories = new Dictionary<string, ViewModelFactory>(ui.NavigationItems.Count);
        foreach (var item in ui.NavigationItems)
        {
            factories[item.Key] = () => item.CreateViewModel(services);
        }
        return factories;
    }

    /// <summary>
    /// 把菜单描述符转换为菜单树。构建语义与旧源生成实现保持一致：
    /// Header 交由 <see cref="MenuItemViewModel.MenuHeader"/> 解析本地化，
    /// 缺失父级自动补虚拟分组节点（图标继承自首个子项），根节点按 Order 排序。
    /// </summary>
    public static List<KeyValuePair<string?, MenuItemViewModel>> ToMenuItems(this GeneratedPluginUiDescriptor ui)
    {
        var allItems = new List<(string? Parent, MenuItemViewModel Item, int Order)>(ui.MenuItems.Count);
        foreach (var item in ui.MenuItems)
        {
            allItems.Add((item.ParentKey, new MenuItemViewModel
            {
                MenuHeader = item.Header,
                Key = item.Key,
                MenuIconName = item.IconName,
                Status = item.Status,
                Order = item.Order,
            }, item.Order));
        }
        return MenuItemTreeBuilder.BuildTree(allItems);
    }
}
