# LYBox 开发与架构

## 仓库边界

`LYBox` 是宿主、布局、平台抽象、插件 SDK、源生成器、启动器和测试仓库；`LYBox.Plugins` 是独立插件实现仓库。两个仓库通过 SDK NuGet 包协作。

## 分层

```text
Plugin.Generators  Roslyn 增量生成器：元数据、UI 映射、RPC 胶水
Plugin.CommandLine CLI 契约与插件命令注册
Plugin.Shared      IPlugin、服务接口、模型、控件、定位器
Plugin.Shared.Web  WebPluginView、Kestrel、RPC、SSE、Channel、浏览器 SDK
Layout.Core        插件加载/安装、数据库、设置、任务、日志
Layout.Ursa        导航、菜单、本地化、主题、主界面
Launcher.Desktop  Avalonia 桌面入口
Launcher.Console   CLI 入口
Platforms.*        跨平台能力抽象与实现
```

`Core.slnx` 面向宿主；`Plugins.slnx` 面向插件。插件项目动态扫描自 `LYBox.Plugins/plugins/*/*.csproj`。

## 启动流程

1. 注册核心 DI、日志、SQLite、设置、任务与窗口服务。
2. 从默认插件目录和 `AVALONIA_EXTRA_PLUGINS_PATH` 发现清单。
3. 校验 `MinPluginSdkVersion`，创建独立可收集 ALC。
4. 调用插件 `InitializeAsync(IServiceCollection)` 注册服务。
5. 构建根 `IServiceProvider` 并初始化 `ServiceLocator`。
6. 执行 EF Core migrations，恢复语言和设置。
7. 调用 `RegisterAsync(IServiceProvider)` 注册本地化、运行时资源和业务。
8. 汇总 View、Navigation、Menu，注册 Web 插件根目录。
9. 有 Web 插件时才懒启动 `127.0.0.1:0` 的 Kestrel。
10. 创建主窗口并进入运行态。

退出时检查运行任务，反向调用 `ShutdownAsync`，释放服务容器和插件资源；超时有兜底退出。

## 插件生命周期与隔离

插件状态包括 `NotInstalled`、`Installed`、`Loaded`、`Disabled`、`PendingUninstall`、`PendingUpgrade`、`Error`。安装、升级、卸载和启停通过 manifest 与 pending 文件协调，通常重启生效。每个插件拥有独立 ALC；框架和共享 SDK 程序集按共享清单转发到默认 ALC。

## SDK 与生成器

插件引用 `LYBox.Plugin.Generators`（Analyzer）、`LYBox.Plugin.Shared`；Web 插件额外引用 `LYBox.Plugin.Shared.Web`；CLI 插件额外引用 `LYBox.Plugin.CommandLine`。`[GenerateMetadata]`、`[ViewMap]`、`[NavigationItem]`、`[Menu]` 和 `[RpcCommand]` 驱动生成代码。元数据优先来自 csproj：`PluginId`、`PluginName`、`PluginAuthor`、`PluginDescription`、`PluginVersion`、`MinPluginSdkVersion`、`PluginKind`、`PluginWwwroot`、`PluginEntryPage`。

## 构建、调试和发布

构建入口是仓库根 `build.ps1` 调用 Cake.Sdk 文件化脚本 `build/build.cs`。常用参数：`--build=bin|plugin|all`、`--configuration`、`--host-version`、`--plugin-version`、`--sdk-version`、`--plugin`、`--runtime-identifier`、`--self-contained`、`--sdk-feed`、`--sdk-feed-path`。发布目录是 `artifacts/publish`，SDK 包和插件 zip 是 `artifacts/packages`。

开发插件可设置：

```powershell
$env:AVALONIA_EXTRA_PLUGINS_PATH = (Resolve-Path "..\LYBox.Plugins\artifacts\bin\LYBox.Plugin.Template\Debug\net10.0").Path
dotnet run --project src/App/LYBox.Launcher.Desktop
```

测试使用 TUnit 与 Microsoft.Testing.Platform；修改后应运行对应解决方案和 Web SDK 测试。
