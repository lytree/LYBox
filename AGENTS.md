# LYBox — AGENTS.md

智能体在本仓库工作时的精简指令。规则明确、信息密度优先。

---

## 构建与运行

**Cake.Sdk 文件化应用**（`build/build.cs`，Cake.Sdk **6.2.0**）。入口脚本：`build.ps1` / `build.sh`。

```powershell
# 本仓库（宿主 + SDK）
.\build.ps1 --build=all                    # 默认：bin（启动器 + SDK NuGet 包）
.\build.ps1 --build=bin                    # 构建启动器 + 打包 SDK NuGet 包
.\build.ps1 --build=nuget                  # --build=bin 的兼容别名
.\build.ps1 --build=publish-nuget          # 打包并推送 SDK NuGet
.\build.ps1 --configuration=Debug
.\build.ps1 --host-version=2.3.0           # 最高优先级
.\build.ps1 --package-version=1.2.3        # 兼容旧用法
.\build.ps1 --runtime-identifier=win-x64
.\build.ps1 --self-contained=true
.\build.ps1 --nuget-source=<URL>
.\build.ps1 --nuget-api-key=<KEY>
```

本仓库**无** `--build=plugin` / `--plugin=` / `--plugin-version=`。插件构建在 `..\LYBox.Plugins`：

```powershell
# LYBox.Plugins 仓库
.\build.ps1                                          # 构建并打包全部插件
.\build.ps1 --plugin=LYBox.Plugin.Template           # 仅构建指定插件
.\build.ps1 --plugin-version=1.2.3
.\build.ps1 --sdk-feed=local --sdk-feed-path=<dir> --sdk-version=<ver>
```

### 版本管理

- **单一真相源**：`version.props` 的 `<LyboxVersion>`（已移除 GitVersion）
- 宿主 + SDK + 前端**共用同一版本号**
- 优先级：`--host-version` > `LYBOX_HOST_VERSION` 环境变量 > `version.props`
- 发版流程：改 `<LyboxVersion>` → 提交 → 打标签 `V<version>` → push 触发 `release-host.yml`
- 插件业务版本由各插件 csproj 内 `<PluginVersion>` 各自维护

### 构建顺序

SDK 包由本仓库 `--build=bin` 产出到 `artifacts/packages/sdk/`；插件在 `LYBox.Plugins` 构建，需将本地 feed 指向该目录。**同版本号重打 SDK 包不会自动更新**——本地验证需清 `LYBox.Plugins/packages/lybox.plugin.*/<version>` 缓存，发版必须递增版本号。

### 运行

```powershell
dotnet run --project src/App/LYBox.Launcher.Desktop
```

### 启动器调试参数（Web 插件开发）

| 开关 / 变量 | 范围 | 说明 |
|---|---|---|
| `--web-dev` | Release | 启用 `WebPluginView` 开发工具栏（DEBUG 默认开启） |
| `--web-devtools[=port]` | 全部 | WebView 远程调试（默认 9222，Windows WebView2） |
| `--web-vite[=dir]` | 全部 | C# 端托管 Vite dev server，自动探测空闲端口写入 `LYBOX_WEB_PORT` |
| `LYBOX_WEB_PORT` | 环境变量 | 固定 `WebHostService` 监听端口（默认 `127.0.0.1:0` 随机） |
| `LYBOX_DEV_ORIGINS` | 环境变量 | 开发来源白名单（Vite 等） |
| 热刷新（livereload） | dev wwwroot 回退时自动 | `FileSystemWatcher` → SSE `dispatch` 推 `__lybox:reload` → ipc.js 自动 `location.reload()` |
| `/{BaseUrl}/__lybox/debug` | 仅 Debug | Web 调试面板（RPC 命令 + SSE 事件流 + session 签发） |
| console 桥 | WebView 自动 | 前端 `console.*` 经 `'L'` 信封转发宿主日志（单参数截断 2000 字符、最多 8 参数） |

### Vite 开发工作流

`templates/web-plugin-ui/` 是脚手架模板。**一条命令启动**：

```powershell
dotnet run --project src/App/LYBox.Launcher.Desktop -- --web-vite
```

宿主自动探测空闲端口写入 `LYBOX_WEB_PORT`，拉起 Vite 并托管生命周期，Vite 代理（`/__bridge` `/sse` `/__lybox`）与宿主同源联动。开发者打开 `http://localhost:5173`，RPC/调试会话自动签发。`npm run build` 产物 `dist/` 拷入插件 `wwwroot/` 后同一套代码自动切换 WebView 原生 bridge。宿主 HTTP 端点**无** CORS 响应头，前端必须走代理。

### VS Code 调试

插件调试启动配置 "Debug Plugin - {Name}" 在 `LYBox.Plugins/.vscode/launch.json` —— 每个配置启动本仓库 Launcher 并设置 `AVALONIA_EXTRA_PLUGINS_PATH` 指向 `LYBox.Plugins/artifacts/publish/plugins/{Name}/publish`。

### CI

- 本仓库：`.github/workflows/ci.yml`、`release-host.yml`
- 插件仓库：`LYBox.Plugins/.github/workflows/ci.yml`、`release-plugins.yml`

---

## Setting Up A New Cake.Sdk Project

### 前置

- .NET SDK（本仓库 `global.json` 仅配置测试运行器，未锁定 SDK 版本）
- 无需安装命令行工具；`dotnet-tools.json` 保持 `"tools": {}`

### `build/build.cs`

```csharp
#!/usr/bin/env dotnet
#:sdk Cake.Sdk@6.2.0
#:package Spectre.Console@0.57.2
#:property PublishAot=false

using Cake.Common;
using Cake.Common.Tools.DotNet.Build;

var target = Argument("target", "Default");

Task("Build")
    .Does(ctx => ctx.DotNetBuild("./src/MyApp/MyApp.csproj",
        new DotNetBuildSettings { Configuration = "Release" }));

RunTarget(target);
```

约定：`Argument("name", default)` 接 `--name=value`；任务可 `.IsDependentOn(...)`；结尾必须 `RunTarget(target)` 且 `target = "Default"`。

### `build.ps1` / `build.sh`

```powershell
# build.ps1
Push-Location $PSScriptRoot
try { dotnet build/build.cs -- $args; exit $LASTEXITCODE }
finally { Pop-Location }
```

```bash
#!/usr/bin/env bash
# build.sh
set -euo pipefail
cd "$(dirname "$0")"
exec dotnet build/build.cs -- "$@"
```

### 运行

```bash
.\build.ps1 --build=all
dotnet build/build.cs -- --build=all   # 免脚本直接调用
```

---

## 架构

### 目录与产物

```text
src/App/        Launcher.Desktop + Launcher.Console
src/Layout/     Layout.Core + Layout.Ursa
src/Plugin/     Plugin.Generators + Plugin.CommandLine + Plugin.Shared + Plugin.Shared.Web
src/Platforms/  Platforms.Abstractions + 平台实现
artifacts/bin/、obj/、publish/、packages/、test-results/
```

### 仓库布局（双仓库）

| 仓库 | 内容 |
|---|---|
| `LYBox`（本仓库） | 宿主 + SDK：`Core.slnx` |
| `..\LYBox.Plugins` | 12 个插件 + 2 个模板，`Plugins.slnx` |

### 项目分层

```
LYBox.Plugin.Generators/        Roslyn 增量源生成器（netstandard2.1，IsRoslynComponent）
LYBox.Plugin.CommandLine/       CLI 契约（netstandard2.1）
LYBox.Plugin.Shared/            共享契约（核心包）
LYBox.Plugin.Shared.Web/        Web 包（Rpc/Web/嵌入式 ipc.js）
LYBox.Platforms.Abstractions/   跨平台抽象基类
LYBox.Layout.Core/              宿主布局核心
LYBox.Layout.Ursa/              宿主应用（导航、菜单、本地化、EF Core、ZLogger）
LYBox.Launcher.Desktop/         桌面入口（AvaloniaUseCompiledBindingsByDefault=true）
```

### 平台项目

- Windows: `net10.0-windows10.0.19041.0` + `Platforms_Windows`
- macOS: `net10.0-macos15.0` + `Platforms_MacOs` + `SupportedOSPlatformVersion=10.15`
- Linux: `net10.0` + `Platforms_Linux`

### 插件项目（在 LYBox.Plugins）

每个插件是 `net10.0` 类库，引用 `LYBox.Plugin.Generators`（analyzer）和 `LYBox.Plugin.Shared`（`PrivateAssets="all"`）。元数据从 csproj 属性注入：

```xml
<PluginId>UUID</PluginId>
<PluginName>...</PluginName>
<PluginAuthor>...</PluginAuthor>
<PluginDescription>...</PluginDescription>
<PluginVersion>1.0.0</PluginVersion>
```

CLI 插件额外引用 `LYBox.Plugin.CommandLine`（namespace 仍为 `LYBox.Plugin.Shared.CommandLine`）。Web 插件额外引用 `LYBox.Plugin.Shared.Web` + `<PluginKind>Web</PluginKind>`。

**12 个插件**：BTSou · ButtonsInputs · DateTime · DialogFeedbacks · Downloader · LayoutDisplay · NavigationMenus · ProDataGrid · ScottPlot · TDLSharp · Template · WebTemplate。

### 应用启动流程

```
Program.cs → App.Initialize()
  1. AddAvaloniaServices()              构建 DI 容器
  2. DiscoverAllPluginAssembliesAsync() 发现并加载插件程序集
  3. InitializeAllPluginsAsync(services) 插件注册服务
  4. ServiceProvider = services.BuildServiceProvider()
  5. ServiceLocator.Initialize(provider)
  6. InitializeDatabase()               EF Core SQLite
  7. InitializeLocalization()           恢复已保存的语言
  8. RegisterAllPluginsAsync(provider)  插件多语言/设置注册
  9. RegisterPluginNavigationAndMenus() 消费 IGeneratedPluginModule.Ui
 10. InitializeWebHost()                仅 Web 插件时懒启动 Kestrel
 11. OnFrameworkInitializationCompleted() → 主窗口
```

退出流程：检查运行任务 → `ShutdownAsync()` → `ServiceProvider.Dispose()` → 取消全局异常订阅。

### 插件加载与程序集排除

- 每个插件独立可收集 `AssemblyLoadContext`
- 框架/共享程序集转发到默认上下文（排除清单见 `LYBox.Plugin.Shared.props/.targets`）
- `GeneratePluginManifest` 自动生成 `plugin.json`
- 发现：`{AppBaseDir}/plugins/` + `AVALONIA_EXTRA_PLUGINS_PATH`
- 输出：`artifacts/publish/plugins/{Name}/publish/` + `artifacts/packages/plugins/{Name}-{Version}.zip`

---

## 插件系统前提约束（强制）

**不支持热加载与热卸载。** 所有插件启动时一次性加载，状态变更（启用/禁用/卸载）需重启生效。

| 规则 | 说明 |
|---|---|
| 无运行时增删 | 通过修改 `plugin.json` 状态实现，下次启动生效 |
| 无需处理 ALC 卸载清理 | 静态/长生命周期字典无需运行时清理 |
| 应用退出需优雅关闭 | `ShutdownAsync()` + `ServiceProvider.Dispose()` |
| 插件安装冲突处理 | DLL 被锁时拒绝覆盖安装，提示用户重启 |
| Disable/Enable 语义 | 仅改状态字段，不触发 ALC 卸载/重载 |

---

## 关键模式（请勿破坏）

| 模式 | 要点 |
|---|---|
| **ServiceLocator** | 静态 `IServiceProvider` 包装器；先 `TryGetService<T>()` 再 `GetService<T>()` |
| **ViewLocator** | 全局 `IDataTemplate` + `ConditionalWeakTable`（VM→View 循环无泄漏） |
| **导航** | key 驱动 `NavigationService` + `WeakReferenceMessenger` "JumpTo"；插件通过 `[NavigationItem]` 声明 |
| **菜单层级** | 扁平菜单项 + `ParentKey`；`MenuItemTreeBuilder.BuildTree()` 组树 |
| **源生成器** | `[GenerateMetadata]` 自动实现 `IPlugin` + `IPluginMetadata`；元数据从 csproj 注入；扫描 `[ViewMap]`/`[NavigationItem]`/`[Menu]` 转为 `IGeneratedPluginModule.Ui`（`{Plugin}.Module.g.cs`，单一 UI 注册轨道） |
| **本地化** | `ILocalizationService` 堆叠 `.resx` `ResourceManager`；插件在 `Initialize()` 注册 |
| **插件图标资源** | `<PluginIconResources>` + `<AvaloniaResource>` 或 `IPlugin.GetIconResources()` 返回 `ResourceDictionary`；启动期合并进 `Application.Current.Resources.MergedDictionaries` |
| **插件生命周期** | `NotInstalled → Installed → Loaded → Disabled → PendingUninstall`（+ `PendingUpgrade`、`Error`，共 7 个状态） |

---

## UI 组件与样式规范（强制）

所有 Host UI 与插件 UI 必须遵守。

### 1. 组件选型优先级

| 优先级 | 来源 | 用法 | 适用场景 |
|---|---|---|---|
| 1 | **Irihi.Ursa**（`u:` 命名空间） | `<u:Button />`、`<u:NavMenu />`、`<u:NumericUpDown />`、`<u:TagInput />`、`<u:TimeBox />`、`<u:Avatar />`、`<u:Badge />`、`<u:Banner />`、`<u:Breadcrumb />`、`<u:Dialog />`、`<u:Drawer />` | 默认首选 |
| 2 | **Avalonia 内置** | `<Button />`、`<TextBox />`、`<ComboBox />`、`<ListBox />`、`<TreeView />`、`<TabControl />`、`<DataGrid />` | Ursa 未覆盖（DataGrid 已应用 Fluent 主题） |
| 3 | **项目自定义 Fluent 补充**（`Theme/FluentDesign/FluentDesignStyles.axaml`） | `FluentSettingsCard` / `FluentInfoBadge` / `FluentProgressRing` / `FluentBreadcrumbItem` / `FluentContentDialogSurface` 等 | Ursa 未提供的 WinUI 风格 |
| 4 | **CommunityToolkit.Mvvm** | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]` | VM 基础设施 |

**禁止**：`Avalonia-Fluent-UI`（`AvaloniaFluentUI`）NuGet 包/项目引用。

### 2. 自定义 Fluent 补充样式速查

位于 `src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml`，由 `UrsaFluentTheme` 自动加载。

| 类名 | 控件 | 替代 WinUI |
|---|---|---|
| `FluentSettingsCard` | Border/Button | SettingsExpander/SettingCard |
| `FluentSettingsCardTitle`/`Description`/`IconHost` | TextBlock/Border | — |
| `FluentInfoBadge` + `.Critical/Warning/Informational/Success` | Border | InfoBadge |
| `FluentInfoBadgeText`/`Dot` | TextBlock/Ellipse | InfoBadge 内容 |
| `FluentProgressRing` + `.Small`/`.Large` | ProgressBar (circular) | ProgressRing |
| `FluentBreadcrumbItem`/`Current`/`Separator` | Button/TextBlock | BreadcrumbBar |
| `FluentContentDialogSurface`/`Title`/`Body`/`ButtonRow` | Border/TextBlock/StackPanel | ContentDialog |
| `FluentNumeric`/`FluentTagInput` | Ursa NumericUpDown/TagInput | NumberBox |
| `FluentComboBox` | ComboBox | WinUI ComboBox |
| `FluentListItem`/`FluentSegmentedItem` | ListBoxItem | ListView/Segmented |
| `FluentToolTip`/`FluentContextMenu`/`FluentMenuItem` | ToolTip/ContextMenu/MenuItem | WinUI |
| `FluentTabView`/`FluentTabItem` | TabControl/TabItem | TabView |
| `FluentHyperlinkButton`/`FluentHypertext` | Button | HyperlinkButton |
| `FluentInfoBar` + SeverityInformational/Warning/Success/Error | Border | InfoBar |
| `FluentSegmentedControl`/`FluentSettingsExpander` | ListBox/Expander | Segmented/SettingsExpander |
| `WinUICard`/`FluentElevatedCard`/`FluentHeaderedCard`/`FluentCardDivider` | Border | 卡片 |
| `WinUIAccent`/`WinUISubtle`/`WinUINavItem`/`FluentAccent`/`FluentStandard`/`FluentSubtle` | Button | — |
| `WinUILargeTitle`/`WinUISectionHeader`/`WinUISubtitle`/`WinUIBody`/`WinUICaption` | TextBlock | 排版 |

### 3. 风格约束

- **唯一允许视觉风格**：Fluent Design（WinUI 3）
- **语义色键**：直接使用 `FluentColor*`（`FluentColorText0/1/2`、`FluentColorPrimary`、`FluentColorDanger/Warning/Success/Info`），含 `Pointerover`/`Active`/`Light` 变体
- **画刷**：`FluentAccentBrush`、`FluentAccentPointeroverBrush`、`FluentAccentPressedBrush`、`FluentCardBackgroundBrush`、`FluentCardStrokeBrush`、`FluentSubtleBrush/Hover/Pressed`、`FluentShellMicaBrush`
- **禁止** XAML 写死颜色字面量（如 `#FF0078D4`）；仅 `BoxShadow`/`Opacity` mask 等场景例外，必须注释
- `SemiColor*` 仅 Ursa 控件模板内部兼容，**业务代码禁用**
- **圆角**：卡片 8px、徽章/小按钮 4px、点状元素圆形
- **间距**：内边距 12/16/24；元素间用 `Spacing` 而非 `Margin`
- **动画**：颜色/画刷 `BrushTransition`（`0:0:0.15`）；阴影 `BoxShadowsTransition`
- **主题入口**：`App.axaml` 仅引用 `<fluent:FluentTheme />` + `<theme:UrsaFluentTheme />` + `<sizeanimations:FluentPopupAnimations />`

### 4. 图标

- **首选**：Fluent Icons（Microsoft Fluent UI System Icons），`Theme/Icons/`：
  - `Fluent/{Regular|Filled}{16|20|24|28|32|48}.axaml` —— key `Fluent{Name}{Size}{Variant}`（90% 场景）
  - `FluentIcons.axaml` —— 16px 占位 `FluentIcon{Name}`（找不到时回退）
- 引用：`PathIcon` / `Image` / `<Button>.<PathIcon>` / `<u:IconButton Icon="..." />`
- **禁止**硬编码 `Geometry.Parse("...")`
- 新增：从 fluentui-system-icons 取 SVG → 转 `<StreamGeometry x:Key="Fluent{Name}{Size}{Variant}">` → 追加到对应 `.axaml` → `{DynamicResource Fluent...}` 引用

### 5. ViewModel 与数据绑定

- 继承 `ObservableObject` 或 `ViewModelBase`
- `[ObservableProperty]` / `[RelayCommand]`；**禁止**手写 INPC/`RelayCommand` 实例
- `AvaloniaUseCompiledBindingsByDefault=true`；`Binding` 必须有 `x:DataType`
- partial VM 必须标 `[INotifyPropertyChanged]` 或继承 `ObservableObject`

### 6. 字体与 Mica

- 字体链：`Segoe UI Variable Text → Segoe UI → Microsoft YaHei UI → PingFang SC / Noto Sans CJK SC → sans-serif`（`FluentFontFamilyRegular`）；代码字体 `Cascadia Code → Consolas → …`（`CodeFontFamily`）
- Windows 11 Mica：`MainWindow.axaml` 设 `TransparencyLevelHint="Mica"` + `Background="Transparent"`；`ApplyBackdropBrushes()` 在 `ActualTransparencyLevel == Mica` 时把 shell 层覆写为半透明 `FluentShellMicaBrush`
- `FluentShellMicaBrush`：`#B3F3F3F3`（Light）/ `#D9202020`（Dark）
- 非 Win11 自动回退不透明配色，无需条件编译

---

## 包与框架版本

`src/Directory.Packages.props`（**单一真相源**）；csproj 用 `Version="$(XxxVersion)"` 引用。

| 包 | 版本 |
|---|---|
| Avalonia | `12.1.2` |
| Irihi.Ursa | `2.2.0` |
| CommunityToolkit.Mvvm | `8.4.2` |
| EF Core | `10.0.12` |
| Microsoft.Extensions.DI / Localization | `10.0.12` |
| AvaloniaUI.DiagnosticsSupport | `2.2.3` |
| ProDataGrid | `12.0.4` |
| ScottPlot | `5.1.59` |
| ZLogger | `2.5.10` |
| SkiaSharp | `3.119.4`（**锁定 3.x**，4.x 待上游生态兼容） |
| Avalonia.Controls.WebView | `12.0.1` |
| HarfBuzzSharp | `14.2.1.1` |
| IrihiAvaloniaShared | `0.5.0` |
| Tmds.DBus.Protocol/Generator | `0.94.2` |
| Microsoft.CodeAnalysis（源生成器） | `5.6.0` |
| System.CommandLine | `2.0.11` |
| Spectre.Console | `0.57.2` |
| Cake.Sdk | `6.2.0` |
| TUnit | `1.63.0` |

插件 SDK 包：`LYBox.Plugin.Generators` + `LYBox.Plugin.CommandLine` + `LYBox.Plugin.Shared` + `LYBox.Plugin.Shared.Web`，版本与宿主一致（`version.props` 的 `<LyboxVersion>`）。

---

## NuGet 配置

- 本仓库 `nuget.config`：`<clear/>` 后仅 `nuget.org`
- `LYBox.Plugins/nuget.config`：`globalPackagesFolder=packages`，两源（`LYBOX_SDK_FEED` 环境变量 + `nuget.org`）
- 验证本地 SDK：`--sdk-feed=local --sdk-feed-path=<本仓库>\artifacts\packages\sdk --sdk-version=<宿主版本>`；缓存 `LYBox.Plugins/packages/lybox.plugin.*/<version>`

---

## 平台目标

`src/Environment.props`：

- Windows: `net10.0-windows10.0.19041.0` + `Platforms_Windows`
- macOS: `net10.0-macos15.0` + `Platforms_MacOs` + `SupportedOSPlatformVersion=10.15`
- Linux: `net10.0` + `Platforms_Linux`
- CI: `PublishBuilding=true` + `PublishPlatform=windows|linux|macos`
- Release + Windows → `OutputType=WinExe`

---

## 已安装的 Skills

`.agents/skills/`：

- `avalonia-layout-zafiro` / `avalonia-viewmodels-zafiro` / `avalonia-zafiro-development` — Avalonia/Zafiro 通用
- `lybox-plugin` — 原生 Avalonia 插件开发规范
- `lybox-web-plugin` — Web 插件开发规范（含 WebView IPC + 嵌入式 SDK）

---

## 插件 .csproj 模板

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
    <PluginId>...</PluginId>
    <PluginName>...</PluginName>
    <PluginAuthor>...</PluginAuthor>
    <PluginDescription>...</PluginDescription>
    <PluginVersion>1.0.0</PluginVersion>
    <MinPluginSdkVersion>2.3.0-preview.3</MinPluginSdkVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LYBox.Plugin.Generators" Version="$(PluginSdkVersion)"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <PackageReference Include="LYBox.Plugin.Shared" Version="$(PluginSdkVersion)" PrivateAssets="all" />
    <!-- CLI 插件需要： -->
    <PackageReference Include="LYBox.Plugin.CommandLine" Version="$(PluginSdkVersion)" PrivateAssets="all" />
    <!-- Web 插件需要： + <PluginKind>Web</PluginKind> -->
    <!-- <PackageReference Include="LYBox.Plugin.Shared.Web" Version="$(PluginSdkVersion)" PrivateAssets="all" /> -->
  </ItemGroup>
</Project>
```

---

## WebView IPC 调研结论（`feat-avalonia-webview-ipc`）

### 结论

**可行** —— 核心双向通道完备，需自建 RPC 层；host object 注入与虚拟主机映射在跨平台抽象层缺失；Linux 嵌入式 WebView 不可用。

### 控件身份

- **官方包** `Avalonia.Controls.WebView`（NuGet `avaloniaui` 所有者，Prefix Reserved，MIT）
- **禁止**已废弃社区库 `Avalonia.WebView`（ChisterWu/Jianfenghuaite，仅 Avalonia 11.x）
- 当前稳定 `12.0.1`，依赖 `Avalonia >= 12.0.0`，与本项目 12.1.x 兼容

### IPC 原语

| 方向 | API |
|---|---|
| C# → JS | `await webView.InvokeScript(jsExpr:string):Task<string?>`（任意 JS 表达式，返回 JSON 字符串） |
| JS → C# | 全局 `invokeCSharpAction(body:string)` → C# 订阅 `WebMessageReceived`（**fire-and-forget**） |

**关键约束**：
- JS→C# 不返回 Promise，要复刻 Wails `calls.js` callback-ID 表 + C# 侧 `InvokeScript("window.__rpc.resolve(id,json)")` 回推
- 序列化全部走 `string`，无 binary
- 抽象层**不提供** `AddHostObjectToScript`、`SetVirtualHostNameToFolderMapping`、`WebMessageAsJson`

### 平台支持矩阵

| 平台 | 后端 | NativeWebView（嵌入） | NativeWebDialog（独立） |
|---|---|---|---|
| Windows | WebView2 | ✔ | ✔ |
| macOS | WKWebView | ✔ | ✔ |
| Linux | **WPE WebKit**（v12.0 新增） | ⚠ 实验性（issue #14） | ✔ |
| iOS/Android | 系统 WebView | ✔ | ✖ |

- Linux 后端选型：WPE WebKit；不稳定时降级 `NativeWebDialog`（WebKitGTK 独立窗口）
- WebKitGTK 后端**不支持嵌入式** `NativeWebView`（Wayland 下不可靠）
- macOS/Linux 无离屏渲染（airspace 问题，issue #3）
- Windows WebView2 Runtime 需随安装包分发

### 与 Wails v2 对照

| 通道 | Wails v2 | Avalonia 等价 |
|---|---|---|
| JS→后端消息 | `window.WailsInvoke(str)` → postMessage | `invokeCSharpAction(str)` → `WebMessageReceived` |
| 后端→JS | `Frontend.ExecJS(js)` | `webView.InvokeScript(js)` |
| Promise 回传 | `window.wails.Callback(json)` | 自建 `InvokeScript("window.__rpc.resolve(...)")` |
| 事件系统 | 内置 `EventsOn/Emit` + `EE`/`EX` 信封 | **未提供**，需自建 dispatcher |
| 绑定生成 | `wails generate` | 可基于 `LYBox.Plugin.Generators` 自建 |

### 实现路径

1. 引导 JS：替换 `WailsInvoke` 为 `invokeCSharpAction`；保留 `C`/`EE`/`EX` 信封
2. C# Dispatcher：`WebMessageReceived` 按 Wails `dispatcher.go` 前缀分发
3. 回调用 `InvokeScript`：复刻 `window.wails.Callback` / `EventsNotify`
4. 绑定生成：基于 `LYBox.Plugin.Generators` 生成 JS 胶水 + TS 声明
5. 握手：复刻 `runtime:ready` 时序
6. Origin 白名单：复刻 Wails `originvalidator`

### 已知风险

| 风险 | 等级 | 说明 |
|---|---|---|
| Linux WPE 实验性 | **高** | EGL 未完成（issue #14），生产前 PoC |
| 高频推送堆积 | 中 | 需 batch 合并避免 `InvokeScript` 队列饱和 |
| macOS airspace / 离屏 | 中 | 影响透明叠加 |
| `WebResourceRequested` 不可 cancel | 中 | issue #53 |

### 后续步骤

1. Windows PoC：`InvokeScript` + `WebMessageReceived` 跑通最小闭环
2. Linux PoC：WPE 稳定性；不可用则验证 `NativeWebDialog` 降级
3. 基于 `LYBox.Plugin.Generators` 做绑定代码生成

---

## 注意事项

- `.slnx` 格式（非 `.sln`）—— .NET 10 XML 解决方案格式
- `build/build.cs` 扫描 `plugins/` 下所有 `*.csproj` 发现插件；`PluginId` 从 .csproj XML 读取
- `Core.slnx` 与 `Plugins.slnx` 都包含 `src/Plugin/LYBox.Plugin.CommandLine`；Generator 固定 `netstandard2.1`
- 插件 NuGet 包必须本地构建后再还原插件（先 `--build=bin` → `artifacts/packages/sdk/`）
- `AvaloniaUseCompiledBindingsByDefault` 在启动器项目中设为 `true`，新插件应遵循
- `src/` 的 `Directory.Build.props` 导入 `Environment.props`，默认 `TargetFramework=net10.0`（按平台覆盖）
- Generators 与 CommandLine 目标 `netstandard2.1`；宿主、Shared、Shared.Web 与插件 `net10.0`
- 仓库中无 `opencode.json` 或 `CLAUDE.md` —— 本 `AGENTS.md` 是唯一的指令文件
