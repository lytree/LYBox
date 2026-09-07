# LYBox

基于 Avalonia 12 + .NET 10 的可扩展桌面应用模板，内置插件 SDK、源生成器驱动的插件元数据、AssemblyLoadContext 隔离加载、Fluent Design 视觉规范、EF Core SQLite 持久化、ZLogger 结构化日志。

> 详细的 API 参考、组件指南、版本契约、覆盖安装评估与 FAQ 已拆分到 [`docs/`](docs/) 目录，见文末 [详细文档](#详细文档)。

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
# 1. 一键构建宿主 + SDK NuGet 包 + 所有插件
.\build.ps1 --build=all

# 2. 运行启动器
dotnet run --project src/App/LYBox.Launcher.Desktop
```

Linux/macOS 用 `./build.sh` 替代 `.\build.ps1`。

---

## 构建与运行

- **构建系统**：Cake.Sdk（`build/build.cs` — .NET 10 文件化应用，Cake.Sdk 6.2.0）。通过 `.\build.ps1`（Windows）或 `./build.sh`（Linux/macOS）调用。

  ```
  .\build.ps1 --build=all                    # 默认：bin（启动器 + NuGet）+ plugin
  .\build.ps1 --build=bin                    # 构建启动器 + 打包 SDK NuGet 包（host 与 SDK 同版本号，统一发版）
  .\build.ps1 --build=plugin                 # 构建并打包所有插件为 zip
  .\build.ps1 --configuration=Debug          # 覆盖配置（默认：Release）
  .\build.ps1 --host-version=2.3.0           # 显式覆盖宿主+SDK 版本（优先级最高，覆盖 version.props）
  .\build.ps1 --plugin-version=1.2.3         # 覆盖所有插件版本
  .\build.ps1 --package-version=1.2.3        # 兼容旧用法（覆盖所有层版本，优先级高于 version.props、低于 --host-version）
  .\build.ps1 --plugin=LYBox.Plugin.Template # 仅构建指定插件（逗号分隔多个）
  .\build.ps1 --runtime-identifier=win-x64   # 设置启动器发布的 RID
  .\build.ps1 --self-contained=true          # 启动器自包含发布
  .\build.ps1 --nuget-source=<URL>           # 指定 NuGet 推送源（默认 nuget.org）
  .\build.ps1 --nuget-api-key=<KEY>          # 推送包到 nuget.org
  ```

- **构建顺序很重要**：`--build=bin` 必须先于 `--build=plugin` 运行（或直接使用 `--build=all`），因为 `--build=bin` 会打包 `LYBox.Plugin.Generators`、`LYBox.Plugin.CommandLine`、`LYBox.Plugin.Shared` 与 `LYBox.Plugin.Shared.Web` SDK NuGet 包。
- **直接 `dotnet build`** 可用于单个项目，但若未预先构建本地 NuGet 包，插件可能还原失败（使用 `--build=bin` 或确保 `artifacts/packages/sdk/` 下有 `.nupkg` 文件）。`--build=nuget` 保留为 `--build=bin` 的兼容别名。
- **运行启动器**：`dotnet run --project src/App/LYBox.Launcher.Desktop`
- **运行控制台 CLI**：

  ```powershell
  dotnet run --project src/App/LYBox.Launcher.Console -- version
  dotnet run --project src/App/LYBox.Launcher.Console -- plugins list
  dotnet run --project src/App/LYBox.Launcher.Console -- plugins list --output=json
  dotnet run --project src/App/LYBox.Launcher.Console -- plugins info <plugin-id>
  dotnet run --project src/App/LYBox.Launcher.Console -- plugins install .\LYBox.Plugin.Example-1.0.0.zip
  dotnet run --project src/App/LYBox.Launcher.Console -- plugins uninstall <plugin-id>
  dotnet run --project src/App/LYBox.Launcher.Console -- plugin run sample echo
  dotnet run --project src/App/LYBox.Launcher.Console -- plugin sample echo
  ```

  `plugin run <name> ...` 与旧 `plugin <name> ...` 语法同时支持。命令、参数、别名、帮助和 `--output` 均由 `System.CommandLine` 解析。插件列表、详情、安装、卸载和状态变更与 GUI 共用 `IPluginManagementService`；覆盖安装与卸载需重启主体程序后生效。`version`、`gui` 和 `plugins` 命令不会预先创建完整插件 CLI Host。
- **VS Code 调试**：使用 "Debug Plugin - {Name}" 启动配置 — 每个配置将 `AVALONIA_EXTRA_PLUGINS_PATH` 指向 `artifacts/bin/{ProjectName}/debug`，用于开发期实时加载。
- **测试与 CI**：已有 `tests/LYBox.Tests`（TUnit 测试框架）与 `.github/workflows/` 下的 CI 工作流（`ci.yml`、`release-host.yml`、`release-plugins.yml`）。

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

`plugins/` 保持独立顶层目录。所有生成物统一进入 `artifacts/`，不再使用含义重复的 `bin/bin`。

### 两个解决方案

| 解决方案 | 内容 |
|----------|------|
| `Core.slnx` | 宿主：Generators、CommandLine、Shared、Shared.Web、UI、Launcher、Platforms.Abstractions |
| `Plugins.slnx` | CommandLine、Shared.Web、所有 `plugins/*` 项目（12 个插件） |

### 项目分层（src/）

```
LYBox.Plugin.Generators/        Roslyn 增量源生成器（netstandard2.1，IsRoslynComponent）
LYBox.Plugin.CommandLine/       CLI 契约（netstandard2.1）：IPluginCommandRegistrar、注册上下文
LYBox.Plugin.Shared/            共享契约：IPlugin、IPluginMetadata、ViewLocator、ServiceLocator、特性、控件；转发旧 CLI 类型
LYBox.Plugin.Shared.Web/        WebView、Kestrel、RPC、Event、Channel 与浏览器 SDK
LYBox.Platforms.Abstractions/   跨平台抽象基类（仅空 README）
LYBox.Layout.Core/              宿主布局核心（ViewModels、Services、Resources 等共享布局基础设施）
LYBox.Layout.Ursa/                       宿主应用：ViewModels、Views、Services（EF Core、导航、菜单、本地化、ZLogger）
LYBox.Launcher.Desktop/         桌面入口（Program.cs → App.axaml.cs）。设置 AvaloniaUseCompiledBindingsByDefault=true。
```

### 平台特定项目

`src/Platforms/` 包含：
- `LYBox.Platforms.Windows` — `net10.0-windows10.0.19041.0`
- `LYBox.Platforms.MacOs` — `net10.0-macos15.0`
- `LYBox.Platforms.Linux` — `net10.0`

### 插件项目（plugins/）

每个插件是 `net10.0` 类库，引用 `LYBox.Plugin.Generators`（analyzer，`OutputItemType="Analyzer"`，`ReferenceOutputAssembly="false"`）和 `LYBox.Plugin.Shared`（`PrivateAssets="all"`）。插件元数据通过 MSBuild 属性声明：

```xml
<PluginId>UUID</PluginId>
<PluginName>...</PluginName>
<PluginAuthor>...</PluginAuthor>
<PluginDescription>...</PluginDescription>
<PluginVersion>1.0.0</PluginVersion>  <!-- 可选，缺省回退到 <Version> -->
<MinPluginSdkVersion>2.3.0-preview.3</MinPluginSdkVersion>  <!-- 可选，缺省 "0.0.0" 无约束 -->
```

提供 CLI 命令的新插件应额外直接引用 `LYBox.Plugin.CommandLine`。契约 namespace 仍为 `LYBox.Plugin.Shared.CommandLine`；既有源码无需修改。`LYBox.Plugin.Shared` 保留同版本 NuGet 依赖与 `TypeForwardedTo`，因此只引用旧 Shared 包编译出的插件二进制仍可由新宿主加载。

12 个内置示例插件：BTSou、ButtonsInputs、DateTime、DialogFeedbacks、Downloader、LayoutDisplay、NavigationMenus、ProDataGrid、ScottPlot、TDLSharp、Template、WebTemplate。


### WebView 插件与嵌入式 SDK

`WebTemplate` 为 **WebView 插件**：宿主通过 WebView 承载前端页面。**不再依赖 pnpm / npm / 任何前端构建工具**——前端所需的 SDK（`lybox-plugin-sdk.js`）与主题 CSS（`lybox-plugin-theme.css`）直接以嵌入资源形式打包在 `LYBox.Plugin.Shared.Web` 中，由宿主 `WebHostService` 在 `/sdk/` 路径下提供，前端在 WebView 中即可联调。

```js
import { invoke, on } from "/sdk/lybox-plugin-sdk.js";

var sum = await invoke("AddAsync", { Left: 3, Right: 5 });
const off = on("tick", data => console.log(data));
```

所有 Web 插件共用一个 loopback 端口，静态资源、RPC、SSE 和 session 继续按 `pluginId` 路径注册。

- SDK 资源契约与可用 API 见 [LYBox.Plugin.Shared.Web/PluginWebSdkResources.cs](src/Plugin/LYBox.Plugin.Shared.Web/PluginWebSdkResources.cs)。
- WebView IPC 接入指南见 [docs/WebView-IPC-Guide.md](docs/WebView-IPC-Guide.md)。

### 应用启动流程

```text
Program.cs → App.Initialize()
  1. 通过 ServiceCollectionExtensions.AddAvaloniaServices() 构建 DI 容器
  2. ServiceLocator.Initialize(provider) — 插件代码使用的静态网关
  3. InitializeDatabase() — 通过 EF Core 初始化 SQLite（AppDbContext）
  4. InitializeLocalization() — 恢复已保存的语言设置
  5. 阶段1：pluginLoader.DiscoverAllPluginAssembliesAsync()  发现并加载程序集
  6. 阶段2：pluginLoader.InitializeAllPluginsAsync(services)   插件向 ServiceCollection 注册服务
  7. ServiceProvider = services.BuildServiceProvider()
  8. 阶段3：pluginLoader.RegisterAllPluginsAsync(provider)     插件执行多语言/设置注册
  9. RegisterPluginNavigationAndMenus()                        注册插件导航与菜单
 10. OnFrameworkInitializationCompleted() → 显示启动闪屏，然后显示 MainWindow
```

退出流程（`App.OnShutdownRequested`）：

```
1. 检查 ITaskRegistry 是否有正在运行的任务（仅告警，不阻塞）
2. pluginLoader.ShutdownAllPluginsAsync()  → 调用每个 IPlugin.ShutdownAsync()
3. (ServiceProvider as IDisposable)?.Dispose()  → 释放 Singleton（IDbContextFactory、ZLogger 等）
4. pluginLoader.Dispose()  → 再次 ShutdownAsync（幂等）+ ALC.Unload
5. 取消订阅全局异常事件（TaskScheduler / AppDomain / Dispatcher）
```

### 插件加载与程序集排除

- 每个插件在独立的、可收集的 `AssemblyLoadContext` 中加载（`isCollectible=true`，但运行时不调用 `Unload()`）
- 框架/共享程序集转发到默认上下文（排除清单见 `LYBox.Plugin.Shared.props`/`.targets`）
- 插件通过 `GeneratePluginManifest` target 自动生成 `plugin.json` 清单
- 发现位置：`{AppBaseDir}/plugins/` 与 `AVALONIA_EXTRA_PLUGINS_PATH` 环境变量
- 构建输出：`artifacts/publish/plugins/{Name}/publish/` + `artifacts/packages/plugins/{Name}-{Version}.zip`

---

## 插件系统前提约束

**当前项目不支持插件热加载与热卸载。** 所有插件在应用启动时一次性加载，状态变更（启用/禁用/标记卸载）需重启应用生效。

| 规则 | 说明 |
|------|------|
| **无运行时插件增删** | 插件安装/卸载/启用/禁用均通过修改 `plugin.json` 状态实现，下次启动时生效。UI 中相关操作需提示用户重启。 |
| **无需处理 ALC 卸载清理** | `PluginLoadContext` 虽标记 `isCollectible=true`，但运行时不调用 `Unload()`。`ViewLocator._viewRegistry`、`LocalizationService._resourceManagers`、`MenuConfigurationService._menuItemsMap` 等静态/长生命周期字典无需在运行时清理。 |
| **应用退出需优雅关闭** | `App.OnShutdownRequested` 调用 `IPlugin.ShutdownAsync()` 并 `ServiceProvider.Dispose()`，确保插件持有的原生资源（如 TdLib 客户端）正确释放。 |
| **插件安装冲突处理** | 覆盖安装时若旧插件 ALC 仍持有 DLL 文件锁，安装管理器拒绝覆盖安装并提示用户重启应用后再试。详见 [docs/Plugin-Upgrade-Evaluation.md](docs/Plugin-Upgrade-Evaluation.md)。 |
| **DisablePlugin/EnablePlugin 语义** | 仅修改状态字段并持久化到 manifest，不触发 ALC 卸载/重载。下次启动时按新状态决定是否加载。 |

> 完整前提约束定义见 [`AGENTS.md`](AGENTS.md)。

---

## 插件开发指南

### 最小插件模板

参考 [`plugins/LYBox.Plugin.Template`](plugins/LYBox.Plugin.Template)。

**1. csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>

    <!-- 插件元数据 -->
    <PluginId>YOUR-UUID-HERE</PluginId>
    <PluginName>My Plugin</PluginName>
    <PluginAuthor>Author</PluginAuthor>
    <PluginDescription>Description</PluginDescription>
    <PluginVersion>1.0.0</PluginVersion>

    <!-- 声明本插件所需的最低 Plugin SDK 契约版本 -->
    <MinPluginSdkVersion>2.3.0-preview.3</MinPluginSdkVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LYBox.Plugin.Generators" Version="1.0.0"
      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <PackageReference Include="LYBox.Plugin.Shared" Version="1.0.0" PrivateAssets="all" />
    <!-- 仅 CLI 插件需要；namespace 仍为 LYBox.Plugin.Shared.CommandLine -->
    <PackageReference Include="LYBox.Plugin.CommandLine" Version="1.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**2. 插件入口类**

```csharp
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Attributes;
using LYBox.Plugin.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LYBox.Plugin.MyPlugin;

[GenerateMetadata]  // 触发源生成器自动实现 IPlugin（扫描伴生 ViewModel 上的 [ViewMap]/[NavigationItem]/[Menu]）
public partial class MyPlugin : IPluginMetadata
{
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Author => "Me";
    public string Description => "Demo";
    public IEnumerable<string> Dependencies => [];
    public string PluginId => "YOUR-UUID-HERE";

    public Task InitializeAsync(IServiceCollection services) => Task.CompletedTask;

    public Task RegisterAsync(IServiceProvider serviceProvider)
    {
        // 注册多语言资源
        if (serviceProvider.GetService<ILocalizationService>() is { } loc)
            loc.RegisterResourceManager(Strings.ResourceManager);
        return Task.CompletedTask;
    }
}
```

**3. ViewModel + View（特性驱动）**

```csharp
[NavigationItem("MyDemo")]                                            // 导航 key
[Menu("NAV_MyDemo", "MyDemo", ParentKey = null, Status = "New", Order = 999)]  // 菜单项
[ViewMap(typeof(Pages.MyDemoPage))]                                   // VM→View 映射
public partial class MyDemoViewModel : ViewModelBase
{
    [ObservableProperty] private string _message = "Hello";
    [RelayCommand] private void DoWork() => Message = $"Clicked at {DateTime.Now:T}";
}
```

源生成器会自动把这些特性转换为 `IPlugin.GetViewDefinitions()`、`GetNavigationItems()`、`GetMenuItems()` 的实现，无需手写。

### `MinPluginSdkVersion` 声明规则

- **何时声明**：插件用到了某个 SDK 版本才引入的 API（如新接口成员、新属性）。
- **取值原则**：填入你 **实际依赖的最低 SDK 版本**，不是当前最新版本。
- **不声明的情况**：插件只用了 1.0.0 就稳定存在的 API，可不填 `<MinPluginSdkVersion>`，构建时 `plugin.json` 会写 `"0.0.0"`，宿主视作无约束。
- **运行时校验**：`PluginLoader.IsPluginSdkCompatible` 比对 `Major.Minor.Build` 三段；解析失败按 fail-closed 拒绝加载。详见 [docs/Plugin-SDK-Versioning.md](docs/Plugin-SDK-Versioning.md)。
- **安装期校验**：`PluginInstallationManager` 在解析 manifest 后即调用同一校验，不兼容直接返回失败，避免安装后启动失败。

### 在插件代码中读取当前 SDK 版本

```csharp
using LYBox.Plugin.Shared;

var current = PluginSdkContract.CurrentVersion;  // 编译期常量，字符串形式，例如 "2.1.0"
```

该常量由 `LYBox.Plugin.Shared` NuGet 包内的 `GeneratePluginSdkContract` MSBuild target 在编译期生成（位于 `obj/PluginSdkContract.g.cs`），值来自编译时的 `$(HostVersion)`（SDK 与宿主同版本）。

### 控件与 API 参考

完整的"插件可用控件清单"和"插件可用 API 清单"已拆分到独立文档，避免本 README 过长：

| 主题 | 文档 | 涵盖内容 |
|------|------|---------|
| **控件与样式** | [docs/Plugin-Components-Guide.md](docs/Plugin-Components-Guide.md) | Ursa 控件、Avalonia 内置控件、项目自定义 Fluent 样式类、颜色/画刷资源、图标使用、ServiceLocator、ViewLocator、完整页面示例 |
| **API 参考** | [docs/Plugin-API-Reference.md](docs/Plugin-API-Reference.md) | `IPlugin`/`IPluginMetadata` 接口、特性清单、`ServiceLocator`/`ViewLocator`/`ViewModelBase`、`ILocalizationService`/`ISettingsService`/`ITaskRegistry`/`IPluginLoader`/`IPluginInstallationManager`/`INavigationService`/`IMenuConfigurationService`/`IWindowInfoService` 全部服务接口签名、模型类、MSBuild 属性与目标、完整插件实现示例 |
| **SDK 版本契约** | [docs/Plugin-SDK-Versioning.md](docs/Plugin-SDK-Versioning.md) | 两层版本号（`HostVersion` / `PluginVersion`）、SDK 契约运行时含义、manifest 字段优先级、`IsPluginSdkCompatible` SemVer 比对规则、不兼容时的用户表现 |

---

## 打包与部署

### 构建产物概览

```
artifacts/
├── bin/{ProjectName}/debug/                  # 普通 dotnet build 编译输出
├── obj/{ProjectName}/                        # MSBuild 中间文件
├── publish/
│   ├── launcher/
│   │   ├── desktop/{rid?}/                   # 桌面宿主发布目录
│   │   └── console/{rid?}/                   # 控制台宿主发布目录
│   └── plugins/{PluginName}/publish/         # 插件可加载目录
├── packages/
│   ├── sdk/                                  # Generators / Shared NuGet 包
│   ├── plugins/{PluginName}-{Version}.zip    # 插件分发包
└── test-results/                             # 测试结果
```

> **重要**：SDK NuGet 包与宿主 launcher 共用同一版本号（`HostVersion`），且在 `--build=bin` 一次性产出。不再有独立的 `nuget` 构建目标，二者一起发版。

### 标准构建流程

```powershell
# 1. 一键构建宿主 + SDK NuGet 包（统一发版，产物在 artifacts/publish/launcher/desktop 与 artifacts/packages/sdk）
.\build.ps1 --build=bin

# 2. 构建并打包所有插件（依赖上一步产出的本地 NuGet 包做 restore）
.\build.ps1 --build=plugin

# 或一步到位
.\build.ps1 --build=all
```

### 两层版本覆盖

发版时可通过命令行临时覆盖 csproj 真相源：

| 参数 | 覆盖 | 优先级 |
|------|------|--------|
| `--host-version=2.2.0` | 宿主+SDK 同步版本 | 仅本次构建 |
| `--plugin-version=1.2.0` | 插件版本 | 仅本次构建（影响所有插件） |
| `--package-version=2.2.0` | **所有层**（紧急发版兼容回退） | 覆盖上述未显式指定的层 |

### 构建单插件

```powershell
.\build.ps1 --build=plugin --plugin=LYBox.Plugin.Template
# 多个用逗号分隔
.\build.ps1 --build=plugin --plugin=LYBox.Plugin.Template,LYBox.Plugin.ButtonsInputs
```

### 部署目录结构

宿主运行时扫描两个位置加载插件：

1. `{AppBaseDir}/plugins/` — 默认插件目录
2. `AVALONIA_EXTRA_PLUGINS_PATH` 环境变量 — 开发期临时加载路径

每个插件是一个 **子目录**（不是 zip）：

```
{AppDir}/
├── LYBox.Launcher.Desktop.exe
├── plugins/
│   ├── MyPlugin/
│   │   ├── MyPlugin.dll
│   │   ├── plugin.json          ← 必需
│   │   └── shared-assemblies.txt
│   └── AnotherPlugin/
│       └── ...
└── ...
```

### 安装插件

#### 方式 A：解压 zip 包到 plugins 目录

```powershell
# 假设用户拿到 LYBox.Plugin.Template-1.0.0.zip
Expand-Archive .\LYBox.Plugin.Template-1.0.0.zip -DestinationPath .\plugins\LYBox.Plugin.Template\
```

#### 方式 B：通过 `AVALONIA_EXTRA_PLUGINS_PATH` 临时加载（开发期）

VS Code launch.json 已为每个插件配置了对应 launch config，设置环境变量指向 `artifacts/bin/{ProjectName}/debug`，可直接热加载调试。

```powershell
$env:AVALONIA_EXTRA_PLUGINS_PATH = (Resolve-Path "artifacts/bin/LYBox.Plugin.Template/debug").Path
dotnet run --project src/App/LYBox.Launcher.Desktop
```

#### 方式 C：通过 `IPluginInstallationManager` 编程安装

```csharp
var installer = ServiceLocator.GetService<IPluginInstallationManager>();
var result = await installer.InstallFromFileAsync(zipPath, progress);
if (result.Success)
{
    // 安装成功，提示用户重启应用以加载新插件
}
```

> **覆盖安装与版本升级方案**：已加载插件因 DLL 锁定无法直接覆盖安装。详细评估"临时目录 + 重启迁移"方案（可行性、风险、实施工作量）见 [docs/Plugin-Upgrade-Evaluation.md](docs/Plugin-Upgrade-Evaluation.md)。

---

## 关键文件参考

| 类别 | 文件 |
|------|------|
| **构建系统** | [build/build.cs](build/build.cs)、[Directory.Build.props](Directory.Build.props)、[build.ps1](build.ps1) |
| **应用入口** | [src/App/LYBox.Launcher.Desktop/Program.cs](src/App/LYBox.Launcher.Desktop/Program.cs)、[App.axaml.cs](src/App/LYBox.Launcher.Desktop/App.axaml.cs) |
| **插件契约** | [src/Plugin/LYBox.Plugin.Shared/IPlugin.cs](src/Plugin/LYBox.Plugin.Shared/IPlugin.cs)、[IPluginMetadata.cs](src/Plugin/LYBox.Plugin.Shared/IPluginMetadata.cs)、[PluginSdkContract.cs](src/Plugin/LYBox.Plugin.Shared/PluginSdkContract.cs) |
| **CLI 契约** | [src/Plugin/LYBox.Plugin.CommandLine/IPluginCommandRegistrar.cs](src/Plugin/LYBox.Plugin.CommandLine/IPluginCommandRegistrar.cs)、[类型转发](src/Plugin/LYBox.Plugin.Shared/CommandLine/TypeForwards.cs) |
| **插件加载** | [src/Layout/LYBox.Layout.Ursa/Services/PluginLoader.cs](src/Layout/LYBox.Layout.Ursa/Services/PluginLoader.cs)、[PluginLoadContext.cs](src/Layout/LYBox.Layout.Ursa/Services/PluginLoadContext.cs) |
| **插件安装** | [src/Layout/LYBox.Layout.Ursa/Services/PluginInstallationManager.cs](src/Layout/LYBox.Layout.Ursa/Services/PluginInstallationManager.cs) |
| **导航与菜单** | [src/Layout/LYBox.Layout.Ursa/Services/NavigationService.cs](src/Layout/LYBox.Layout.Ursa/Services/NavigationService.cs)、[MenuConfigurationService.cs](src/Layout/LYBox.Layout.Ursa/Services/MenuConfigurationService.cs) |
| **本地化** | [src/Layout/LYBox.Layout.Ursa/Services/LocalizationService.cs](src/Layout/LYBox.Layout.Ursa/Services/LocalizationService.cs) |
| **设置** | [src/Layout/LYBox.Layout.Ursa/Services/SettingsService.cs](src/Layout/LYBox.Layout.Ursa/Services/SettingsService.cs)、[src/Plugin/LYBox.Plugin.Shared/Models/SettingDefinition.cs](src/Plugin/LYBox.Plugin.Shared/Models/SettingDefinition.cs) |
| **任务注册** | [src/Plugin/LYBox.Plugin.Shared/TaskScope.cs](src/Plugin/LYBox.Plugin.Shared/TaskScope.cs)、[src/Layout/LYBox.Layout.Ursa/Services/TaskRegistry.cs](src/Layout/LYBox.Layout.Ursa/Services/TaskRegistry.cs) |
| **视图解析** | [src/Plugin/LYBox.Plugin.Shared/ViewLocator.cs](src/Plugin/LYBox.Plugin.Shared/ViewLocator.cs) |
| **源生成器** | [src/Plugin/LYBox.Plugin.Generators/](src/Plugin/LYBox.Plugin.Generators/) |
| **共享程序集配置** | [src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.props](src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.props)、[.targets](src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.targets) |
| **主题与样式** | [src/Layout/LYBox.Layout.Ursa/Theme/UrsaSemiTheme.axaml](src/Layout/LYBox.Layout.Ursa/Theme/UrsaSemiTheme.axaml)、[FluentDesign/FluentDesignStyles.axaml](src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml) |
| **示例插件** | [plugins/LYBox.Plugin.Template/](plugins/LYBox.Plugin.Template/)、[plugins/LYBox.Plugin.TDLSharp/](plugins/LYBox.Plugin.TDLSharp/)、[plugins/LYBox.Plugin.Downloader/](plugins/LYBox.Plugin.Downloader/) |

---

## 详细文档

以下文档位于 [`docs/`](docs/) 目录，按主题分类详细展开：

| 文档 | 主题 | 适用读者 |
|------|------|---------|
| [docs/Plugin-Components-Guide.md](docs/Plugin-Components-Guide.md) | **插件可用组件指南** — Ursa 控件清单、Avalonia 内置控件、项目自定义 Fluent 样式类、颜色/画刷资源、Fluent Icons、ServiceLocator、ViewLocator、完整插件页面示例 | 插件 UI 开发者 |
| [docs/Plugin-API-Reference.md](docs/Plugin-API-Reference.md) | **插件 API 参考** — `IPlugin`/`IPluginMetadata` 接口、源生成器特性、`ServiceLocator`/`ViewLocator`/`ViewModelBase` 基础设施类、`MenuItemViewModel`/`ToolBarItemViewModel` 视图模型、`PluginManifest`/`PluginInfo`/`PluginState`/`SettingDefinition` 模型、`ILocalizationService`/`IPluginLoader`/`IPluginInstallationManager`/`ISettingsService`/`ITaskRegistry`/`IWindowInfoService` 全部服务接口、MSBuild 属性与目标、完整插件实现示例 | 插件开发者 |
| [docs/Plugin-SDK-Versioning.md](docs/Plugin-SDK-Versioning.md) | **插件 SDK 版本契约** — `HostVersion`/`PluginVersion` 两层版本号、SDK 契约的运行时含义、`plugin.json` 中 `minPluginSdkVersion` 字段优先级、`IsPluginSdkCompatible` SemVer 比对规则、不兼容时的用户表现 | 宿主维护者、插件作者 |
| [docs/Plugin-Upgrade-Evaluation.md](docs/Plugin-Upgrade-Evaluation.md) | **插件覆盖安装与升级方案评估** — 背景、方案设计（`.pending` 目录 + 重启迁移）、8 维度可行性评估、7 个潜在问题与对策、实现工作量评估、总结与建议实施顺序 | 宿主维护者 |
| [docs/WebView-IPC-Guide.md](docs/WebView-IPC-Guide.md) | **WebView 插件 IPC 指南** — 前端与宿主双向通信、嵌入式 SDK（`/sdk/lybox-plugin-sdk.js`）与主题 CSS 用法、`WebTemplate` 插件接入流程 | 前端/WebView 插件开发者 |
| [docs/FAQ.md](docs/FAQ.md) | **常见问题** — 插件加载失败、菜单/导航不显示、设置项不显示、第三方 NuGet 包使用、全局快捷键、插件间通信、源生成器调试、`PluginLoadContext` 的 `isCollectible` 设计意图 | 所有开发者 |

> 项目开发约束与前提（含插件系统强制约束）见 [`AGENTS.md`](AGENTS.md)。
