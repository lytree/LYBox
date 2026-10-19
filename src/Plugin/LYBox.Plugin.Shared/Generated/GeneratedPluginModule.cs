using Avalonia.Controls;

namespace LYBox.Plugin.Shared.Generated;

/// <summary>Identifies the source-generated module that describes a plugin assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class GeneratedPluginModuleAttribute : Attribute
{
    public GeneratedPluginModuleAttribute(Type moduleType)
    {
        ModuleType = moduleType ?? throw new ArgumentNullException(nameof(moduleType));
    }

    public Type ModuleType { get; }
}

/// <summary>Stable, reflection-free entry point generated for a plugin assembly.</summary>
public interface IGeneratedPluginModule
{
    Type PluginType { get; }
    IPlugin CreatePlugin();
    IPluginMetadata Metadata { get; }
    GeneratedPluginUiDescriptor Ui { get; }
}

public sealed class GeneratedPluginUiDescriptor
{
    public GeneratedPluginUiDescriptor(
        IReadOnlyList<GeneratedViewDescriptor> views,
        IReadOnlyList<GeneratedNavigationDescriptor> navigationItems,
        IReadOnlyList<GeneratedMenuDescriptor> menuItems)
    {
        Views = views ?? throw new ArgumentNullException(nameof(views));
        NavigationItems = navigationItems ?? throw new ArgumentNullException(nameof(navigationItems));
        MenuItems = menuItems ?? throw new ArgumentNullException(nameof(menuItems));
    }

    public IReadOnlyList<GeneratedViewDescriptor> Views { get; }
    public IReadOnlyList<GeneratedNavigationDescriptor> NavigationItems { get; }
    public IReadOnlyList<GeneratedMenuDescriptor> MenuItems { get; }
}

public sealed class GeneratedViewDescriptor
{
    public GeneratedViewDescriptor(Type viewModelType, Type viewType, Func<IServiceProvider, Control> createView)
    {
        ViewModelType = viewModelType ?? throw new ArgumentNullException(nameof(viewModelType));
        ViewType = viewType ?? throw new ArgumentNullException(nameof(viewType));
        CreateView = createView ?? throw new ArgumentNullException(nameof(createView));
    }

    public Type ViewModelType { get; }
    public Type ViewType { get; }
    public Func<IServiceProvider, Control> CreateView { get; }
}

public sealed class GeneratedNavigationDescriptor
{
    public GeneratedNavigationDescriptor(string key, Type viewModelType, Func<IServiceProvider, object> createViewModel)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        ViewModelType = viewModelType ?? throw new ArgumentNullException(nameof(viewModelType));
        CreateViewModel = createViewModel ?? throw new ArgumentNullException(nameof(createViewModel));
    }

    public string Key { get; }
    public Type ViewModelType { get; }
    public Func<IServiceProvider, object> CreateViewModel { get; }
}

public sealed class GeneratedMenuDescriptor
{
    /// <summary>
    /// 兼容旧插件程序集（生成代码仍使用 6 参数构造）的保留重载。
    /// </summary>
    public GeneratedMenuDescriptor(
        string header,
        string key,
        string? parentKey,
        string? iconName,
        string? status,
        int order)
        : this(header, key, parentKey, iconName, status, order, null)
    {
    }

    public GeneratedMenuDescriptor(
        string header,
        string key,
        string? parentKey,
        string? iconName,
        string? status,
        int order,
        string? group)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Key = key ?? throw new ArgumentNullException(nameof(key));
        ParentKey = parentKey;
        IconName = iconName;
        Status = status;
        Order = order;
        Group = group;
    }

    public string Header { get; }
    public string Key { get; }
    public string? ParentKey { get; }
    public string? IconName { get; }
    public string? Status { get; }
    public int Order { get; }

    /// <summary>菜单分组标识；相邻项分组不同时宿主插入分隔线。</summary>
    public string? Group { get; }
}
