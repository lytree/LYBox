# 插件 API 参考（当前实现）

## 核心接口

```csharp
public interface IPlugin
{
    Task InitializeAsync(IServiceCollection services);
    Task RegisterAsync(IServiceProvider serviceProvider);
    Task ShutdownAsync();
    IEnumerable<KeyValuePair<Type, ViewFactory>> GetViewDefinitions();
    Dictionary<string, ViewModelFactory> GetNavigationItems();
    List<KeyValuePair<string?, MenuItemViewModel>> GetMenuItems();
}

public interface IPluginMetadata
{
    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }
    string PluginId { get; }
    string MinPluginSdkVersion { get; }
}
```

不要臆造 `Dependencies` 或 `GetIconResources`：它们不是当前核心契约要求的成员。通常使用 `[GenerateMetadata]` 的 partial 入口类，由生成器补齐 `IPlugin` 实现。

## 元数据特性

- `[GenerateMetadata]`：标记插件入口类。
- `[ViewMap(typeof(View))]`：声明 ViewModel 到 View 的映射。
- `[NavigationItem("key")]`：注册导航 key。
- `[Menu(header, key, ParentKey = ..., IconName = ..., Status = ..., Order = ...)]`：注册菜单并由宿主组树。
- `[RpcCommand]`：生成 Web RPC 绑定、JS 客户端和 TypeScript 声明。

## 宿主服务

通过构造函数注入优先；插件兼容代码可使用 `ServiceLocator.TryGetService<T>`。核心服务包括 `ILocalizationService`、`ISettingsService`、`ITaskRegistry`、`IPluginLoader`、`IPluginInstallationManager`、`IPluginManagementService`、`INavigationService`、`IMenuConfigurationService` 和 `IWindowInfoService`。

- 本地化：注册 `ResourceManager`，宿主批量重建缓存。
- 设置：按 PluginId 隔离并由 SQLite 持久化，支持 Text/Switch/Dropdown/Path 定义。
- 任务：使用 `TaskToken`/`IDisposable` 表示运行任务，退出时宿主检查未完成任务。
- 导航/菜单：key 驱动，菜单使用 `ParentKey` 自动构建层级。

## Web 与 CLI 契约

Web 插件引用 `LYBox.Plugin.Shared.Web`，宿主通过 `WebPluginView` 承载页面。`IRpcHost` 支持命令注册、payload 命令、`Channel<T>` 和事件发送。前端直接引用 `/sdk/lybox-plugin-sdk.js`。系统内建命令包括 `OpenFilePicker`、`SaveFilePicker`、`OpenFolderPicker`、`ShowMessageBox`、`ShowConfirmDialog`。

CLI 插件实现 `IPluginCommandRegistrar`，由生成器加入 `IGeneratedPluginCliModule`；通过 `plugin.cli.json` 暴露别名、命令、参数和输出模式。

## 最小入口

```csharp
[GenerateMetadata]
public partial class MyPlugin : IPluginMetadata
{
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Author => "Author";
    public string Description => "Description";
    public string PluginId => "your-unique-id";
    public string MinPluginSdkVersion => PluginSdkContract.CurrentVersion;

    public Task InitializeAsync(IServiceCollection services) => Task.CompletedTask;
    public Task RegisterAsync(IServiceProvider services) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
}
```

## csproj 元数据

必需：`PluginId`、`PluginName`、`PluginAuthor`、`PluginDescription`。常用：`PluginVersion`、`MinPluginSdkVersion`、`PluginKind`（`Avalonia`/`Web`）、`PluginWwwroot`、`PluginEntryPage`。CLI 可设置 `GeneratePluginCliIndex`、`PluginCliAlias`、`PluginCliDescription`、`PluginCliRuntimeProfile` 和 `PluginCliOutputModes`。
