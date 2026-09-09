
using Avalonia.Controls;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// 返回插件自定义的资源字典（如图标 StreamGeometry、Brush 等），启动期会被宿主
    /// 合并到 <see cref="Avalonia.Application.Resources"/> 的 MergedDictionaries，
    /// 之后 XAML 端可通过 {DynamicResource ...} 引用这些资源。
    /// 返回 null 表示该插件没有自定义资源（默认实现）。
    /// </summary>
    /// <remarks>
    /// 当前由 <c>LYBox.Plugin.Generators.MetadataGenerator</c> 对走 [GenerateMetadata] 的插件
    /// 硬编码 emit <c>=> null;</c>（除非 csproj 声明 <c>PluginIconResources</c>）。若插件需要
    /// 提供资源，可在 csproj 声明资源文件，或手动实现 <see cref="IPlugin"/>（不走源生成器）。
    /// </remarks>
    IResourceDictionary? GetIconResources() => null;
}


/// <summary>
/// ViewModel 工厂委托。宿主导航注册（<c>INavigationService</c>）使用。
/// </summary>
public delegate object ViewModelFactory();
/// <summary>
/// 视图工厂委托。<see cref="ViewLocator"/> 视图注册表使用。
/// </summary>
public delegate Control ViewFactory();
