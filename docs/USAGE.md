# LYBox 当前功能与使用说明

> 本文档以当前源码和构建脚本为准。宿主仓库：`LYBox`；插件仓库：`LYBox.Plugins`。

## 功能范围

- Avalonia 12 + .NET 10 桌面宿主与控制台启动器。
- 插件发现、独立 AssemblyLoadContext 隔离、三阶段生命周期：Discover → Initialize → Register。
- 插件安装、覆盖升级、卸载、禁用与 SDK 兼容性校验；变更通常在重启后生效，不支持常规热加载。
- 导航、菜单、本地化、设置（EF Core SQLite）、任务注册、窗口信息和结构化日志。
- Web 插件：宿主内嵌 Kestrel，统一提供静态资源、RPC、SSE 和嵌入式浏览器 SDK。
- 控制台命令：`version`、`gui`、`plugins list|info|install|uninstall`、`plugin run` 以及插件别名命令。

## 启动与管理

```powershell
dotnet run --project src/App/LYBox.Launcher.Desktop
dotnet run --project src/App/LYBox.Launcher.Console -- version
dotnet run --project src/App/LYBox.Launcher.Console -- plugins list
dotnet run --project src/App/LYBox.Launcher.Console -- plugins info <plugin-id>
dotnet run --project src/App/LYBox.Launcher.Console -- plugins install .\plugin.zip
dotnet run --project src/App/LYBox.Launcher.Console -- plugins uninstall <plugin-id>
```

`plugins list` 支持 `--output=json`。安装包是插件构建产生的 zip；覆盖安装、卸载、禁用/启用均需重启宿主后完成状态切换。外部开发目录是只读的。

## 插件发现与目录

宿主扫描 `{AppDir}\plugins\`，并可通过 `AVALONIA_EXTRA_PLUGINS_PATH` 追加开发目录。每个插件目录至少包含 DLL 与 `plugin.json`；Web 插件还包含 `wwwroot` 及入口页。路径穿越、绝对路径、符号链接逃逸和超限压缩包会被拒绝。

## 当前插件仓库

`LYBox.Plugins` 当前包含：BTSou、ButtonsInputs、DateTime、DialogFeedbacks、Downloader、LayoutDisplay、NavigationMenus、ProDataGrid、ScottPlot、TDLSharp、Template、WebTemplate。它们分别覆盖控件演示、布局/导航、数据表格、图表、下载、Telegram、BT 搜索、模板和 Web/RPC 示例。

## 构建

```powershell
# LYBox 仓库
.\build.ps1 --build=bin
.\build.ps1 --build=plugin
.\build.ps1 --build=all

# LYBox.Plugins 仓库
.\build.ps1 --build=plugin --sdk-feed=local
.\build.ps1 --build=plugin --plugin=LYBox.Plugin.Template,LYBox.Plugin.WebTemplate
```

宿主 `version.props` 的 `LyboxVersion` 是 Host/SDK 版本真相源；当前代码版本为 `2.3.0-preview.3`。插件业务版本由各插件 csproj 的 `PluginVersion` 管理。产物位于 `artifacts/publish` 与 `artifacts/packages`。

## 相关文档

- [开发与架构](DEVELOPMENT.md)
- [插件 API](Plugin-API-Reference.md)
- [插件组件](Plugin-Components-Guide.md)
- [SDK 版本契约](Plugin-SDK-Versioning.md)
- [Web 插件指南](WEB_PLUGIN_GUIDE.md)
- [WebView IPC 设计](WEBVIEW_IPC.md)
- [升级实现](Plugin-Upgrade-Evaluation.md)
- [FAQ](FAQ.md)
