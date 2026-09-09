---
name: lybox-plugin
description: "LYBox 非 Web 插件（Avalonia 原生插件）开发规范：csproj 声明、GenerateMetadata 源生成器、ViewMap/NavigationItem/Menu 特性、本地化与设置注册、生命周期约束。新建或修改 plugins/ 下不涉及 WebView/wwwroot 的插件时使用。"
risk: unknown
source: project
date_added: "2026-08-16"
---

# LYBox 非 Web 插件（Avalonia 原生）开发规范

> 适用范围：`plugins/` 下**不含** WebView、wwwroot、RPC 命令的插件。
> Web 插件（含 WebView / 前端页面）请使用 `lybox-web-plugin` skill。
> 本规范基于当前代码库事实；标 ⏳ 的条目为 `docs/WebHost-Optimization-Design.md` 中的**设计中**变更，实施前以现状为准。

---

## 🎯 何时使用本 Skill

- 新建一个纯 Avalonia UI 插件（演示页、控件展示、工具页）
- 为现有插件添加页面、导航项、菜单项
- 插件注册 DI 服务、设置项、本地化资源
- 排查插件加载/注册问题

---

## 📦 csproj 模板（单一事实来源）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
    <!-- 插件元数据：构建期由 GeneratePluginManifest 目标写入 plugin.json -->
    <PluginId>固定UUID-勿与代码硬编码不一致</PluginId>
    <PluginName>My Plugin</PluginName>
    <PluginAuthor>...</PluginAuthor>
    <PluginDescription>...</PluginDescription>
    <PluginVersion>1.0.0</PluginVersion>
    <MinPluginSdkVersion>2.0.0</MinPluginSdkVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LYBox.Plugin.Generators" Version="$(PluginSdkVersion)"
      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <PackageReference Include="LYBox.Plugin.Shared" Version="$(PluginSdkVersion)" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

✅ **PluginId 一致性（已解决）**：元数据由源生成器从 csproj 注入，入口类不再手写 `IPluginMetadata` 属性，消除 csproj 与代码双处硬编码不一致问题（O-1+O-8）。

⚠️ **禁止**：不要在非 Web 插件中创建 `wwwroot/` 目录——Web 资源由 `<PluginKind>Web</PluginKind>` 声明驱动，非 Web 插件不应携带 Web 资源。

---

## 🧩 入口类模式

```csharp
[GenerateMetadata]   // 源生成器从 csproj 注入元数据，生成 IPlugin + IPluginMetadata 实现
public partial class MyPlugin : IPluginMetadata
{
    // 元数据属性（Name/Version/Author/Description/PluginId/MinPluginSdkVersion）全部由生成器从 csproj 注入，无需手写

    // 可选：注册 DI 服务（默认实现已返回 CompletedTask，空实现无需 override）
    public Task InitializeAsync(IServiceCollection services) => Task.CompletedTask;

    // 典型：本地化注册（宿主批量注册时自动扫描 resx ResourceManager，此方法通常无需手写）
    public Task RegisterAsync(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService<ILocalizationService>() is { } loc)
            loc.RegisterResourceManager(Resources.Strings.ResourceManager);
        return Task.CompletedTask;
    }
}
```

要点：
- 不直接实现 `IPlugin`——`[GenerateMetadata]` 源生成器自动补全 `GetViewDefinitions/GetNavigationItems/GetMenuItems`；
- `InitializeAsync`/`RegisterAsync`/`ShutdownAsync` 均有接口默认实现，**空逻辑不要 override**（曾存在 10/12 插件的多余空 override，已清理，O-7）；
- 本地化批量注册由宿主 `PluginLoader` 自动扫描完成（O-6/O-14），插件一般无需手动注册 resx；
- 持有原生/后台资源（如 TdLib 客户端、HTTP 长连接）时**必须** override `ShutdownAsync` 释放——宿主退出时会调用 `PluginLoader.ShutdownAllPluginsAsync`。

---

## 🏷️ 特性驱动注册（源生成器消费）

| 特性 | 标注目标 | 作用 | 生成结果 |
|------|---------|------|---------|
| `[ViewMap(typeof(MyView))]` | ViewModel 类 | VM→View 映射 | `GetViewDefinitions()` 条目，ViewLocator 解析 |
| `[NavigationItem("my-feature")]` | ViewModel 类 | 注册导航 key | `GetNavigationItems()` 条目，`NavigationService.Navigate("my-feature")` 可达 |
| `[Menu(Header, Key, ParentKey)]` | ViewModel 类 | 注册菜单项 | `GetMenuItems()` 条目，菜单树构建 |

```csharp
[NavigationItem("my-feature")]
[Menu("我的页面", "my-feature", parentKey: null, Order = 10)]
[ViewMap(typeof(MyPageView))]
public partial class MyPageViewModel : ViewModelBase { }
```

菜单图标：`[Menu]` 的 `IconName` 命名属性可指定 Fluent 图标资源 key。父菜单图标已从硬编码 `ParentIconMap` 改为继承子菜单图标（O-9），无需改 Shared 库。

---

## 🌐 本地化

1. `Resources/Strings.resx`（默认，建议 zh-CN）+ `Strings.en.resx` 等语言变体；
2. 宿主 `PluginLoader` 批量注册时自动扫描各插件 resx `ResourceManager` 并统一重建缓存（O-6/O-14），插件一般无需在 `RegisterAsync` 手动注册；
3. XAML/代码经 `ILocalizationService` 取串。

---

## 🖼️ 插件图标资源（自带的 StreamGeometry）

`IPlugin` 提供了一个可选钩子 `IResourceDictionary? GetIconResources()`：返回的字典会在启动期由宿主 `App.RegisterPluginNavigationAndMenus` 合并进 `Application.Current.Resources.MergedDictionaries`（参照 [windit `App.axaml.cs:140`](file:///F:/Code/Dotnet/LYBox/windit-toolbox-main/src/App/Avalonia.Launcher.Desktop/App.axaml.cs#L140)）。之后 XAML 端通过 `{DynamicResource YourKey}` 引用即可（`IconNameToPathConverter` 也会递归查找，见下文）。

### 何时需要

- 插件菜单/导航需要**宿主内置图标库没有的**专有图标（品牌 logo、自研控件的图形、第三方图标的版权清理版等）。
- 插件希望用同一个 `StreamGeometry` 在多个 XAML 位置（菜单、设置页、工具栏、ViewBox）共享，避免每处 `Geometry.Parse(...)`。
- **不需要**自带图标资源的插件（绝大多数）保持 `GetIconResources()` 默认返回 `null` 即可。

### 写法 A：csproj 声明（**推荐**——保留 `[GenerateMetadata]`）

在插件 csproj 声明 `PluginIconResources` 指向一个 `<AvaloniaResource>` 嵌入的 XAML 文件：

```xml
<PropertyGroup>
  <!-- XAML 路径相对 csproj。生成器会用 avares://{AssemblyName}/PluginIcons.axaml 加载。 -->
  <PluginIconResources>PluginIcons.axaml</PluginIconResources>
</PropertyGroup>
<ItemGroup>
  <!-- 必须显式声明 AvaloniaResource，否则 XAML 不会嵌入 dll，avares:// 找不到。 -->
  <AvaloniaResource Include="PluginIcons.axaml" />
</ItemGroup>
```

`PluginIcons.axaml` 内容形如：

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 任何宿主内嵌 XAML 资源字典支持的元素都可放：StreamGeometry、Brush、SolidColorBrush 等 -->
    <StreamGeometry x:Key="MyPlugin.Gear">M9 2.39a1.5 1.5 0 0 1 2 0 ...</StreamGeometry>
    <StreamGeometry x:Key="MyPlugin.GearFilled">M11 2.39a1.5 1.5 0 0 0-2 0 ...</StreamGeometry>
    <SolidColorBrush x:Key="MyPlugin.BrandBrush" Color="#0078D4" />
</ResourceDictionary>
```

生成器会自动 emit `GetIconResources()` + `LoadIconResources()`，按 `avares://{AssemblyName}/PluginIcons.axaml` 加载。失败（资源缺失/解析抛错）一律返回 `null`，不影响宿主启动。

### 写法 B：手动实现 `IPlugin`

`LYBox.Plugin.Generators.MetadataGenerator` 不接管 `GetIconResources` 以外的元数据时**仍可**走生成器（用 `[GenerateMetadata]`），但只要插件想自定义 `GetIconResources` 的加载逻辑（比如多文件、运行时拼接），就必须**放弃 `[GenerateMetadata]`、手动实现 `IPlugin`**。同时仍可在伴生 VM 类上标注 `[Menu]/[NavigationItem]/[ViewMap]`，源生成器只接管入口类本身。

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.ViewModels;

public sealed class MyPlugin : IPlugin
{
    // 元数据：手动实现时仍然要在 csproj 声明，并由代码（或 [PluginMetadata] 工具方法）提供。
    public string Name        => "My Plugin";
    public string Version     => "1.0.0";
    public string Author      => "...";
    public string Description => "...";
    public string PluginId    => "固定UUID";

    public Task InitializeAsync(IServiceCollection services) => Task.CompletedTask;
    public Task RegisterAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;

    public IEnumerable<KeyValuePair<Type, ViewFactory>> GetViewDefinitions() => [];
    public Dictionary<string, ViewModelFactory> GetNavigationItems() => [];
    public List<KeyValuePair<string?, MenuItemViewModel>> GetMenuItems() => [];

    public IResourceDictionary? GetIconResources()
    {
        var dict = new ResourceDictionary();

        // 1. 直接 StreamGeometry（菜单/导航图标最常见）。
        dict["MyPlugin.Gear"]     = StreamGeometry.Parse("M...");
        dict["MyPlugin.GearFilled"] = StreamGeometry.Parse("M...");

        // 2. 也可放 Brush / 颜色 / 自定义画刷。
        dict["MyPlugin.BrandBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));

        return dict;
    }
}
```

### 键名空间隔离（强烈建议）

宿主内置资源键形如 `FluentHome24Regular`，如果插件直接注册同名 key 会**覆盖宿主资源**。建议给插件资源统一加插件前缀（如 `MyPlugin.Gear`），避免冲突。

### XAML 端引用

```xml
<!-- NavMenu / 设置按钮 -->
<PathIcon Data="{DynamicResource MyPlugin.Gear}" Width="20" Height="20" />

<!-- 或经 MenuIconName 走 IconNameToPathConverter（递归 Application.Resources + Styles.Resources） -->
<!-- [Menu(IconName = "MyPlugin.Gear")] -->
```

### 查找路径（`IconNameToPathConverter`）

converter 当前在以下位置查找 `iconName` 对应的 `StreamGeometry`（参照 [windit 实现](file:///F:/Code/Dotnet/LYBox/windit-toolbox-main/src/Plugin/Avalonia.Plugin.Shared/Converters/IconNameToPathConverter.cs)）：

1. `Application.Current.Resources` 及其所有 `MergedDictionaries`（**插件图标资源就挂在这里**）；
2. `Application.Current.Styles` 中每个 `Styles.Resources` 与 `IResourceProvider`（如 `UrsaFluentTheme`、`UrsaSemiTheme` 的图标）；
3. 键名容错：`FluentIcon*` / `Fluent*` 开头原样；其他自动补 `FluentIcon` 前缀。

> 因此 `[Menu(IconName = "FluentHome24Regular")]` 走 converter 也能找到宿主内置的 Fluent 图标，不必走 `GetIconResources()`。

---

## ⚙️ 设置注册（参考 Downloader 插件）

`RegisterAsync` 中经 `ISettingsService` 注册 `SettingDefinition`（路径、代理等），参考 `DownloaderPlugin.cs:32-53`。设置值由宿主设置页渲染与持久化，插件不要自建设置 UI。

---

## 🔌 服务注册规范

- DI 注册放 `InitializeAsync(IServiceCollection)`；
- **禁止**注册静态单例（反例：`BTSouPlugin.cs:25` 注册 `BTSouSearchService.Current`）——一律交给容器管理生命周期；
- 解析服务优先构造函数注入；插件代码内静态解析用 `ServiceLocator.TryGetService<T>()`（先 Try 后用，`GetService<T>` 会抛异常）。

---

## 🔄 生命周期约束（强制前提）

| 规则 | 说明 |
|------|------|
| 无热加载/热卸载 | 插件启用/禁用/卸载通过 `plugin.json` 状态字段，**重启生效**；UI 操作需提示用户重启 |
| 状态机 | `NotInstalled → Installed → Loaded → Disabled → PendingUninstall / PendingUpgrade / Error` |
| 启动加载顺序 | Discover（创建 ALC+反射）→ Initialize（DI 注册）→ Register（宿主服务就绪后）→ 导航/菜单注册 |
| 退出 | `App.OnShutdownRequested` 调用各插件 `ShutdownAsync()`；持有原生资源必须实现 |

---

## 🎨 UI 规范（强制）

组件选型与样式**必须**遵守根目录 `AGENTS.md` 的「UI 组件与样式规范」章节：
- 控件优先级：Irihi.Ursa（`u:`）→ Avalonia 内置 → 项目 Fluent 补充样式（`FluentDesignStyles.axaml`）；
- 唯一视觉风格：Fluent Design；禁止 Semi 硬编码色值与 `Avalonia-Fluent-UI` 包；
- 图标只用 `Theme/Icons/` 下的 `Fluent{Name}{Size}{Variant}`（如 `FluentHome24Regular`）或 `FluentIcon{Name}`（如 `FluentIconAdd`）StreamGeometry 资源，禁止 `Geometry.Parse` 字面量；
- VM 一律 `ObservableObject` + `[ObservableProperty]` + `[RelayCommand]`，绑定走 CompiledBindings（需正确 `x:DataType`）。

---

## ✅ 完成检查清单

- [ ] csproj 元数据齐全（`PluginId/PluginName/PluginVersion` 等），无需手写入口类元数据属性
- [ ] 入口类 `[GenerateMetadata]` + `partial` + 实现 `IPluginMetadata`
- [ ] 无 `wwwroot/` 目录、无 `<PluginKind>Web</PluginKind>`（那是 Web 插件的事）
- [ ] 页面 VM：`[ViewMap]` + `[NavigationItem]` + `[Menu]` 三件套齐全
- [ ] resx 本地化已注册；设置经 `ISettingsService`
- [ ] 无空 override；有后台资源时实现 `ShutdownAsync`
- [ ] 插件自带图标（仅当走 `GetIconResources()` 手动实现 `IPlugin`）：资源键加插件前缀避免与宿主冲突
- [ ] `dotnet build` 通过且输出目录生成 `plugin.json`
- [ ] 完整验证：`.\build.ps1 --build=plugin`（需先 `--build=bin` 打 SDK 包）

---

## ❌ 反模式

- 在非 Web 插件里引用 `Avalonia.Controls.WebView` 或 `LYBox.Plugin.Shared.Web` 包
- 手写 `IPlugin.GetViewDefinitions()` 等生成器已接管的方法
- 手写 `IPluginMetadata` 属性（应由源生成器从 csproj 注入）
- 注册静态单例到 DI；在插件里直接操作其他插件的服务/资源
- 硬编码颜色/Geometry；手写 INPC 属性（应 `[ObservableProperty]`）
