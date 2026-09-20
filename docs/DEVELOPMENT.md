# LYBox 开发与架构

面向开发者。聚焦仓库边界、分层、启动流程、SDK 与构建调试。

---

## 1. 仓库边界

LYBox 是双仓库结构：

| 仓库 | 角色 |
|---|---|
| [`LYBox`](../) | 宿主、布局、平台抽象、插件 SDK、源生成器、启动器、测试 |
| [`LYBox.Plugins`](../../LYBox.Plugins) | 12 个内置示例插件 + 2 个脚手架模板 |

两个仓库通过 SDK NuGet 包协作：`LYBox` 用 `--build=bin` 打包 SDK 到 `artifacts/packages/sdk/`；`LYBox.Plugins` 把本地 feed 指向该目录后构建插件。

---

## 2. 分层

```text
Plugin.Generators    Roslyn 增量生成器（netstandard2.1）
Plugin.CommandLine   CLI 契约
Plugin.Shared        IPlugin、服务接口、模型、控件、定位器
Plugin.Shared.Web    WebPluginView、Kestrel、RPC、SSE、Channel、浏览器 SDK
Layout.Core          插件加载/安装、数据库、设置、任务、日志
Layout.Ursa          导航、菜单、本地化、主题、主界面
Launcher.Desktop     Avalonia 桌面入口
Launcher.Console     CLI 入口
Platforms.*          跨平台抽象与实现
```

- `Core.slnx` 面向宿主
- `Plugins.slnx` 面向插件，扫描 `plugins/*.csproj`

---

## 3. 启动流程

```text
App.Initialize()
  1. AddAvaloniaServices()              注册核心 DI、日志、SQLite、设置、任务、窗口
  2. DiscoverAllPluginAssembliesAsync() 扫描 plugins/ + AVALONIA_EXTRA_PLUGINS_PATH
  3. InitializeAllPluginsAsync(services) 校验 MinPluginSdkVersion、创建 ALC
  4. BuildServiceProvider + ServiceLocator.Initialize
  5. InitializeDatabase()               EF Core migrations
  6. InitializeLocalization()           恢复语言和设置
  7. RegisterAllPluginsAsync(provider) 本地化、运行时资源、业务
  8. RegisterPluginNavigationAndMenus() 汇总 View/Navigation/Menu
  9. InitializeWebHost()                仅 Web 插件时懒启动 Kestrel
 10. OnFrameworkInitializationCompleted() 创建主窗口
```

退出流程：检查运行任务 → `ShutdownAsync()` → `ServiceProvider.Dispose()` → 取消全局异常订阅。

---

## 4. 插件生命周期与隔离

### 状态机

```
NotInstalled → Installed → Loaded → Disabled → PendingUninstall
              ↓                       ↓
        PendingUpgrade            PendingUpgrade
              ↓
            Error
```

### 隔离

- 每个插件独立 `AssemblyLoadContext`（`isCollectible=true`，运行时不调用 `Unload()`）
- 框架/共享程序集按 `LYBox.Plugin.Shared.props/.targets` 排除清单转发到默认 ALC
- 第三方程序集默认私有加载；需要共享时通过 `shared-assemblies.txt` 声明
- **不支持热加载与热卸载** —— 状态变更需重启生效

### SDK 引用

| 插件类型 | PackageReference |
|---|---|
| 所有插件 | `LYBox.Plugin.Generators`（analyzer）+ `LYBox.Plugin.Shared` |
| CLI 插件 | + `LYBox.Plugin.CommandLine` |
| Web 插件 | + `LYBox.Plugin.Shared.Web` + `<PluginKind>Web</PluginKind>` |

### 元数据来源

元数据全部从 csproj 属性注入（单一事实来源）：

```xml
<PluginId>UUID</PluginId>
<PluginName>...</PluginName>
<PluginAuthor>...</PluginAuthor>
<PluginDescription>...</PluginDescription>
<PluginVersion>1.0.0</PluginVersion>      <!-- 可选，缺省回退到 <Version> -->
<MinPluginSdkVersion>2.3.0-preview.3</MinPluginSdkVersion>
<PluginKind>Avalonia|Web</PluginKind>
<PluginWwwroot>wwwroot</PluginWwwroot>   <!-- 仅 Web -->
<PluginEntryPage>index.html</PluginEntryPage>  <!-- 仅 Web -->
```

CLI 插件可设置：`GeneratePluginCliIndex`、`PluginCliAlias`、`PluginCliDescription`、`PluginCliRuntimeProfile`、`PluginCliOutputModes`。

---

## 5. SDK 与生成器

### 源生成器驱动特性

| 特性 | 作用 |
|---|---|
| `[GenerateMetadata]` | 自动实现 `IPlugin` + `IPluginMetadata` |
| `[ViewMap(typeof(View))]` | VM→View 映射 |
| `[NavigationItem("key")]` | 注册导航 key |
| `[Menu(header, key, ParentKey=..., IconName=..., Status=..., Order=...)]` | 注册菜单 |
| `[RpcCommand]` | 生成 Web RPC 绑定、JS 客户端、`.d.ts` 声明 |

### 生成产物

`{Plugin}.Module.g.cs` 实现 `IGeneratedPluginModule`：

```csharp
public interface IGeneratedPluginModule
{
    Type PluginType { get; }
    IPlugin CreatePlugin();
    IPluginMetadata Metadata { get; }
    GeneratedPluginUiDescriptor Ui { get; }   // Views / NavigationItems / MenuItems
}
```

宿主 `App.RegisterPluginNavigationAndMenus` 经 `PluginLoader.GetGeneratedModule(pluginId)` 消费，**单一 UI 注册轨道**。

### SDK 版本常量

```csharp
using LYBox.Plugin.Shared;
var current = PluginSdkContract.CurrentVersion;   // 编译期常量
```

由 `LYBox.Plugin.Shared` 包内的 `GeneratePluginSdkContract` MSBuild target 在 `obj/PluginSdkContract.g.cs` 生成。

---

## 6. 构建、调试和发布

### 入口

```powershell
# LYBox 仓库
.\build.ps1 --build=bin
.\build.ps1 --build=publish-nuget --nuget-source=<URL> --nuget-api-key=<KEY>

# LYBox.Plugins 仓库
.\build.ps1 --build=plugin --sdk-feed=local
.\build.ps1 --build=plugin --plugin=LYBox.Plugin.Template
```

构建入口：`build/build.cs`（Cake.Sdk 文件化应用，Cake.Sdk 6.2.0）。

### 常用参数

| 参数 | 作用 |
|---|---|
| `--build=bin` | 启动器 + SDK NuGet |
| `--build=plugin` | 插件构建与打包（仅 `LYBox.Plugins`） |
| `--build=publish-nuget` | 打包并推送 SDK NuGet |
| `--build=all` | 等价 bin + plugin |
| `--configuration=Debug\|Release` | 默认 Release |
| `--host-version=<ver>` | 覆盖宿主+SDK 版本（最高优先级） |
| `--plugin-version=<ver>` | 覆盖所有插件版本 |
| `--sdk-version=<ver>` | 覆盖 SDK 包版本 |
| `--plugin=<Name>` | 仅构建指定插件（逗号分隔多个） |
| `--runtime-identifier=<rid>` | 启动器发布 RID |
| `--self-contained=true` | 启动器自包含 |
| `--sdk-feed=local\|nuget` | SDK 解析源 |
| `--sdk-feed-path=<dir>` | 本地 feed 目录 |

### 产物

```text
artifacts/
├── publish/                         # 启动器与插件发布
│   ├── launcher/{desktop|console}/{rid?}/
│   └── plugins/{PluginName}/publish/
├── packages/
│   ├── sdk/                         # SDK NuGet 包（Generators / Shared / Shared.Web / CommandLine）
│   └── plugins/{PluginName}-{Version}.zip
├── bin/{ProjectName}/debug/         # 普通 dotnet build 输出
├── obj/{ProjectName}/               # MSBuild 中间产物
└── test-results/
```

### 开发插件调试

```powershell
# 终端 1：构建宿主（Debug）
.\build.ps1 --configuration=Debug

# 终端 2：在 LYBox.Plugins 仓库构建并启动插件调试
cd ..\LYBox.Plugins
dotnet build plugins\LYBox.Plugin.Template\LYBox.Plugin.Template.csproj -c Debug
$env:AVALONIA_EXTRA_PLUGINS_PATH = (Resolve-Path "artifacts\bin\LYBox.Plugin.Template\Debug\net10.0").Path
& "..\LYBox\artifacts\bin\LYBox.Launcher.Desktop\Debug\LYBox.Launcher.Desktop.exe"
```

或直接用 VS Code 的 "Debug Plugin - {Name}" 启动配置（位于 `LYBox.Plugins/.vscode/launch.json`）。

### 测试

测试项目：`tests/LYBox.Tests`，框架 TUnit（`1.63.0`），运行器 Microsoft.Testing.Platform。

```powershell
dotnet test tests/LYBox.Tests/LYBox.Tests.csproj
```

修改 SDK / Web 插件 IPC 后应运行对应解决方案与 Web SDK 测试。

---

## 7. 关键模式

| 模式 | 要点 |
|---|---|
| **ServiceLocator** | 静态 `IServiceProvider` 包装器；先 `TryGetService<T>()` 再 `GetService<T>()` |
| **ViewLocator** | 全局 `IDataTemplate` + `ConditionalWeakTable`（VM→View 循环无泄漏） |
| **导航** | key 驱动 `NavigationService` + `WeakReferenceMessenger` "JumpTo" |
| **菜单层级** | 扁平菜单项 + `ParentKey`；`MenuItemTreeBuilder.BuildTree()` 组树 |
| **源生成器** | `[GenerateMetadata]` 自动实现契约；元数据从 csproj 注入 |
| **本地化** | `ILocalizationService` 堆叠 `ResourceManager`；插件在 `Initialize()` 注册 |
| **插件图标** | `<PluginIconResources>` + `<AvaloniaResource>` 或 `IPlugin.GetIconResources()` |

---

## 8. UI 规范

详见 [AGENTS.md - UI 组件与样式规范](../AGENTS.md#ui-组件与样式规范强制)。

要点：
- 组件优先级：Irihi.Ursa > Avalonia 内置 > 项目 Fluent 补充 > CommunityToolkit.Mvvm
- 唯一视觉风格：Fluent Design（WinUI 3）
- 语义色键：`FluentColor*` + `FluentAccentBrush` 系列
- 图标：Fluent Icons（`Theme/Icons/Fluent/`）
- VM：`ObservableObject` + `[ObservableProperty]` / `[RelayCommand]`

---

## 9. 平台目标

| 平台 | TFM | 条件符号 |
|---|---|---|
| Windows | `net10.0-windows10.0.19041.0` | `Platforms_Windows` |
| macOS | `net10.0-macos15.0` | `Platforms_MacOs`（`SupportedOSPlatformVersion=10.15`） |
| Linux | `net10.0` | `Platforms_Linux` |

`src/Environment.props` 管理平台特定 TFM；CI 使用 `PublishBuilding=true` + `PublishPlatform=windows|linux|macos`；Release + Windows → `OutputType=WinExe`。

---

## 10. WebView IPC

Web 插件基于官方 `Avalonia.Controls.WebView 12.0.1`。前端 SDK（`lybox-plugin-sdk.js` + `lybox-plugin-theme.css`）作为嵌入资源打包在 `LYBox.Plugin.Shared.Web` 中，由宿主 `WebHostService` 在 `/sdk/` 路径提供。

### 路由

| 路径 | 用途 |
|---|---|
| `/{pluginId}/{**path}` | 静态资源 + 入口页 |
| `/sse/{pluginId}` | SSE 事件流 |
| `/__bridge/{pluginId}/{action}` | RPC / emit / channel-close（合并） |
| `/{pluginId}/.lybox/{artifact}` | 生成器产出的 JS/TS 绑定 |
| `/sdk/lybox-plugin-sdk.{js,css}` | 嵌入式 SDK |
| `/__lybox/debug` | Debug 构建：调试面板 |

### 信任边界

- 每个 WebView 短期 session token（`X-LYBox-Session`）
- 检查 Origin、pluginId、当前文档 Base URI、授权路径、目录边界
- 仅允许 loopback 或 `AllowedOrigins` 白名单
- 拒绝目录穿越、绝对路径、重解析点

### 系统命令（自动注册）

`SystemCommands` 在 `WebPluginView` 初始化时自动注册：`OpenFilePicker`、`SaveFilePicker`、`OpenFolderPicker`、`ShowMessageBox`、`ShowConfirmDialog`。

详细使用指南：[`docs/WebView-IPC-Guide.md`](WebView-IPC-Guide.md)。

---

## 11. 安装/升级/卸载

详见 [`Plugin-Upgrade-Evaluation.md`](Plugin-Upgrade-Evaluation.md)。

要点：
- 安装器限制条目数量、解压总大小、校验路径越界
- 覆盖升级：写入 `plugins/.pending/{PluginId}.new/`，重启后由 `ProcessPendingUpgrades()` 处理
- 插件数据目录（强制约定）：

| 平台 | Data 根 |
|---|---|
| Windows | `%LOCALAPPDATA%/LYBox` |
| Linux | `~/.config/LYBox` |
| macOS | `~/Library/Application Support/LYBox` |

通过 `IPluginDataDirectoryProvider` 解析。**禁止**回退到 `AppContext.BaseDirectory` 或 `Environment.SpecialFolder.*`。

---

## 12. 包与框架版本

`src/Directory.Packages.props`（**单一真相源**）；csproj 用 `Version="$(XxxVersion)"` 引用。

| 包 | 版本 |
|---|---|
| Avalonia | 12.1.2 |
| Irihi.Ursa | 2.2.0 |
| CommunityToolkit.Mvvm | 8.4.2 |
| EF Core | 10.0.12 |
| Microsoft.Extensions.DI / Localization | 10.0.12 |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 |
| ProDataGrid | 12.0.4 |
| ScottPlot | 5.1.59 |
| ZLogger | 2.5.10 |
| SkiaSharp | 3.119.4（锁定 3.x） |
| Avalonia.Controls.WebView | 12.0.1 |
| HarfBuzzSharp | 14.2.1.1 |
| IrihiAvaloniaShared | 0.5.0 |
| Tmds.DBus.Protocol / Generator | 0.94.2 |
| Microsoft.CodeAnalysis | 5.6.0 |
| System.CommandLine | 2.0.11 |
| Spectre.Console | 0.57.2 |
| Cake.Sdk | 6.2.0 |
| TUnit | 1.63.0 |

---

## 13. 相关文档

- [USAGE.md](USAGE.md) — 当前功能与使用说明
- [Plugin-API-Reference.md](Plugin-API-Reference.md) — 插件 API 参考
- [Plugin-Components-Guide.md](Plugin-Components-Guide.md) — 插件可用组件指南
- [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md) — SDK 版本契约
- [WebView-IPC-Guide.md](WebView-IPC-Guide.md) — WebView IPC 使用指南
- [WEBVIEW_IPC.md](WEBVIEW_IPC.md) — WebView IPC 当前实现
- [WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) — Web 插件指南
- [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md) — WebHost 优化设计方案
- [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) — 插件实现现状
- [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md) — SDK 优化分析
- [Plugin-Upgrade-Evaluation.md](Plugin-Upgrade-Evaluation.md) — 升级评估
- [FAQ.md](FAQ.md) — 常见问题
- [AGENTS.md](../AGENTS.md) — 智能体指令与强制规范
