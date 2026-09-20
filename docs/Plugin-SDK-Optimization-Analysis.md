# Plugin SDK 当前结构

> 本文档描述当前已落地的 SDK 结构，不记录历史方案或待实施设计（待实施优化见 [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md)）。当前代码版本：`2.3.0-preview.3`。

---

## 1. 包边界

| 包 | 目标 | 职责 |
|---|---|---|
| `LYBox.Plugin.Shared` | net10.0 | 核心契约（`IPlugin` / `IPluginMetadata`）、服务接口、模型、ViewModel 基类、Avalonia 共享组件 |
| `LYBox.Plugin.Shared.Web` | net10.0 | WebView、Kestrel、RPC、SSE、Channel、嵌入式浏览器 SDK、嵌入式主题 CSS |
| `LYBox.Plugin.CommandLine` | netstandard2.1 | CLI 契约（`IPluginCommandRegistrar`） |
| `LYBox.Plugin.Generators` | netstandard2.1 | Roslyn 增量源生成器（`[GenerateMetadata]`、UI 特性、`[RpcCommand]`） |

四个包版本号与宿主一致，**统一发版**（受 `version.props` 的 `<LyboxVersion>` 唯一控制）。

---

## 2. 简化原则

1. **元数据单一事实来源**：插件元数据由 csproj 属性和生成器统一产生，**避免手写重复清单**（`IPluginMetadata.Name`/`Version`/`Author`/`Description`/`PluginId`/`MinPluginSdkVersion` 全部从 csproj 注入）
2. **Web 资源统一注册**：宿主按 `PluginKind`/`Webroot`/`EntryPage` 统一调用 `MapPluginRoot`，插件代码不再含注册调用
3. **前端 SDK 嵌入资源**：`lybox-plugin-sdk.js` + `lybox-plugin-theme.css` 作为 `LYBox.Plugin.Shared.Web` 的嵌入资源，**不要求插件仓库维护前端工作区**
4. **ALC 隔离 + 共享清单转发**：插件依赖通过独立 `AssemblyLoadContext` 隔离；公共框架程序集按 `LYBox.Plugin.Shared.props/.targets` 排除清单转发到默认 ALC
5. **SDK 兼容性校验统一**：安装期与加载期复用同一 `IsPluginSdkCompatible` 校验（解析 SemVer / Major 比对 / Minor-Build 不超界）

---

## 3. 包职责细化

### 3.1 `LYBox.Plugin.Shared`

```text
src/Plugin/LYBox.Plugin.Shared/
├── Attributes/          [GenerateMetadata] [ViewMap] [NavigationItem] [Menu] [RpcCommand]
├── Models/              PluginManifest, PluginInfo, SettingDefinition, MenuItemViewModel, …
├── Services/            ILocalizationService, ISettingsService, ITaskRegistry,
│                        IPluginLoader, IPluginInstallationManager, IPluginManagementService,
│                        IWindowInfoService, IPluginDataDirectoryProvider, …
├── ViewLocator.cs       IDataTemplate + ConditionalWeakTable
├── ServiceLocator.cs    静态 IServiceProvider 包装
├── ViewModelBase.cs     ObservableObject 基类
└── buildTransitive/     props/targets：共享清单 + 类型转发
```

`Shared` 通过 `TypeForwardedTo` 对两个旧 CLI 类型保留同版本包依赖，**禁止在 Shared 中重新引入 `System.CommandLine` 或 `Spectre.Console` 的直接 PackageReference**。

### 3.2 `LYBox.Plugin.Shared.Web`

```text
src/Plugin/LYBox.Plugin.Shared.Web/
├── Rpc/
│   ├── IRpcTransport.cs        传输抽象（MessageReceived + ExecuteScriptAsync）
│   ├── IRpcHost.cs             主机接口（RegisterCommand + EmitEvent + CreateChannel）
│   ├── WebViewIpcHost.cs       WebView IPC 运行时（实现 IRpcHost）
│   ├── WebViewIpcTransport.cs  包装 Avalonia.Controls.WebView
│   ├── Channel.cs              流式通道（Tauri 风格）
│   ├── SseEventPusher.cs       事件推送
│   ├── RpcEnvelope.cs          消息信封 + 错误码
│   ├── PluginRpcDispatcher.cs  命令分发
│   └── Assets/ipc.js           WebView 引导脚本（注入）
├── Web/
│   ├── WebHostService.cs       嵌入式 Kestrel（127.0.0.1:0）
│   ├── WebPluginView.axaml(.cs)  页面承载
│   ├── SystemCommands.cs       系统级 RPC（OpenFilePicker 等）
│   ├── WebViewIpcTransport.cs  WebView 适配器
│   ├── DebugPanelHtml.cs       Web 调试面板
│   └── PluginWebViewDevTools.cs
├── Assets/
│   ├── lybox-plugin-sdk.js     嵌入式浏览器 SDK
│   └── lybox-plugin-theme.css  嵌入式主题 CSS
├── FrameworkReference         Microsoft.AspNetCore.App
└── PackageReference            Avalonia.Controls.WebView 12.0.1
```

---

## 4. 现状核对

| 项 | 值 |
|---|---|
| 宿主版本 | `2.3.0-preview.3`（`version.props` 的 `<LyboxVersion>`） |
| 插件仓库 | 12 个内置插件 + 2 个脚手架模板（命名 `LYBox.Plugin.*`） |
| Web 示例 | `LYBox.Plugin.WebTemplate`（vanilla HTML/JS）+ `LYBox.Plugin.ViteSample`（Vite + TS） |
| 前端 SDK 来源 | `LYBox.Plugin.Shared.Web` 嵌入资源，由 `WebHostService` 在 `/sdk/` 提供 |
| 共享清单 | `LYBox.Plugin.Shared.props` / `.targets` 统一维护 |
| ALC 策略 | `isCollectible=true`，运行时不调用 `Unload()` |
| SDK 兼容性 | `IsPluginSdkCompatible` 在安装期与加载期共用 |
| Vite 开发 | 宿主 `--web-vite` 托管 Vite dev server，HMR + 同源代理 |

**已删除**（不要再使用）：

- ❌ `frontend/` pnpm monorepo（含 `@lybox/sdk` / `create-lybox-react` / `create-lybox-vue3`）
- ❌ `tools/LYBox.MockServer`（lybox-mock dotnet tool）
- ❌ `@avalonia-template/plugin-sdk` npm 包
- ❌ `LYBox.Plugin.DouyinDownloader`（已合并到 `LYBox.Plugin.Downloader`）
- ❌ `IWebPlugin.PluginBaseDir` 可写属性（已瘦身为只读 `Web` 描述符）

---

## 5. 关键依赖

| 包 | 版本 | 单一真相源 |
|---|---|---|
| `Avalonia` | `12.1.2` | `$(AvaloniaVersion)` |
| `Irihi.Ursa` | `2.2.0` | `$(IrihiUrsaVersion)` |
| `CommunityToolkit.Mvvm` | `8.4.2` | `$(CommunityToolkit)` |
| `EF Core` | `10.0.12` | `$(EfCoreVersion)` |
| `Microsoft.Extensions.DI` | `10.0.12` | `$(MicrosoftExtensionsDI)` |
| `Microsoft.Extensions.Localization` | `10.0.12` | `$(MicrosoftExtensionsLocalization)` |
| `Avalonia.Controls.WebView` | `12.0.1` | （Web 包直接引用） |
| `Microsoft.CodeAnalysis`（源生成器 SDK） | `5.6.0` | （Generators 直接引用） |
| `System.CommandLine` | `2.0.11` | （CommandLine 直接引用） |
| `Spectre.Console` | `0.57.2` | （CommandLine 直接引用） |
| `HarfBuzzSharp` | `14.2.1.1` | （宿主 / 字体引擎） |
| `IrihiAvaloniaShared` | `0.5.0` | （Ursa 共享契约） |
| `Tmds.DBus.Protocol` / `Generator` | `0.94.2` | （Linux DBus） |

所有版本在 `src/Directory.Packages.props`（**单一真相源**）；csproj 用 `Version="$(XxxVersion)"` 引用。

---

## 6. 关键文件

| 路径 | 作用 |
|---|---|
| [src/Plugin/LYBox.Plugin.Generators/RpcCommandGenerator.cs](../src/Plugin/LYBox.Plugin.Generators/RpcCommandGenerator.cs) | `[RpcCommand]` 源生成器 |
| [src/Plugin/LYBox.Plugin.Generators/PluginModuleGenerator.cs](../src/Plugin/LYBox.Plugin.Generators/PluginModuleGenerator.cs) | `[GenerateMetadata]` + UI 描述符 |
| [src/Plugin/LYBox.Plugin.Shared/IPlugin.cs](../src/Plugin/LYBox.Plugin.Shared/IPlugin.cs) | 核心契约 |
| [src/Plugin/LYBox.Plugin.Shared/PluginSdkContract.cs](../src/Plugin/LYBox.Plugin.Shared/PluginSdkContract.cs) | 编译期版本常量 |
| [src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.props](../src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.props) | 共享清单（props） |
| [src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.targets](../src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.targets) | 共享清单（targets） |
| [src/Plugin/LYBox.Plugin.Shared.Web/PluginWebSdkResources.cs](../src/Plugin/LYBox.Plugin.Shared.Web/PluginWebSdkResources.cs) | 嵌入式 SDK 资源契约 |
| [src/Directory.Packages.props](../src/Directory.Packages.props) | 包版本单一真相源 |
| [version.props](../version.props) | Host / SDK 版本唯一真相源 |

---

## 7. 相关文档

- [DEVELOPMENT.md](DEVELOPMENT.md) — 仓库边界、分层、启动流程
- [USAGE.md](USAGE.md) — 当前功能与使用说明
- [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) — 插件实现现状
- [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md) — SDK 版本契约
- [Plugin-API-Reference.md](Plugin-API-Reference.md) — 插件 API 参考
- [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md) — WebHost 优化设计方案（**设计中，未实施**）
