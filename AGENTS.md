# LYBox — AGENTS.md

OpenCode 智能体在本仓库工作时的精简指南。

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

- **版本管理（单一真相源）**：宿主 + SDK + 前端共用同一版本号，唯一维护于仓库根 `version.props` 的 `<LyboxVersion>`。**已移除 GitVersion 依赖**（GitVersion.yml / dotnet-tools.json 中的 GitVersion.Tool / build.cs 中相关逻辑均已删除）。

  - 宿主（Launcher / UI / Platforms.\*）与 SDK（Generators / Shared / Shared.Web）版本号严格一致（`HostVersion == PluginSdkVersion`），由 `Directory.Build.props` 导入 `version.props` 解析。

  - **版本来源优先级**（高 → 低）：

    1. `--host-version` 命令行参数（CI 手动触发 / 紧急覆盖）
    2. 环境变量 `LYBOX_HOST_VERSION`（CI/脚本注入）
    3. `version.props` 中 `<LyboxVersion>`（唯一真相源，本地 / IDE / CI 默认兜底）

  - **发版流程**：修改 `version.props` 中 `<LyboxVersion>` → 提交 → 打标签 `V<version>` → push 触发 `release-host.yml`。

  - **插件版本独立**：插件版本由各插件 csproj 内 `<PluginVersion>` 各自声明与维护，不受 `version.props` 控制。

- **构建顺序很重要**：`--build=bin` 必须先于 `--build=plugin` 运行（或直接使用 `--build=all`），因为 `--build=bin` 会打包 Generators、CommandLine、Shared 与 Shared.Web SDK NuGet 包。

- **直接** **`dotnet build`** 可用于单个项目，但若未预先构建本地 NuGet 包，插件可能还原失败（使用 `--build=bin` 或确保 `artifacts/packages/sdk/` 下有 `.nupkg` 文件）。`--build=nuget` 保留为 `--build=bin` 的兼容别名。

- **运行启动器**：`dotnet run --project src/App/LYBox.Launcher.Desktop`

- **VS Code 调试**：使用 "Debug Plugin - {Name}" 启动配置 — 每个配置将 `AVALONIA_EXTRA_PLUGINS_PATH` 指向 `artifacts/bin/{ProjectName}/debug`，用于开发期实时加载。

- **CI 工作流**：`.github/workflows/ci.yml`（push/PR 验证构建）、`release-host.yml`（宿主+SDK+Tool 发布）、`release-plugins.yml`（插件发布）。

## Setting Up A New Cake.Sdk Project 构建

本仓库的构建并不是传统的 "cake.exe + build.cake" 模式，而是 **Cake.Sdk 文件化应用**：一个自引导的 C# 单文件 `build/build.cs`，通过 `#:sdk` 指令自动拉取运行器，无 cake.exe、无 `.cake` 脚本、无需 `.config/dotnet-tools.json` 工具清单。搭建一个新 Cake.Sdk 构建项目只需以下四步。

### 1. 前置条件

- .NET SDK（本仓库 `global.json` 仅配置测试运行器，未锁定 SDK 版本；按需补 `<sdk><version>`）。

- 无需安装命令行工具；`dotnet-tools.json`（根目录，`"tools": {}`）可以保持为空。

### 2. 新增构建脚本（最小骨架）

在新建目录（如 `build/`）放一个 `build.cs`，文件头声明 SDK 与依赖，正文用 Cake 的 `Task`/`RunTarget` 描述构建流程：

```csharp
#!/usr/bin/env dotnet
#:sdk Cake.Sdk@6.2.0       // 运行器版本，缺省从仓库依赖解析
#:package Spectre.Console@0.57.2   // 可选附加包
#:property PublishAot=false

using Cake.Common;
using Cake.Common.Tools.DotNet.Build;

var target   = Argument("target", "Default");
var buildNumber = Argument("build-number", 0);

Task("Build")
    .Does(ctx => {
        ctx.DotNetBuild("./src/MyApp/MyApp.csproj",
            new DotNetBuildSettings { Configuration = "Release" });
    });

RunTarget(target);
```

约定：

- 用 `Argument("名称", 默认值)` 接收命令行（`--名称=值`）；复杂项目可抽一个 `BuildContext` 类缓存已解析设置，并用 `[Flags] enum` 表示多目标（如本仓库 `BuildTarget`）。

- 任务可组合：`Task("Default")` 用 `.IsDependentOn("Build")` 串联别名。

- 结尾必须 `RunTarget(target);` 并把 `target` 设为 `Default`。

### 3. 封装启动脚本

在仓库根提供 `build.ps1`（Windows）与 `build.sh`（Linux/macOS），二者仅转调文件脚本并透传参数、透传退出码：

```powershell
# build.ps1
Push-Location $PSScriptRoot
try { dotnet build/build.cs -- $args; exit $LASTEXITCODE }
finally { Pop-Location }
```

```bash
# build.sh（Linux/macOS）
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
exec dotnet build/build.cs -- "$@"
```

### 4. 运行

```bash
.\build.ps1 --build=all            # Windows
./build.sh --build=all             # Linux/macOS
dotnet build/build.cs -- --build=all   # 免脚本直接调用
```

关键点：`--` 之后的参数由 Cake.Sdk 原样解析为 `Argument`，与 `build.cs` 内定义的键一一对应。首次运行会按 `#:sdk` 自动获取运行器（需联网还原），后续幂等。

## 架构

### 目录与产物分层

```text
src/App/        Launcher.Desktop + Launcher.Console
src/Layout/     Layout.Core + Layout.Ursa
src/Plugin/     Plugin.Generators + Plugin.CommandLine + Plugin.Shared + Plugin.Shared.Web
src/Platforms/  Platforms.Abstractions + 平台实现
artifacts/bin/、obj/、publish/、packages/、test-results/
```

普通 `dotnet build/test` 与 Cake 构建共享 `artifacts/` 根目录。SDK 包位于 `artifacts/packages/sdk/`，插件 zip 位于 `artifacts/packages/plugins/`。

### 两个解决方案

| 解决方案           | 内容                                                                 |
| -------------- | ------------------------------------------------------------------ |
| `Core.slnx`    | 宿主：Generators、CommandLine、Shared、Shared.Web、UI、Launcher、Platforms.Abstractions |
| `Plugins.slnx` | CommandLine、Shared.Web、所有 `plugins/*` 项目（12 个插件）                         |

### 项目分层（src/）

```
LYBox.Plugin.Generators/        Roslyn 增量源生成器（netstandard2.1，IsRoslynComponent）
LYBox.Plugin.CommandLine/       CLI 契约（netstandard2.1）：IPluginCommandRegistrar、注册上下文与 CLI 依赖裁剪规则
LYBox.Plugin.Shared/            共享契约（核心包）：IPlugin、IPluginMetadata、ViewLocator、ServiceLocator、特性、控件；通过 TypeForwardedTo 兼容旧 CLI 二进制
LYBox.Plugin.Shared.Web/        Web 包：Rpc/（IRpcHost、WebViewIpcHost、Channel、SseEventPusher）、Web/（WebHostService、SystemCommands、WebPluginView）、嵌入式 ipc.js
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
```

新增 CLI 功能时直接引用 `LYBox.Plugin.CommandLine`，但继续使用兼容 namespace `LYBox.Plugin.Shared.CommandLine`。`LYBox.Plugin.Shared` 对两个旧类型保留同版本包依赖与程序集类型转发，禁止在 Shared 中重新引入 `System.CommandLine` 或 `Spectre.Console` 的直接 PackageReference。

12 个插件：ButtonsInputs、DateTime、DialogFeedbacks、Downloader、LayoutDisplay、NavigationMenus、ProDataGrid、ScottPlot、TDLSharp、Template、BTSou、WebTemplate。

其中 `WebTemplate` 为 **WebView 插件**：宿主通过 WebView 承载前端页面。前端 **不再依赖 pnpm / npm / 任何构建工具**——所需的 SDK JS/CSS 直接来自 `LYBox.Plugin.Shared.Web` 嵌入资源（`PluginWebSdkResources`），由宿主 `WebHostService` 在 `/sdk/` 路径下提供。前端与宿主通信（WebView IPC）见 [docs/WebView-IPC-Guide.md](docs/WebView-IPC-Guide.md)。

### 应用启动流程

```
Program.cs → App.Initialize()
  1. 通过 ServiceCollectionExtensions.AddAvaloniaServices() 构建 DI 容器
  2. pluginLoader.DiscoverAllPluginAssembliesAsync() — 发现并加载插件程序集
  3. pluginLoader.InitializeAllPluginsAsync(services) — 插件向 ServiceCollection 注册服务
  4. ServiceProvider = services.BuildServiceProvider()
  5. ServiceLocator.Initialize(ServiceProvider) — 插件代码使用的静态网关
  6. InitializeDatabase() — 通过 EF Core 初始化 SQLite（AppDbContext）
  7. InitializeLocalization() — 恢复已保存的语言设置
  8. pluginLoader.RegisterAllPluginsAsync(ServiceProvider) — 插件执行多语言/设置注册
  9. RegisterPluginNavigationAndMenus() — 注册插件导航与菜单
 10. OnFrameworkInitializationCompleted() → 显示启动闪屏，然后显示 MainWindow
```

### 插件加载与程序集排除

- 每个插件在独立的、可收集的 `AssemblyLoadContext` 中加载

- 框架/共享程序集转发到默认上下文（排除清单见 `LYBox.Plugin.Shared.props`/`.targets`）

- 插件通过 `GeneratePluginManifest` 目标自动生成 `plugin.json` 清单（来自 `LYBox.Plugin.Shared.targets`）

- 发现：扫描 `{AppBaseDir}/plugins/` 和 `AVALONIA_EXTRA_PLUGINS_PATH` 环境变量

- 构建输出：`artifacts/publish/plugins/{Name}/publish/`（发布目录）+ `artifacts/packages/plugins/{Name}-{Version}.zip`（剥离 .pdb、.xml、.deps.json、.runtimeconfig.json）

## 插件系统前提约束（强制）

**当前项目不支持插件热加载与热卸载。** 所有插件在应用启动时一次性加载，状态变更（启用/禁用/标记卸载）需重启应用生效。

基于此前提，开发时遵循以下规则：

| 规则                                | 说明                                                                                                                                                                                                |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **无运行时插件增删**                      | 插件安装/卸载/启用/禁用均通过修改 `plugin.json` 状态实现，下次启动时生效。UI 中相关操作需提示用户重启。                                                                                                                                    |
| **无需处理 ALC 卸载清理**                 | `PluginLoadContext` 虽标记 `isCollectible=true`，但运行时不调用 `Unload()`。`ViewLocator._viewRegistry`、`LocalizationService._resourceManagers`、`MenuConfigurationService._menuItemsMap` 等静态/长生命周期字典无需在运行时清理。 |
| **应用退出需优雅关闭**                     | `App.OnShutdownRequested` 中应调用 `IPlugin.ShutdownAsync()` 并 `ServiceProvider.Dispose()`，确保插件持有的原生资源（如 TdLib 客户端）正确释放。                                                                              |
| **插件安装冲突处理**                      | 覆盖安装时若旧插件 ALC 仍持有 DLL 文件锁，需提示用户重启后再安装，或先关闭应用再安装。                                                                                                                                                  |
| **DisablePlugin/EnablePlugin 语义** | 仅修改状态字段并持久化到 manifest，不触发 ALC 卸载/重载。下次启动时按新状态决定是否加载。                                                                                                                                              |

## 关键模式（请勿破坏）

| 模式                 | 要点                                                                                                                                                                                                                                                            |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **ServiceLocator** | 插件使用的静态 `IServiceProvider` 包装器。在 `App.Initialize()` 中初始化一次。调用 `GetService<T>()` 前先用 `TryGetService<T>()` 检查。                                                                                                                                                  |
| **ViewLocator**    | 全局 `IDataTemplate`，使用 `ConditionalWeakTable` 缓存（VM→View 循环无泄漏）。在 XAML 中注册 — `ContentControl.Content="{Binding Content}"` 自动解析。                                                                                                                                |
| **导航**             | 基于 key 的 `NavigationService` + `WeakReferenceMessenger` 发布/订阅（"JumpTo" 消息）。插件在 `IPlugin.GetNavigationItems()` 中注册导航项。                                                                                                                                         |
| **菜单层级**           | 扁平菜单项 + 可选 `parentKey`。`MenuItemTreeBuilder.BuildTree()` 解析为树。`MenuConfigurationService` 管理增删。                                                                                                                                                                |
| **源生成器**           | 在插件入口类上标注 `[GenerateMetadata]` → 自动生成 `IPlugin` + `IPluginMetadata` 实现。`IPluginMetadata` 属性（Name/Version/Author/Description/PluginId/MinPluginSdkVersion）由生成器从 csproj 元数据属性注入（单一事实来源，见 O-1/O-8），入口类无需手写。同时扫描伴生类上的 `[ViewMap]`、`[NavigationItem]`、`[Menu]` 特性。 |
| **本地化**            | `ILocalizationService` 堆叠 `.resx` `ResourceManager` 实例。插件在 `Initialize()` 中注册自己的 ResourceManager。                                                                                                                                                             |
| **插件图标资源**        | `IPlugin.GetIconResources()` 返回的 `IResourceDictionary` 会在启动期合并进 `Application.Current.Resources.MergedDictionaries`（参照 [windit App.axaml.cs:140](file:///F:/Code/Dotnet/LYBox/windit-toolbox-main/src/App/Avalonia.Launcher.Desktop/App.axaml.cs#L140)）。源生成器支持两种声明方式：①插件 csproj 声明 `<PluginIconResources>File.axaml</PluginIconResources>` + `<AvaloniaResource Include="File.axaml" />`（生成器自动 emit `avares://` 加载逻辑）；②手动实现 `IPlugin.GetIconResources()` 返回 `new ResourceDictionary { ... }`。完整规范见 [`.agents/skills/lybox-plugin/SKILL.md` §🖼️ 插件图标资源](file:///F:/Code/Dotnet/LYBox/LYBox/.agents/skills/lybox-plugin/SKILL.md)。 |
| **插件生命周期**         | `NotInstalled → Installed → Loaded → Disabled → PendingUninstall`（另有 `PendingUpgrade`、`Error`，共 7 个状态）。状态变更触发事件通知 UI。                                                                                                                                         |

## UI 组件与样式规范（强制）

所有 Host UI 与插件 UI 必须遵守以下选型与风格规则。违反规则的 UI 代码视为需重构。

### 1. 组件选型优先级（从高到低）

| 优先级 | 来源                                                                                                | 用法示例                                                                                                                                                                                                                                         | 适用场景                                                                     |
| --- | ------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| 1   | **Irihi.Ursa**（`u:` 命名空间）                                                                         | `<u:Button />`、`<u:Banner />`、`<u:NavMenu />`、`<u:Form />`、`<u:NumericUpDown />`、`<u:TagInput />`、`<u:IPv4Box />`、`<u:TimeBox />`、`<u:Avatar />`、`<u:Card />`、`<u:Badge />`、`<u:Loading />`、`<u:Breadcrumb />`、`<u:Dialog />`、`<u:Drawer />` | 默认首选。所有通用控件优先用 Ursa。                                                     |
| 2   | **Avalonia 内置控件**（无 `u:` 前缀）                                                                      | `<Button />`、`<TextBox />`、`<CheckBox />`、`<ComboBox />`、`<ListBox />`、`<TreeView />`、`<TabControl />`、`<ProgressBar />`、`<Slider />`、`<DatePicker />`、`<DataGrid />`                                                                        | Ursa 未覆盖或场景不适合 Ursa 时使用。DataGrid 已应用 `<datagrid:DataGridFluentTheme />`。 |
| 3   | **项目自定义 Fluent 补充样式**（`src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml`） | `Button.FluentSettingsCard`、`Border.FluentInfoBadge`、`ProgressBar.circular.FluentProgressRing`、`Button.FluentBreadcrumbItem`、`Border.FluentContentDialogSurface`                                                                             | Ursa 未提供的 WinUI 风格控件。详见下表。                                               |
| 4   | **CommunityToolkit.Mvvm**                                                                         | `ObservableObject`、`[ObservableProperty]`、`[RelayCommand]`                                                                                                                                                                                   | ViewModel 基础设施（与组件选型并列，但所有 VM 必须用此库）。                                    |

**禁止**：引入 `Avalonia-Fluent-UI`（`AvaloniaFluentUI`）NuGet 包或项目引用。该库与 Irihi.Ursa 大量功能重叠且未发布到 NuGet。需要 WinUI 风格控件时，使用上述第 3 级的项目内补充样式。

### 2. 自定义 Fluent 补充样式速查表

所有补充样式位于 `src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml`，通过 `UrsaFluentTheme` 自动加载，无需手动 `<StyleInclude>`。

| 类名                                                                                                                     | 控件类型                                  | 替代的 WinUI 控件                       | 用途                                           |
| ---------------------------------------------------------------------------------------------------------------------- | ------------------------------------- | ---------------------------------- | -------------------------------------------- |
| `FluentSettingsCard`                                                                                                   | `Border` 或 `Button`                   | `SettingsExpander` / `SettingCard` | 设置页条目：左图标 + 标题 + 描述 + 右内容                    |
| `FluentSettingsCardTitle` / `FluentSettingsCardDescription` / `FluentSettingsCardIconHost`                             | `TextBlock` / `Border`                | —                                  | SettingsCard 内部子元素样式                         |
| `FluentInfoBadge` (+ `.FluentInfoBadgeCritical/Warning/Informational/Success`)                                         | `Border`                              | `InfoBadge`                        | 数值或状态徽章                                      |
| `FluentInfoBadgeText`                                                                                                  | `TextBlock`                           | —                                  | InfoBadge 内数字                                |
| `FluentInfoBadgeDot`                                                                                                   | `Ellipse`                             | `InfoBadge` (dot)                  | 点状徽章                                         |
| `FluentProgressRing` (+ `.Small` / `.Large`)                                                                           | `ProgressBar` (Classes=`circular`)    | `ProgressRing`                     | 圆环进度（确定性或 `IsIndeterminate="True"`）          |
| `FluentBreadcrumbItem`                                                                                                 | `Button`                              | `BreadcrumbBar` 项                  | 面包屑导航可点击项                                    |
| `FluentBreadcrumbCurrent` / `FluentBreadcrumbSeparator`                                                                | `TextBlock`                           | —                                  | 当前节点 / 分隔符                                   |
| `FluentContentDialogSurface` / `FluentContentDialogTitle` / `FluentContentDialogBody` / `FluentContentDialogButtonRow` | `Border` / `TextBlock` / `StackPanel` | `ContentDialog`                    | 模态对话框外观（控件仍走 Ursa `Dialog` API）              |
| `FluentNumeric`                                                                                                        | `u:NumericUpDown`                     | `NumberBox`                        | Ursa NumericUpDown 的 Fluent 边框微调             |
| `FluentTagInput`                                                                                                       | `u:TagInput`                          | —                                  | Ursa TagInput 的 Fluent 边框微调                  |
| `FluentComboBox`                                                                                                       | `ComboBox`                            | `ComboBox`                         | WinUI 3 下拉框（32px 高、4px 圆角、边框/浮层样式）           |
| `FluentListItem`                                                                                                       | `ListBoxItem`                         | `ListView` 项                       | WinUI 3 列表项（pointerover/pressed/selected 状态） |
| `FluentSegmentedItem`                                                                                                  | `ListBoxItem`                         | `Segmented`                        | 分段选择器项（配合水平 `ListBox` 使用）                    |
| `FluentToolTip`                                                                                                        | `ToolTip`                             | `ToolTip`                          | WinUI 3 工具提示外观                               |
| `FluentContextMenu` / `FluentMenuItem`                                                                                 | `ContextMenu` / `MenuItem`            | `MenuFlyout`                       | WinUI 3 右键菜单与菜单项                             |
| `FluentTabView` / `FluentTabItem`                                                                                      | `TabControl` / `TabItem`              | `TabView`                          | WinUI 3 标签页（下划线选中态）                          |

**示例**：

```xml
<!-- SettingsCard -->
<Border Classes="FluentSettingsCard">
    <Grid ColumnDefinitions="Auto,*,Auto">
        <Border Classes="FluentSettingsCardIconHost" Grid.Column="0">
            <Image Source="{DynamicResource FluentIconSettings}" Width="16" Height="16" />
        </Border>
        <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
            <TextBlock Classes="FluentSettingsCardTitle" Text="主题" />
            <TextBlock Classes="FluentSettingsCardDescription" Text="选择浅色或深色外观" />
        </StackPanel>
        <u:ToggleSwitch Grid.Column="2" IsChecked="{Binding EnableDarkMode}" />
    </Grid>
</Border>

<!-- InfoBadge -->
<Border Classes="FluentInfoBadge FluentInfoBadgeCritical" VerticalAlignment="Top">
    <TextBlock Classes="FluentInfoBadgeText" Text="3" />
</Border>

<!-- ProgressRing -->
<ProgressBar Classes="circular FluentProgressRing" IsIndeterminate="True" />

<!-- Breadcrumb -->
<ItemsControl ItemsSource="{Binding BreadcrumbSegments}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <Button Classes="FluentBreadcrumbItem" Content="{Binding Title}" Command="{Binding NavigateCommand}" />
                <TextBlock Classes="FluentBreadcrumbSeparator" Text="/" />
            </StackPanel>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### 3. 样式风格约束（限定 Fluent-UI 风格）

- **唯一允许的视觉风格**：Fluent Design System（WinUI 3 风格）。

- **语义色键使用规则**：宿主与插件 XAML **直接使用** **`Fluent*`** **语义色键**（`FluentColorText0/1/2`、`FluentColorPrimary`、`FluentColorDanger`、`FluentColorWarning`、`FluentColorSuccess`、`FluentColorInfo`，定义于 `Theme/FluentDesign/Light.axaml` / `Dark.axaml`，均含 `Pointerover` / `Active` / `Light` 状态变体）。`SemiColor*` 键仅为 Ursa 控件模板内部兼容保留（由 `UrsaFluentTheme` 的 ThemeDictionary 自动映射到 Fluent 配色），业务代码**不要再新增** **`SemiColor*`** **引用**。不允许在 XAML 中写死颜色字面量（如 `#FF0078D4`）。

- **颜色资源层级**：

  1. Fluent 语义 brush（首选）：`FluentAccentBrush`、`FluentAccentPointeroverBrush`、`FluentAccentPressedBrush`、`FluentCardBackgroundBrush`、`FluentCardStrokeBrush`、`FluentSubtleBrush`、`FluentSubtleHoverBrush`、`FluentSubtlePressedBrush`
  2. `FluentColor*` token（次选，随 Light/Dark 主题字典切换）：`FluentColorText0/1/2`、`FluentColorPrimary`、`FluentColorDanger`、`FluentColorWarning`、`FluentColorSuccess`、`FluentColorInformation`
  3. `SemiColor*` 键（仅 Ursa 控件模板兼容，业务代码禁用）
  4. 字面量颜色（仅用于阴影 `BoxShadow`、`Opacity` mask 等无法用语义资源表达的场景）：使用 `#XXRRGGBB` 格式，且必须注释说明原因

- **圆角规范**：卡片 8px、徽章/小按钮 4px、点状元素圆形（`CornerRadius="0"` + `CornerRadius` 全值 = 宽/2）。

- **间距规范**：内边距遵循 12/16/24 三档；元素间用 `Spacing` 而非 `Margin`。

- **动画规范**：颜色/画刷过渡统一用 `BrushTransition`，时长 `0:0:0.15`；阴影过渡用 `BoxShadowsTransition`。复杂动画引用 `Theme/Animations/` 下的 `DefaultSizeAnimations`、`NavMenuSizeAnimations`、`FluentPopupAnimations`。

- **主题入口**：所有样式通过 `src/Layout/LYBox.Layout.Ursa/Theme/UrsaFluentTheme.axaml` 注册，应用入口 `App.axaml` 仅引用 `<fluent:FluentTheme />` + `<theme:UrsaFluentTheme />` + `<sizeanimations:FluentPopupAnimations />`，**不要**在 `App.axaml` 中追加额外 `<StyleInclude>`。

### 4. 图标使用规则（优先 Fluent-UI icon）

- **首选图标集**：Fluent Icons（Microsoft Fluent UI System Icons）。资源位于 `src/Layout/LYBox.Layout.Ursa/Theme/Icons/`，按两套命名分文件存放：

  - `Theme/Icons/Fluent/{Regular|Filled}{16|20|24|28|32|48}.axaml` —— **多尺寸多风格资源**，key 形如 `Fluent{Name}{Size}{Variant}`（例：`FluentHome24Regular`、`FluentSettings20Regular`、`FluentHome20Filled`）。这是项目里 90% 的场景，**优先用这套**。
  - `Theme/Icons/FluentIcons.axaml` —— **16px 占位清单**，key 形如 `FluentIcon{Name}`（例：`FluentIconAdd`、`FluentIconAccessibility`），**不包含 Size/Variant 段**。仅当 `Fluent/*` 找不到对应图标时使用。

  > 命名规则：Fluent/* 套是 `{Name}{Size}{Variant}`（Fluent UI System Icons 官方惯例），FluentIcons.axaml 套是 `{FluentIcon}{Name}`。**两套并存**，新代码请按"先查 Fluent/{Variant}{Size}.axaml → 没有时回退到 FluentIcons.axaml"的顺序选。

- **图标资源键示例**：

  - `FluentHome24Regular`（24px Regular Home，nav menu、工具栏常用）
  - `FluentSettings20Regular`（20px Regular Settings，侧栏设置按钮）
  - `FluentHome20Filled`（20px Filled Home，NavMenu 选中态的替代选择）
  - `FluentIconAdd`（16px Add 占位，来自 FluentIcons.axaml）

- **图标引用方式**（按控件类型选择）：

  1. **`PathIcon`** **/** **`Image`**（首选，矢量）：

     ```xml
     <PathIcon Data="{DynamicResource FluentSettings20Regular}" Width="20" Height="20" />
     <!-- 或 -->
     <Image Source="{DynamicResource FluentSettings20Regular}" Width="20" Height="20" />
     ```
  2. **`Button.Content`**（按钮内图标）：

     ```xml
     <Button Classes="FluentSettingsCard">
         <PathIcon Data="{DynamicResource FluentSettings20Regular}" />
     </Button>
     ```
  3. **Ursa** **`IconButton`**（推荐用于纯图标按钮）：

     ```xml
     <u:IconButton Icon="{DynamicResource FluentSettings20Regular}" />
     ```

- **次选图标集**：项目自定义 `Semi` 风格图标（`src/Layout/LYBox.Layout.Ursa/Theme/Icons/_index.axaml` 中以 `SemiIcon` 开头的资源键，如 `SemiIconChevronDown`）。仅当 Fluent Icons 中找不到对应图标时使用，且需在代码注释中说明原因。

- **禁止**：硬编码 `Geometry.Parse("...")` 字面量路径。所有路径必须以 `StreamGeometry` 资源形式定义在 `Theme/Icons/` 下。

- **新增 Fluent 图标流程**：

  1. 从 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) 获取 SVG path
  2. 转换为 `<StreamGeometry x:Key="Fluent{Name}{Size}{Variant}">path data</StreamGeometry>`（如 `FluentSettings20Regular`）
  3. 追加到对应尺寸/风格的 `Theme/Icons/Fluent/{Variant}{Size}.axaml`（如 `Regular20.axaml`）
  4. 在 XAML 中以 `{DynamicResource Fluent...}` 引用

### 5. ViewModel 与数据绑定

- **ViewModel 基类**：所有 VM 继承 `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` 或项目 `ViewModelBase`。

- **属性**：用 `[ObservableProperty]` 自动生成 INPC。**禁止**手写 `private T _field; public T Foo { get => _field; set => SetProperty(ref _field, value); }`。

- **命令**：用 `[RelayCommand]` 自动生成 `ICommand`。**禁止**手写 `RelayCommand`/`DelegateCommand` 实例。

- **CompiledBindings**：`AvaloniaUseCompiledBindingsByDefault=true`（已全局开启）。所有 `Binding` 必须有正确的 `x:DataType`，避免运行时反射开销。

- **MVVM Toolkit 源生成器**：partial VM 类必须标注 `[INotifyPropertyChanged]` 或继承 `ObservableObject`，否则 `[ObservableProperty]` 不会生成。

### 6. 字体与窗口材质（WinUI 3 Mica）

- **字体**：WinUI 3 标准字体链 `Segoe UI Variable Text → Segoe UI → Microsoft YaHei UI（CJK）→ PingFang SC / Noto Sans CJK SC → sans-serif`，由 `Theme/UrsaFluentTheme.axaml.cs` 以 `FluentFontFamilyRegular` 资源注入（代码字体为 `Cascadia Code → Consolas → …` 的 `CodeFontFamily`），业务代码无需另行设置。

- **Mica 材质（Windows 11）**：`MainWindow.axaml` 设置 `TransparencyLevelHint="Mica"` + `Background="Transparent"`；`MainWindow.axaml.cs` 的 `ApplyBackdropBrushes()` 在 `ActualTransparencyLevel == Mica` 时将 shell 层资源键（`FluentShellBackgroundBrush`、`FluentNavPaneBackgroundBrush`、`FluentTitleBarBackgroundBrush`、`TitleBarBackground`、`WindowBackground`）覆写为半透明 `FluentShellMicaBrush`，否则移除覆盖回退不透明主题色。

- **材质资源定义**：`FluentShellMicaBrush` 位于 `Theme/FluentDesign/Light.axaml`（`#B3F3F3F3`）与 `Dark.axaml`（`#D9202020`），经 `Themes/{Light,Dark}/_index.axaml` 挂入 `UrsaFluentTheme` 的 ThemeDictionaries。

- **平台回退**：非 Win11（无 Mica）环境 DWM 自动降级透明等级，`ApplyBackdropBrushes()` 监听 `ActualTransparencyLevel` / `ActualThemeVariant` 变化并在 `OnOpened` 后重算，Linux/macOS/低版本 Windows 自动回退不透明配色，无需条件编译。

- **新增窗口若需 Mica**：遵循相同模式 — `TransparencyLevelHint="Mica"` + 透明背景 + 按透明等级切换 shell brush；不要在各视图内硬编码半透明色模拟材质。

## 包与框架版本

所有版本以 MSBuild 属性形式集中管理于 `src/Directory.Packages.props`（**单一真相源**；升级版本只动该文件，csproj 端用 `Version="$(XxxVersion)"` 引用）：

- Avalonia: `12.1.2` (`$(AvaloniaVersion)`) — `Avalonia` / `Avalonia.Themes.Fluent` / `Avalonia.Desktop` / `Avalonia.Fonts.Inter` 共用此版本

- Irihi.Ursa: `2.2.0` (`$(IrihiUrsaVersion)`) — 已与上游 2.x 最新稳定版对齐

- CommunityToolkit.Mvvm: `8.4.2` (`$(CommunityToolkit)`)

- EF Core: `10.0.12` (`$(EfCoreVersion)`) — `Sqlite` / `Design` 共用此版本

- Microsoft.Extensions.DI: `10.0.12` (`$(MicrosoftExtensionsDI)`)

- Microsoft.Extensions.Localization: `10.0.12` (`$(MicrosoftExtensionsLocalization)`)

- AvaloniaUI.DiagnosticsSupport: `2.2.3` (`$(AvaloniaDiagnosticsSupport)`)

- ProDataGrid: `12.0.4` (`$(ProDataGridVersion)`) — 暂未跟随 Avalonia 12.1 升 12.1.0.4，跨 minor 需单独回归

- ScottPlot: `5.1.59` (`$(ScottPlotVersion)`)

- ZLogger: `2.5.10` (`$(ZLoggerVersion)`)

- SkiaSharp: `3.119.4` (`$(SkiaSharpVersion)`) — **锁定 3.x**，Avalonia 12.x 与 ScottPlot 5.1.x 均依赖；4.x 是主版本，待上游生态兼容后再评估

- 嵌入式依赖：`HarfBuzzSharp 14.2.1.1` / `IrihiAvaloniaShared 0.5.0` / `Avalonia.Controls.WebView 12.0.1` / `Tmds.DBus.Protocol 0.94.2` + `Tmds.DBus.Generator 0.94.2` / `Microsoft.CodeAnalysis 5.6.0`（源生成器 SDK） / `System.CommandLine 2.0.11` / `Spectre.Console 0.57.2`

- 插件 NuGet 包：`LYBox.Plugin.Generators` + `LYBox.Plugin.CommandLine` + `LYBox.Plugin.Shared` + `LYBox.Plugin.Shared.Web`，版本与宿主一致（唯一真相源 `version.props` 的 `<LyboxVersion>`），本地构建到 `artifacts/packages/sdk/`

## NuGet 配置

- **根** **`nuget.config`**：将 `globalPackagesFolder` 设置为 `<repo>/packages`（本地缓存，在 `.gitignore` 中以 `packages/nuget/` 例外跟踪）

- **`plugins/nuget.config`**：继承根配置，新增 `LYBoxPluginLocal` 源指向 `<repo>/artifacts/packages/sdk` — 插件通过此源解析本地构建的 `LYBox.Plugin.Generators` 和 `LYBox.Plugin.Shared` 包

## 平台目标

`src/Environment.props` 管理平台特定的 TFM：

- Windows: `net10.0-windows10.0.19041.0` + 定义 `Platforms_Windows`

- macOS: `net10.0-macos15.0` + 定义 `Platforms_MacOs` + `SupportedOSPlatformVersion=10.15`

- Linux: `net10.0`（无平台后缀）+ 定义 `Platforms_Linux`

- 开发模式通过 `[System.OperatingSystem]::IsWindows()` 等自动检测 OS

- CI 使用 `PublishBuilding=true` + `PublishPlatform=windows|linux|macos`

- Release + Windows → `OutputType=WinExe`

## 已安装的 Skills（本地）

`.agents/skills/` 中的 skill 分为两组：

**Avalonia/Zafiro 通用 skill**（来自 `sickn33/antigravity-awesome-skills`）：

- `avalonia-layout-zafiro` — XAML 布局约定

- `avalonia-viewmodels-zafiro` — ViewModel/Wizard 模式

- `avalonia-zafiro-development` — 强制约定与规则

**LYBox 项目 skill**（本仓库维护，插件开发必读）：

- `lybox-plugin` — 非 Web 插件（Avalonia 原生）开发规范：csproj 声明、GenerateMetadata 特性、本地化/设置注册、生命周期约束

- `lybox-web-plugin` — Web 插件开发规范：PluginKind 声明与 Web 描述符、wwwroot、WebView IPC（RPC/事件/Channel/SSE）、嵌入式 SDK（`/sdk/lybox-plugin-sdk.js` + `/sdk/lybox-plugin-theme.css`，无需 pnpm）、lybox-mock 浏览器开发模式

这些 skill 处于激活状态，当其模式适用时应予使用。开发 `plugins/` 下的插件时，先按插件类型选用 `lybox-plugin` 或 `lybox-web-plugin`。

## 插件 .csproj 模板（用于新插件）

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
    <MinPluginSdkVersion>2.0.0</MinPluginSdkVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LYBox.Plugin.Generators" Version="1.0.0"
      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <PackageReference Include="LYBox.Plugin.Shared" Version="1.0.0" PrivateAssets="all" />
    <!-- 仅实现 CLI 命令的插件需要 -->
    <PackageReference Include="LYBox.Plugin.CommandLine" Version="1.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Web 插件差异**：在 `<PropertyGroup>` 中追加 `<PluginKind>Web</PluginKind>`，并额外引用 Web 包 `<PackageReference Include="LYBox.Plugin.Shared.Web" Version="1.0.0" PrivateAssets="all" />`。宿主依据 `PluginKind=Web` 自动注册 `wwwroot`（声明即注册），非 Web 插件**不要**引用 `LYBox.Plugin.Shared.Web`。

## WebView IPC 调研结论（特性分支 `feat-avalonia-webview-ipc`）

针对"Avalonia.Controls.WebView 能否实现 Wails 风格 IPC"的调研结论，作为后续实现阶段的强制约束。

### 结论

**可行——核心双向通道完备，需自建 RPC 层；host object 注入与虚拟主机映射在跨平台抽象层缺失；Linux 嵌入式 WebView 不可用。**

### 控件身份

- 使用 **官方包** `Avalonia.Controls.WebView`（NuGet `avaloniaui` 所有者，Prefix Reserved，MIT，仓库 [AvaloniaUI/Avalonia.Controls.WebView](https://github.com/AvaloniaUI/Avalonia.Controls.WebView)）。

- **禁止**引入已废弃的社区库 `Avalonia.WebView`（ChisterWu/Jianfenghuaite，仅匹配 Avalonia 11.x）。

- 当前稳定版 `12.0.1`，依赖 `Avalonia >= 12.0.0`，与本项目 12.1.0 兼容。

### IPC 原语（仅这两个低级通道，跨平台一致）

| 方向      | API                                                                                       |
| ------- | ----------------------------------------------------------------------------------------- |
| C# → JS | `await webView.InvokeScript(jsExpr:string):Task<string?>`（任意 JS 表达式，返回值 JSON 字符串）         |
| JS → C# | JS 调全局 `invokeCSharpAction(body:string)` → C# 订阅 `WebMessageReceived` 事件，`e.Body:string?` |

**关键约束**：

- JS→C# 是 **fire-and-forget**：`invokeCSharpAction` 不返回值给 JS。要实现 Promise 模型，必须复刻 Wails `calls.js` 的 callback-ID 表 + C# 侧 `InvokeScript("window.__rpc.resolve(id,json)")` 回推。

- 序列化全部走 **string**（双向 `JSON.stringify` 自理），无 binary 通道。

- 抽象层**不提供** `AddHostObjectToScript`、`SetVirtualHostNameToFolderMapping`、`WebMessageAsJson`。需这些能力只能经 `webView.TryGetPlatformHandle()` 拿平台 COM 指针自行实现（不可移植）。

### 平台支持矩阵

| 平台          | 后端                       | `NativeWebView`（嵌入）              | `NativeWebDialog`（独立窗口） |
| ----------- | ------------------------ | -------------------------------- | ----------------------- |
| Windows     | WebView2                 | ✔                                | ✔                       |
| macOS       | WKWebView                | ✔                                | ✔                       |
| Linux       | **WPE WebKit**（v12.0 新增） | ⚠️ 实验性（EGL 支持未完成，issue #14 open） | ✔                       |
| iOS/Android | 系统 WebView               | ✔                                | ✖                       |

- **Linux 后端选型：WPE WebKit**（`WpeWebView`）。v12.0 新增的 WPE 后端是 AvaloniaUI 团队为弥补 WebKitGTK 不能嵌入主窗口的缺口而引入的实验性方案，目标是让 Linux 上 `NativeWebView` 可嵌入式使用。

- **WPE 当前为实验性**：EGL 支持仍未完成（issue #14 open），生产环境使用需评估稳定性。在 WPE 后端成熟前，Linux 上若需稳定 WebView，仍可降级用 `NativeWebDialog` 独立窗口（WebKitGTK 后端）。

- **`WebKitGTK`** **后端不支持嵌入式** **`NativeWebView`**（官方 FAQ 明确，Wayland 下原生控件嵌入不可靠），故 Linux 嵌入式场景**不使用** WebKitGTK。

- macOS/Linux **无离屏渲染**（airspace 问题，issue #3 open）。

- Windows WebView2 Runtime 需随安装包分发（Win10 不预装）。

### 与 Wails v2 传输模型对照

| 通道            | Wails v2                                   | Avalonia 等价                                       |
| ------------- | ------------------------------------------ | ------------------------------------------------- |
| JS→后端消息       | `window.WailsInvoke(str)` → 平台 postMessage | `invokeCSharpAction(str)` → `WebMessageReceived`  |
| 后端→JS 执行      | `Frontend.ExecJS(js)`                      | `webView.InvokeScript(js)`                        |
| 回传 Promise 结果 | `window.wails.Callback(json)`              | 需自建：`InvokeScript("window.__rpc.resolve(...)")`   |
| 事件系统          | 内置 `EventsOn/Emit` + `EE`/`EX` 信封          | **未提供**，需自建 dispatcher                            |
| 绑定生成          | `wails generate` 产出 `wailsjs/go/**` + TS   | **无**，可基于 `LYBox.Plugin.Generators` Roslyn 源生成器自建 |

### 实现路径（复刻 Wails v2 四件套）

1. **引导 JS**：把 Wails `ipc.js` 中 `window.WailsInvoke` 替换为 `invokeCSharpAction`；保留 `'C'`/`'EE'`/`'EX'` 前缀信封。
2. **C# Dispatcher**：在 `WebMessageReceived` 按 Wails `dispatcher.go` 前缀分发（`C`=调用、`EE`=emit、`EX`=off）。
3. **回调用** **`InvokeScript`**：复刻 `window.wails.Callback` / `EventsNotify`。
4. **绑定生成**：复用项目现有 `LYBox.Plugin.Generators`（Roslyn 增量源生成器）基础设施，对标记的 RPC 服务类生成 JS 胶水 + TS 声明。
5. **握手**：复刻 `runtime:ready` 时序，C# 在 `NavigationCompleted` + 运行时就绪后再下发绑定。
6. **Origin 白名单**：复刻 Wails `originvalidator`，防 iframe 攻击。

### 已知风险

| 风险                               | 等级    | 说明                                                                                |
| -------------------------------- | ----- | --------------------------------------------------------------------------------- |
| Linux WPE 后端实验性                  | **高** | EGL 支持未完成（issue #14 open），生产前需 PoC 验证稳定性；不稳定则降级 `NativeWebDialog`（WebKitGTK 独立窗口） |
| Go→JS 高频推送堆积                     | 中     | 需像 Wails 一样做 batch 合并，避免 `InvokeScript` 队列饱和                                      |
| macOS airspace / 离屏渲染            | 中     | 影响透明叠加、弹层混合                                                                       |
| `WebResourceRequested` 不可 cancel | 中     | issue #53 open，拦截 URL 请求能力受限                                                      |

### 后续步骤

1. 先在 Windows 做 PoC：`InvokeScript` + `WebMessageReceived` 跑通"JS 调 C# 返回 Promise"最小闭环。
2. Linux 上 PoC WPE WebKit 嵌入式 `NativeWebView` 的稳定性（issue #14 EGL 完成度）；不可用则验证 `NativeWebDialog`（WebKitGTK）降级路径。
3. 基于 `LYBox.Plugin.Generators` 做绑定代码生成，与插件系统统一。

## 注意事项

- `.slnx` 格式（非 `.sln`）— .NET 10 XML 解决方案格式

- 构建脚本（`build/build.cs`）通过扫描 `plugins/` 下所有 `*.csproj` 发现插件 — `PluginId` 等从 .csproj XML 读取

- `Core.slnx` 与 `Plugins.slnx` 都包含 `src/Plugin/LYBox.Plugin.CommandLine`；Generator 继续固定为 `netstandard2.1`

- 插件 NuGet 包必须在还原插件前本地构建。先用 `.\build.ps1 --build=bin` 构建；包输出到 `artifacts/packages/sdk/`。`plugins/nuget.config` 将此目录添加为本地源。

- `AvaloniaUseCompiledBindingsByDefault` 在启动器项目中设为 `true` — 新插件应遵循此约定

- `src/` 下的 `Directory.Build.props` 导入 `Environment.props` 并设置默认 `TargetFramework=net10.0`（按平台覆盖）

- Generators 与 CommandLine 契约项目目标为 `netstandard2.1`；宿主、Shared、Shared.Web 与插件项目为 `net10.0`

- 仓库中无 `opencode.json` 或 `CLAUDE.md` — 本 `AGENTS.md` 是唯一的指令文件

