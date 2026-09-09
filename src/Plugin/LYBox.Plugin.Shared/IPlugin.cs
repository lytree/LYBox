
using Avalonia.Controls;
using LYBox.Plugin.Shared.ViewModels;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace LYBox.Plugin.Shared;


public interface IPlugin
{
    /// <summary>
    /// 初始化插件，向 IServiceCollection 注册服务。在 DI 容器构建前调用。
    /// </summary>
    Task InitializeAsync(IServiceCollection services) => Task.CompletedTask;

    /// <summary>
    /// DI 容器构建完成后调用，用于注册语言资源、设置等需要 IServiceProvider 的操作
    /// </summary>
    Task RegisterAsync(IServiceProvider serviceProvider) => Task.CompletedTask;

    Task ShutdownAsync() => Task.CompletedTask;
    IEnumerable<KeyValuePair<Type, ViewFactory>> GetViewDefinitions();
    Dictionary<string, ViewModelFactory> GetNavigationItems();
    List<KeyValuePair<string?, MenuItemViewModel>> GetMenuItems();

    /// <summary>
    /// 返回插件自定义的资源字典（如图标 StreamGeometry、Brush 等），启动期会被宿主
    /// 合并到 <see cref="Avalonia.Application.Resources"/> 的 MergedDictionaries，
    /// 之后 XAML 端可通过 {DynamicResource ...} 引用这些资源。
    /// 返回 null 表示该插件没有自定义资源（默认实现）。
    /// </summary>
    /// <remarks>
    /// 当前由 <c>LYBox.Plugin.Generators.MetadataGenerator</c> 对走 [GenerateMetadata] 的插件
    /// 硬编码 emit <c>=> null;</c>。若插件需要提供资源，必须手动实现 <see cref="IPlugin"/>
    /// （不走源生成器），或后续扩展生成器以支持资源声明。
    /// </remarks>
    IResourceDictionary? GetIconResources() => null;
}


/// <summary>
/// ViewModel 工厂委托
/// </summary>
public delegate object ViewModelFactory();
/// <summary>
/// 视图工厂委托
/// </summary>
public delegate Control ViewFactory();
