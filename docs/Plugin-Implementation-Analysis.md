# 插件实现现状

> 本文档描述当前已落地的实现，不记录历史方案或待实施设计。当前代码版本：`2.3.0-preview.3`。

---

## 1. 宿主侧

| 模块 | 路径 | 作用 |
|---|---|---|
| 插件加载 | `src/Layout/LYBox.Layout.Ursa/Services/PluginLoader.cs` | Discover → Initialize → Register 三阶段 |
| 插件安装 | `src/Layout/LYBox.Layout.Ursa/Services/PluginInstallationManager.cs` | 安装 / 卸载 / 状态变更 |
| ALC 隔离 | `src/Layout/LYBox.Layout.Ursa/Services/PluginLoadContext.cs` | 独立可收集 ALC，共享清单转发 |
| 插件管理 | `src/Layout/LYBox.Layout.Ursa/Services/PluginManagementService.cs` | UI 与 CLI 共享 |
| 导航 | `src/Layout/LYBox.Layout.Ursa/Services/NavigationService.cs` | key 驱动 + `WeakReferenceMessenger` |
| 菜单 | `src/Layout/LYBox.Layout.Ursa/Services/MenuConfigurationService.cs` | 扁平菜单 + `MenuItemTreeBuilder` |
| 本地化 | `src/Layout/LYBox.Layout.Ursa/Services/LocalizationService.cs` | 堆叠 `.resx` `ResourceManager` |
| 主题 | `src/Layout/LYBox.Layout.Ursa/Theme/UrsaFluentTheme.axaml` | Fluent Design / Mica 入口 |
| EF Core | `src/Layout/LYBox.Layout.Core/Data/AppDbContext.cs` | SQLite 持久化 |
| ZLogger | 同上 | 结构化日志 |

---

## 2. SDK 侧

`src/Plugin/` 包含四个包：

| 包 | 目标 | 作用 |
|---|---|---|
| `LYBox.Plugin.Generators` | netstandard2.1 | Roslyn 增量源生成器（`IsRoslynComponent`） |
| `LYBox.Plugin.CommandLine` | netstandard2.1 | CLI 契约（`IPluginCommandRegistrar`） |
| `LYBox.Plugin.Shared` | net10.0 | 核心契约、服务、模型、Avalonia 组件、控件 |
| `LYBox.Plugin.Shared.Web` | net10.0 | WebView、Kestrel、RPC、SSE、Channel、嵌入式 SDK |

### 2.1 源生成器负责

- `[GenerateMetadata]` —— 自动实现 `IPlugin` + `IPluginMetadata`；元数据从 csproj 属性注入
- `[ViewMap]` / `[NavigationItem]` / `[Menu]` —— 转换为 `IGeneratedPluginModule.Ui` 描述符（`{Plugin}.Module.g.cs`）
- `[RpcCommand]` —— 生成 RPC 绑定 `IRpcBindingSource` partial + 客户端 JS + `.d.ts`

### 2.2 Web 宿主职责

- 静态资源服务（`MapPluginRoot`）
- Session 鉴权（`X-LYBox-Session`）
- RPC / SSE / Channel 端点
- 嵌入式浏览器 SDK（`lybox-plugin-sdk.js` + `lybox-plugin-theme.css`）
- 系统命令自动注册（`SystemCommands`）

---

## 3. 插件侧

[`LYBox.Plugins`](../../LYBox.Plugins) 仓库当前 12 个插件 + 2 个脚手架模板：

| 插件 | 类型 | 覆盖范围 |
|---|---|---|
| `BTSou` | 原生 | BT 搜索 + 迅雷下载 |
| `ButtonsInputs` | 原生 | 19 个按钮 / 输入控件 |
| `DateTime` | 原生 | 14 个日期 / 时间选择器 |
| `DialogFeedbacks` | 原生 | 9 个对话框 / 反馈组件 |
| `Downloader` | 原生 | HLS/DASH/MSS 下载器 + 抖音子模块 |
| `LayoutDisplay` | 原生 | 19 个布局 / 展示元素 |
| `NavigationMenus` | 原生 | 5 个导航 / 菜单 / 标签 |
| `ProDataGrid` | 原生 | ProDataGrid 高级数据表格 9 类演示 |
| `ScottPlot` | 原生 | ScottPlot 图表 5 类演示 |
| `TDLSharp` | 原生 | Telegram TDLib 集成 |
| `Template` | 原生 | 插件模板（含 CLI 注册示例） |
| `WebTemplate` | Web | vanilla HTML/JS WebView 模板 |
| `templates/plugin-template-aot` | 模板 | 原生 Avalonia 脚手架 |
| `templates/web-plugin-vanilla` | 模板 | vanilla Web 脚手架 |
| `plugins/LYBox.Plugin.ViteSample` | Web | Vite + TypeScript 端到端示例 |

每个插件目录下的 README 详细说明功能、设置项、关键依赖、数据存储与调试入口。

---

## 4. 当前约束（强制）

### 4.1 不支持热加载 / 热卸载

所有插件在应用启动时一次性加载，状态变更（安装 / 卸载 / 升级 / 启用 / 禁用）需重启生效。详见 [FAQ.md §10-11](FAQ.md)。

### 4.2 原生资源释放

插件必须在 `ShutdownAsync` 释放：

- 原生客户端（TDLib、COM 互操作、HttpClient）
- 后台任务（`Task` / `Channel` / `Timer`）
- 事件订阅（`+=` 必须有对应 `-=` 或 `IDisposable`）

### 4.3 Web 插件契约

必须使用：

- `LYBox.Plugin.Shared.Web` NuGet 包
- `WebPluginView` 承载页面
- `IWebPlugin` 入口（`[GenerateMetadata]` 自动实现 `Web` 描述符）
- 嵌入式 SDK（`/sdk/lybox-plugin-sdk.js`）

**禁止**使用已删除的 `PluginWebAppService` / `PluginWebViewPage` / `PluginWebIpcService` / `@avalonia-template/plugin-sdk` / `frontend/` monorepo / `lybox-mock` dotnet tool。

### 4.4 共享公共 API

新增公共 API 时必须同步更新：

- SDK 版本契约（`PluginSdkContract.CurrentVersion` + `version.props` 的 `<LyboxVersion>`）
- 源生成器（如涉及）
- 插件 csproj 的 `<MinPluginSdkVersion>`

### 4.5 文档与清单基准

文档、清单、构建产物应以当前 csproj、`plugin.json` 和源码为准。历史方案与待实施设计单独写在 `WebHost-Optimization-Design.md`，不混入当前实现文档。

---

## 5. 关键文件

| 类别 | 路径 |
|---|---|
| 宿主插件加载 | [src/Layout/LYBox.Layout.Ursa/Services/PluginLoader.cs](../src/Layout/LYBox.Layout.Ursa/Services/PluginLoader.cs) |
| 插件安装 | [src/Layout/LYBox.Layout.Ursa/Services/PluginInstallationManager.cs](../src/Layout/LYBox.Layout.Ursa/Services/PluginInstallationManager.cs) |
| 导航 | [src/Layout/LYBox.Layout.Ursa/Services/NavigationService.cs](../src/Layout/LYBox.Layout.Ursa/Services/NavigationService.cs) |
| 菜单 | [src/Layout/LYBox.Layout.Ursa/Services/MenuConfigurationService.cs](../src/Layout/LYBox.Layout.Ursa/Services/MenuConfigurationService.cs) |
| 本地化 | [src/Layout/LYBox.Layout.Ursa/Services/LocalizationService.cs](../src/Layout/LYBox.Layout.Ursa/Services/LocalizationService.cs) |
| 设置 | [src/Layout/LYBox.Layout.Ursa/Services/SettingsService.cs](../src/Layout/LYBox.Layout.Ursa/Services/SettingsService.cs) |
| 任务 | [src/Layout/LYBox.Layout.Ursa/Services/TaskRegistry.cs](../src/Layout/LYBox.Layout.Ursa/Services/TaskRegistry.cs) |
| 视图解析 | [src/Plugin/LYBox.Plugin.Shared/ViewLocator.cs](../src/Plugin/LYBox.Plugin.Shared/ViewLocator.cs) |
| 源生成器 | [src/Plugin/LYBox.Plugin.Generators/](../src/Plugin/LYBox.Plugin.Generators/) |
| 共享构建 | [src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.props](../src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.props) |
| 共享构建 | [src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.targets](../src/Plugin/LYBox.Plugin.Shared/buildTransitive/LYBox.Plugin.Shared.targets) |

---

## 6. 相关文档

- [DEVELOPMENT.md](DEVELOPMENT.md) — 仓库边界、分层、启动流程
- [USAGE.md](USAGE.md) — 当前功能与使用说明
- [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md) — SDK 简化原则
- [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md) — SDK 版本契约
- [Plugin-Upgrade-Evaluation.md](Plugin-Upgrade-Evaluation.md) — 安装 / 升级 / 卸载设计
- [FAQ.md](FAQ.md) — 常见问题
