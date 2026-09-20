# LYBox

基于 **Avalonia 12 + .NET 10** 的可扩展桌面应用模板。内置插件 SDK、源生成器驱动的元数据、AssemblyLoadContext 隔离加载、Fluent Design 视觉规范、EF Core SQLite 持久化、ZLogger 结构化日志。

仓库由两部分组成：

| 仓库 | 内容 |
|------|------|
| [`LYBox`](.) | 宿主、布局、平台抽象、插件 SDK、源生成器、启动器、测试 |
| [`LYBox.Plugins`](../LYBox.Plugins) | 12 个内置示例插件 + 2 个脚手架模板 |

---

## 目录

- [快速开始](#快速开始)
- [构建与运行](#构建与运行)
- [架构概览](#架构概览)
- [插件系统前提约束](#插件系统前提约束)
- [插件开发指南](#插件开发指南)
- [打包与部署](#打包与部署)
- [关键文件参考](#关键文件参考)
- [详细文档](#详细文档)

---

## 快速开始

```powershell
# 1. 构建宿主 + SDK NuGet 包
.\build.ps1 --build=bin

# 2. 在 LYBox.Plugins 仓库构建所有插件
cd ..\LYBox.Plugins
.\build.ps1

# 3. 启动宿主
cd ..\LYBox
dotnet run --project src/App/LYBox.Launcher.Desktop
```

Linux/macOS 用 `./build.sh` 替代 `.\build.ps1`。

---

## 构建与运行

### 构建系统

[Cake.Sdk](https://github.com/cake-build/cake) 文件化应用（`build/build.cs`，Cake.Sdk **6.2.0**）。通过 `.\build.ps1`（Windows）或 `./build.sh`（Linux/macOS）调用。

### 常用命令

```powershell
# 构建宿主 + SDK NuGet 包（host 与 SDK 同版本）
.\build.ps1 --build=bin

# 构建并打包所有插件（在 LYBox.Plugins 仓库）
.\build.ps1

# 一键构建所有产物
.\build.ps1 --build=all

# 覆盖配置
.\build.ps1 --configuration=Debug

# 覆盖宿主与 SDK 版本（优先级最高）
.\build.ps1 --host-version=2.3.0

# 覆盖所有层版本（兼容旧用法）
.\build.ps1 --package-version=1.2.3

# 启动器发布参数
.\build.ps1 --runtime-identifier=win-x64
.\build.ps1 --self-contained=true

# NuGet 推送
.\build.ps1 --nuget-source=<URL>
.\build.ps1 --nuget-api-key=<KEY>
```

> 插件构建命令（`--build=plugin`、`--plugin=`、`--plugin-version=`）已迁移到 [`LYBox.Plugins`](../LYBox.Plugins) 仓库。

### 运行

```powershell
# 桌面启动器
dotnet run --project src/App/LYBox.Launcher.Desktop

# 控制台 CLI
dotnet run --project src/App/LYBox.Launcher.Console -- version
dotnet run --project src/App/LYBox.Launcher.Console -- plugins list
dotnet run --project src/App/LYBox.Launcher.Console -- plugins list --output=json
dotnet run --project src/App/LYBox.Launcher.Console -- plugins info <plugin-id>
dotnet run --project src/App/LYBox.Launcher.Console -- plugins install <zip-path>
dotnet run --project src/App/LYBox.Launcher.Console -- plugins uninstall <plugin-id>
dotnet run --project src/App/LYBox.Launcher.Console -- plugin run <name> [args...]
```

### VS Code 调试

每个插件都有 "Debug Plugin - {Name}" 启动配置（位于 [`LYBox.Plugins/.vscode/launch.json`](../LYBox.Plugins/.vscode/launch.json)），自动设置 `AVALONIA_EXTRA_PLUGINS_PATH` 指向插件的 build 输出。

### 测试与 CI

- 测试项目：`tests/LYBox.Tests`（TUnit）
- CI：[`.github/workflows/ci.yml`](.github/workflows/ci.yml)、[`release-host.yml`](.github/workflows/release-host.yml)
- 插件仓库 CI：`LYBox.Plugins/.github/workflows/`

---

## 架构概览

### 源码与产物布局

```text
src/
  App/          桌面与控制台启动器
  Layout/       布局核心与 Ursa UI
  Plugin/       插件契约与源生成器
  Platforms/    跨平台抽象及平台实现
artifacts/
  bin/          普通 dotnet build 输出（按项目隔离）
  obj/          MSBuild 中间产物
  publish/      Launcher 与插件发布目录
  packages/     SDK NuGet 与插件 zip
  test-results/ 测试报告
```

### 两个解决方案

| 解决方案 | 内容 |
|----------|------|
| `Core.slnx` | 宿主：Generators、CommandLine、Shared、Shared.Web、UI、Launcher、Platforms.Abstractions |
| `Plugins.slnx`（在 `LYBox.Plugins`） | CommandLine、Shared.Web、所有 12 个插件项目 |

### 项目分层

```
LYBox.Plugin.Generators/        Roslyn 增量源生成器（netstandard2.1）
LYBox.Plugin.CommandLine/       CLI 契约（netstandard2.1）
LYBox.Plugin.Shared/            共享契约：IPlugin、IPluginMetadata、ViewLocator、特性、控件
LYBox.Plugin.Shared.Web/        WebView、Kestrel、RPC、Event、Channel、嵌入式浏览器 SDK
LYBox.Platforms.Abstractions/   跨平台抽象基类
LYBox.Layout.Core/              宿主布局核心
LYBox.Layout.Ursa/              宿主应用：导航、菜单、本地化、EF Core、ZLogger
LYBox.Launcher.Desktop/         桌面入口
LYBox.Launcher.Console/         CLI 入口
```

### 平台项目

- `LYBox.Platforms.Windows` — `net10.0-windows10.0.19041.0`
- `LYBox.Platforms.MacOs` — `net10.0-macos15.0`
- `LYBox.Platforms.Linux` — `net10.0`

### WebView 插件与嵌入式 SDK

`WebTemplate` 为 **WebView 插件**：宿主通过 WebView 承载前端页面。**不再依赖任何前端构建工具** —— 前端 SDK（`lybox-plugin-sdk.js`）与主题 CSS（`lybox-plugin-theme.css`）作为嵌入资源打包在 `LYBox.Plugin.Shared.Web` 中，由宿主 `WebHostService` 在 `/sdk/` 路径下提供。

```javascript
import { invoke, on } from "/sdk/lybox-plugin-sdk.js";

const sum = await invoke("AddAsync", { Left: 3, Right: 5 });
const off = on("tick", data => console.log(data));
```

所有 Web 插件共用一个 loopback 端口，静态资源、RPC、SSE、session 继续按 `pluginId` 路径注册。

- SDK 资源契约与可用 API：[`LYBox.Plugin.Shared.Web/PluginWebSdkResources.cs`](src/Plugin/LYBox.Plugin.Shared.Web/PluginWebSdkResources.cs)
- WebView IPC 接入指南：[`docs/WebView-IPC-Guide.md`](docs/WebView-IPC-Guide.md)

### 应用启动流程

```text
Program.cs → App.Initialize()
  1. 通过 AddAvaloniaServices() 构建 DI 容器
  2. DiscoverAllPluginAssembliesAsync()    发现并加载插件程序集
  3. InitializeAllPluginsAsync(services)   插件向 ServiceCollection 注册服务
  4. ServiceProvider = services.BuildServiceProvider()
  5. ServiceLocator.Initialize(provider)
  6. InitializeDatabase()                  EF Core SQLite
  7. InitializeLocalization()              恢复已保存的语言
  8. RegisterAllPluginsAsync(provider)    多语言/设置注册
  9. RegisterPluginNavigationAndMenus()   注册视图/导航/菜单
 10. InitializeWebHost()                   仅 Web 插件存在时懒启动 Kestrel
 11. OnFrameworkInitializationCompleted() → 显示主窗口
```

退出流程（`App.OnShutdownRequested`）：检查运行任务 → `ShutdownAsync()` → `ServiceProvider.Dispose()` → 取消全局异常订阅。

### 插件加载与程序集排除

- 每个插件在独立的、可收集的 `AssemblyLoadContext` 中加载（`isCollectible=true`，运行时不调用 `Unload()`）
- 框架/共享程序集转发到默认上下文（排除清单见 `LYBox.Plugin.Shared.props/.targets`）
- 插件通过 `GeneratePluginManifest` 自动生成 `plugin.json`
- 发现位置：`{AppBaseDir}/plugins/` 与 `AVALONIA_EXTRA_PLUGINS_PATH` 环境变量
- 外部开发目录是只读的

---

## 插件系统前提约束

**当前不支持插件热加载与热卸载。** 所有插件在应用启动时一次性加载，状态变更（启用/禁用/卸载）需重启生效。

| 规则 | 说明 |
|------|------|
| 无运行时增删 | 通过修改 `plugin.json` 状态实现，下次启动生效 |
| 无需处理 ALC 卸载清理 | 静态/长生命周期字典无需运行时清理 |
| 应用退出需优雅关闭 | `ShutdownAsync()` + `ServiceProvider.Dispose()` |
| 安装冲突处理 | 覆盖安装若 DLL 被锁，拒绝并提示重启 |
| Disable/Enable 语义 | 仅修改状态字段，不触发 ALC 卸载/重载 |

> 完整前提约束见 [`AGENTS.md`](AGENTS.md)。

---

## 插件开发指南

### 最小插件模板

参考 [`LYBox.Plugins/templates/plugin-template-aot`](../LYBox.Plugins/templates/plugin-template-aot)。

**1. csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>

    <PluginId>YOUR-UUID</PluginId>
    <PluginName>My Plugin</PluginName>
    <PluginAuthor>Author</PluginAuthor>
    <PluginDescription>Description</PluginDescription>
    <PluginVersion>1.0.0</PluginVersion>
    <MinPluginSdkVersion>2.3.0-preview.3</MinPluginSdkVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LYBox.Plugin.Generators" Version="$(PluginSdkVersion)"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <PackageReference Include="LYBox.Plugin.Shared" Version="$(PluginSdkVersion)" PrivateAssets="all" />
    <!-- CLI 插件需要： -->
    <PackageReference Include="LYBox.Plugin.CommandLine" Version="$(PluginSdkVersion)" PrivateAssets="all" />
    <!-- Web 插件需要： + 额外声明 <PluginKind>Web</PluginKind> -->
    <!-- <PackageReference Include="LYBox.Plugin.Shared.Web" Version="$(PluginSdkVersion)" PrivateAssets="all" /> -->
  </ItemGroup>
</Project>
```

**2. 入口类**

```csharp
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace LYBox.Plugin.MyPlugin;

[GenerateMetadata]   // 触发源生成器自动实现 IPlugin
public partial class MyPlugin : IPluginMetadata
{
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Author => "Me";
    public string Description => "Demo";
    public string PluginId => "YOUR-UUID";
    public string MinPluginSdkVersion => PluginSdkContract.CurrentVersion;

    public Task InitializeAsync(IServiceCollection services) => Task.CompletedTask;

    public Task RegisterAsync(IServiceProvider sp)
    {
        if (sp.GetService<ILocalizationService>() is { } loc)
            loc.RegisterResourceManager(Strings.ResourceManager);
        return Task.CompletedTask;
    }
}
```

**3. ViewModel + View（特性驱动）**

```csharp
[NavigationItem("MyDemo")]
[Menu("NAV_MyDemo", "MyDemo", ParentKey = null, Status = "New", Order = 999)]
[ViewMap(typeof(MyDemoPage))]
public partial class MyDemoViewModel : ViewModelBase
{
    [ObservableProperty] private string _message = "Hello";
    [RelayCommand] private void DoWork() => Message = $"Clicked at {DateTime.Now:T}";
}
```

源生成器会自动把这些特性转换为 `IGeneratedPluginModule.Ui` 描述符，宿主启动时统一注册。

### `MinPluginSdkVersion` 声明规则

- **何时声明**：插件用到了某个 SDK 版本才引入的 API。
- **取值原则**：填入**实际依赖的最低 SDK 版本**，不是当前最新版本。
- **不声明**：可不填 `<MinPluginSdkVersion>`，构建时 `plugin.json` 写 `"0.0.0"`，宿主视作无约束。
- **运行时校验**：`PluginLoader.IsPluginSdkCompatible` 比对 `Major.Minor.Build`；解析失败按 fail-closed 拒绝加载。
- **安装期校验**：`PluginInstallationManager` 同样校验，避免安装后启动失败。

### 控件与 API 参考

| 主题 | 文档 |
|------|------|
| **控件与样式** | [docs/Plugin-Components-Guide.md](docs/Plugin-Components-Guide.md) |
| **API 参考** | [docs/Plugin-API-Reference.md](docs/Plugin-API-Reference.md) |
| **SDK 版本契约** | [docs/Plugin-SDK-Versioning.md](docs/Plugin-SDK-Versioning.md) |
| **WebView IPC** | [docs/WebView-IPC-Guide.md](docs/WebView-IPC-Guide.md) |
| **FAQ** | [docs/FAQ.md](docs/FAQ.md) |

---

## 打包与部署

### 构建产物概览

```text
artifacts/
├── bin/{ProjectName}/debug/                  # 普通 dotnet build 输出
├── obj/{ProjectName}/                        # MSBuild 中间文件
├── publish/
│   ├── launcher/{desktop|console}/{rid?}/    # 宿主发布目录
│   └── plugins/{PluginName}/publish/         # 插件可加载目录
├── packages/
│   ├── sdk/                                  # SDK NuGet 包
│   └── plugins/{PluginName}-{Version}.zip    # 插件分发包
└── test-results/                             # 测试结果
```

### 标准构建流程

```powershell
# LYBox 仓库：构建宿主 + SDK NuGet 包
.\build.ps1 --build=bin

# LYBox.Plugins 仓库：构建并打包所有插件
cd ..\LYBox.Plugins
.\build.ps1
```

### 版本覆盖

| 参数 | 覆盖 | 优先级 |
|------|------|--------|
| `--host-version=2.2.0` | 宿主+SDK 同步版本 | 最高 |
| `LYBOX_HOST_VERSION` 环境变量 | 宿主+SDK | 中 |
| `version.props` 的 `<LyboxVersion>` | 宿主+SDK | 默认（唯一真相源） |
| `--package-version=2.2.0` | 所有层 | 紧急兼容 |

### 部署目录结构

```text
{AppDir}/
├── LYBox.Launcher.Desktop.exe
├── plugins/
│   ├── MyPlugin/
│   │   ├── MyPlugin.dll
│   │   ├── plugin.json
│   │   └── shared-assemblies.txt
│   └── AnotherPlugin/
└── ...
```

### 安装插件

**方式 A：解压 zip 到 plugins 目录**

```powershell
Expand-Archive .\LYBox.Plugin.Template-1.0.0.zip -DestinationPath .\plugins\LYBox.Plugin.Template\
```

**方式 B：通过 `AVALONIA_EXTRA_PLUGINS_PATH` 临时加载（开发期）**

```powershell
$env:AVALONIA_EXTRA_PLUGINS_PATH = (Resolve-Path "..\LYBox.Plugins\artifacts\bin\LYBox.Plugin.Template\debug").Path
dotnet run --project src/App/LYBox.Launcher.Desktop
```

**方式 C：通过 `IPluginInstallationManager` 编程安装**

```csharp
var installer = ServiceLocator.GetService<IPluginInstallationManager>();
var result = await installer.InstallFromFileAsync(zipPath, progress);
if (result.Success)
{
    // 提示用户重启应用
}
```

> 覆盖安装与版本升级方案：[docs/Plugin-Upgrade-Evaluation.md](docs/Plugin-Upgrade-Evaluation.md)

---

## 关键文件参考

| 类别 | 路径 |
|------|------|
| **构建系统** | [build/build.cs](build/build.cs)、[Directory.Build.props](Directory.Build.props)、[build.ps1](build.ps1) |
| **应用入口** | [Program.cs](src/App/LYBox.Launcher.Desktop/Program.cs)、[App.axaml.cs](src/App/LYBox.Launcher.Desktop/App.axaml.cs) |
| **插件契约** | [IPlugin.cs](src/Plugin/LYBox.Plugin.Shared/IPlugin.cs)、[IPluginMetadata.cs](src/Plugin/LYBox.Plugin.Shared/IPluginMetadata.cs)、[PluginSdkContract.cs](src/Plugin/LYBox.Plugin.Shared/PluginSdkContract.cs) |
| **CLI 契约** | [IPluginCommandRegistrar.cs](src/Plugin/LYBox.Plugin.CommandLine/IPluginCommandRegistrar.cs) |
| **插件加载** | [PluginLoader.cs](src/Layout/LYBox.Layout.Ursa/Services/PluginLoader.cs)、[PluginLoadContext.cs](src/Layout/LYBox.Layout.Ursa/Services/PluginLoadContext.cs) |
| **插件安装** | [PluginInstallationManager.cs](src/Layout/LYBox.Layout.Ursa/Services/PluginInstallationManager.cs) |
| **导航/菜单** | [NavigationService.cs](src/Layout/LYBox.Layout.Ursa/Services/NavigationService.cs)、[MenuConfigurationService.cs](src/Layout/LYBox.Layout.Ursa/Services/MenuConfigurationService.cs) |
| **本地化** | [LocalizationService.cs](src/Layout/LYBox.Layout.Ursa/Services/LocalizationService.cs) |
| **设置** | [SettingsService.cs](src/Layout/LYBox.Layout.Ursa/Services/SettingsService.cs)、[SettingDefinition.cs](src/Plugin/LYBox.Plugin.Shared/Models/SettingDefinition.cs) |
| **任务注册** | [TaskRegistry.cs](src/Layout/LYBox.Layout.Ursa/Services/TaskRegistry.cs) |
| **视图解析** | [ViewLocator.cs](src/Plugin/LYBox.Plugin.Shared/ViewLocator.cs) |
| **源生成器** | [src/Plugin/LYBox.Plugin.Generators/](src/Plugin/LYBox.Plugin.Generators/) |
| **主题/样式** | [UrsaFluentTheme.axaml](src/Layout/LYBox.Layout.Ursa/Theme/UrsaSemiTheme.axaml)、[FluentDesignStyles.axaml](src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml) |

---

## 详细文档

所有文档位于 [`docs/`](docs/)：

| 文档 | 主题 |
|------|------|
| [docs/USAGE.md](docs/USAGE.md) | 当前功能与使用说明 |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | 开发与架构 |
| [docs/Plugin-Components-Guide.md](docs/Plugin-Components-Guide.md) | 插件可用组件指南 |
| [docs/Plugin-API-Reference.md](docs/Plugin-API-Reference.md) | 插件 API 参考 |
| [docs/Plugin-SDK-Versioning.md](docs/Plugin-SDK-Versioning.md) | 插件 SDK 版本契约 |
| [docs/Plugin-Upgrade-Evaluation.md](docs/Plugin-Upgrade-Evaluation.md) | 插件覆盖安装与升级方案 |
| [docs/WebView-IPC-Guide.md](docs/WebView-IPC-Guide.md) | WebView 插件 IPC 指南 |
| [docs/WEBVIEW_IPC.md](docs/WEBVIEW_IPC.md) | WebView IPC 当前实现 |
| [docs/WEB_PLUGIN_GUIDE.md](docs/WEB_PLUGIN_GUIDE.md) | Web 插件指南 |
| [docs/WebHost-Optimization-Design.md](docs/WebHost-Optimization-Design.md) | WebHost 优化设计方案 |
| [docs/FAQ.md](docs/FAQ.md) | 常见问题 |

> 项目开发约束与前提（含插件系统强制约束）见 [`AGENTS.md`](AGENTS.md)。
