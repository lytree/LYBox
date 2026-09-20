# LYBox 当前功能与使用说明

本文档以当前源码与构建脚本为准。当前代码版本：`2.3.0-preview.3`。

---

## 1. 功能范围

- **桌面宿主**：Avalonia 12 + .NET 10 跨平台桌面应用（Fluent Design / Mica）
- **控制台启动器**：`System.CommandLine` 驱动的 CLI（`version` / `plugins` / `plugin run`）
- **插件体系**：
  - 独立 `AssemblyLoadContext` 隔离 + 共享清单转发
  - 三阶段生命周期：**Discover → Initialize → Register**
  - 安装 / 覆盖升级 / 卸载 / 启用 / 禁用 + SDK 兼容性校验
  - 7 个状态机：`NotInstalled`、`Installed`、`Loaded`、`Disabled`、`PendingUninstall`、`PendingUpgrade`、`Error`
- **宿主服务**：导航、菜单、本地化、设置（EF Core SQLite）、任务注册、窗口信息、ZLogger 结构化日志
- **Web 插件**：宿主内嵌 Kestrel，统一 `/sdk/` 静态资源、RPC、SSE、Channel、嵌入式浏览器 SDK
- **CLI 插件**：插件可注册子命令，由 CLI 解析并支持 `--output=json`

> **不支持**常规热加载、热卸载。安装 / 升级 / 卸载 / 启用 / 禁用均需重启生效。

---

## 2. 启动与管理

```powershell
# 桌面启动器
dotnet run --project src/App/LYBox.Launcher.Desktop

# 控制台 CLI
dotnet run --project src/App/LYBox.Launcher.Console -- version
dotnet run --project src/App/LYBox.Launcher.Console -- plugins list [--output=json]
dotnet run --project src/App/LYBox.Launcher.Console -- plugins info <plugin-id>
dotnet run --project src/App/LYBox.Launcher.Console -- plugins install <plugin-zip>
dotnet run --project src/App/LYBox.Launcher.Console -- plugins uninstall <plugin-id>
dotnet run --project src/App/LYBox.Launcher.Console -- plugin run <name> [args...]
```

`plugins list --output=json` 输出 JSON 清单便于脚本处理。安装包是插件构建产生的 zip；覆盖安装、卸载、启用/禁用均需重启宿主后完成状态切换。

---

## 3. 插件发现与目录

宿主扫描：

| 来源 | 说明 |
|---|---|
| `{AppBaseDir}/plugins/` | 主加载目录（已安装插件） |
| `AVALONIA_EXTRA_PLUGINS_PATH` 环境变量 | 追加的开发目录（开发期热加载调试） |
| `LYBOX_DATA_ROOT` 环境变量 | 覆盖插件数据根目录 |

每个插件目录至少包含：

```text
plugins/{PluginId}/
├── {PluginId}.dll
├── plugin.json               ← 必需（构建期生成）
└── shared-assemblies.txt     ← 可选：声明需共享的程序集
```

Web 插件还包含 `wwwroot/` 与入口页（`PluginEntryPage`，默认 `index.html`）。

**安全校验**：

- 路径穿越（`..`、`\`、绝对路径）
- 符号链接逃逸
- 重解析点（reparse points）
- 超限条目 / 解压总大小

---

## 4. 构建

### LYBox 仓库（宿主 + SDK）

```powershell
.\build.ps1 --build=bin                # 启动器 + SDK NuGet 包（host 与 SDK 同版本）
.\build.ps1 --build=publish-nuget      # 打包并推送 SDK NuGet
.\build.ps1 --configuration=Debug
.\build.ps1 --host-version=2.3.0       # 覆盖宿主 + SDK 版本
```

### LYBox.Plugins 仓库

```powershell
.\build.ps1                                                # 构建并打包全部插件
.\build.ps1 --sdk-feed=local --sdk-feed-path=<dir>          # 从本地 SDK feed 构建
.\build.ps1 --plugin=LYBox.Plugin.Template,LYBox.Plugin.WebTemplate
.\build.ps1 --plugin-version=1.2.3
```

### 产物

```text
artifacts/
├── publish/                            # 启动器与插件发布
│   ├── launcher/{desktop|console}/{rid?}/
│   └── plugins/{PluginName}/publish/
├── packages/
│   ├── sdk/                            # SDK NuGet 包
│   └── plugins/{PluginName}-{Version}.zip
├── bin/{ProjectName}/debug/
├── obj/{ProjectName}/
└── test-results/
```

---

## 5. 当前内置插件

[`LYBox.Plugins`](../../LYBox.Plugins) 仓库共 12 个插件 + 2 个脚手架模板：

| 插件 | 类型 | 说明 |
|---|---|---|
| `BTSou` | 原生 | BT 资源搜索 + 迅雷下载（**仅 Windows**） |
| `ButtonsInputs` | 原生 | 19 个按钮 / 输入控件演示 |
| `DateTime` | 原生 | 14 个日期 / 时间选择器演示 |
| `DialogFeedbacks` | 原生 | 9 个对话框 / 反馈组件演示 |
| `Downloader` | 原生 | HLS/DASH/MSS 下载器 + 抖音子模块（**Windows + Linux**） |
| `LayoutDisplay` | 原生 | 19 个布局 / 展示元素演示 |
| `NavigationMenus` | 原生 | 5 个导航 / 菜单 / 标签演示 |
| `ProDataGrid` | 原生 | ProDataGrid 高级数据表格 9 类演示 |
| `ScottPlot` | 原生 | ScottPlot 图表 5 类演示 |
| `TDLSharp` | 原生 | Telegram TDLib 集成（批量转发 / 导出 / 下载） |
| `Template` | 原生 | 插件模板（含 CLI 注册示例） |
| `WebTemplate` | Web | vanilla HTML/JS WebView 模板 |
| `ViteSample` | Web | Vite + TypeScript 端到端示例 |

详见各插件目录下的 README（已补充完整）。

---

## 6. 版本真相源

| 文件 | 角色 |
|---|---|
| `LYBox/version.props` 的 `<LyboxVersion>` | 宿主 + SDK 共同版本号 |
| 各插件 csproj 的 `<PluginVersion>` | 各自业务版本（独立维护） |

优先级：`--host-version` 命令行 > `LYBOX_HOST_VERSION` 环境变量 > `version.props`。

发版流程：修改 `<LyboxVersion>` → 提交 → 打 `V<version>` 标签 → push 触发 `release-host.yml`。

---

## 7. 相关文档

| 文档 | 主题 |
|---|---|
| [INDEX.md](INDEX.md) | 文档总索引 |
| [DEVELOPMENT.md](DEVELOPMENT.md) | 开发与架构 |
| [Plugin-API-Reference.md](Plugin-API-Reference.md) | 插件 API 参考 |
| [Plugin-Components-Guide.md](Plugin-Components-Guide.md) | 插件可用组件指南 |
| [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md) | 插件 SDK 版本契约 |
| [Plugin-Upgrade-Evaluation.md](Plugin-Upgrade-Evaluation.md) | 插件安装 / 升级 / 卸载设计 |
| [WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) | Web 插件指南 |
| [WebView-IPC-Guide.md](WebView-IPC-Guide.md) | WebView IPC 使用指南 |
| [WEBVIEW_IPC.md](WEBVIEW_IPC.md) | WebView IPC 当前实现速查 |
| [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) | 插件实现现状 |
| [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md) | SDK 简化原则与现状 |
| [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md) | WebHost 优化设计方案（设计中，未实施） |
| [FAQ.md](FAQ.md) | 常见问题 |
