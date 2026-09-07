# 插件实现现状

## 宿主侧

插件加载、安装、升级和状态管理位于 `src/Layout/LYBox.Layout.Core/Services`；导航、菜单、本地化和主题位于 `LYBox.Layout.Ursa`。加载采用 Discover → Initialize → Register 三阶段，每个插件独立 AssemblyLoadContext，公共程序集通过共享清单转发。

## SDK 侧

`src/Plugin` 包含 Shared、Shared.Web、CommandLine 和 Generators。生成器负责 `[GenerateMetadata]`、UI 特性和 `[RpcCommand]` 的编译期代码生成。Web 宿主提供静态资源、session 鉴权、RPC、SSE、Channel 和嵌入式 JS/CSS SDK。

## 插件侧

`LYBox.Plugins/plugins` 当前包含 BTSou、ButtonsInputs、DateTime、DialogFeedbacks、Downloader、LayoutDisplay、NavigationMenus、ProDataGrid、ScottPlot、TDLSharp、Template、WebTemplate。插件覆盖控件/布局演示、数据表格、图表、下载、Telegram、BT 搜索、CLI 和 Web RPC 示例。

## 当前约束

- 不支持常规热加载和热卸载，变更需重启。
- 插件应在 `ShutdownAsync` 释放原生资源、任务和事件订阅。
- Web 插件使用 `LYBox.Plugin.Shared.Web` 与 `WebPluginView`，不得使用旧的 Web 宿主类型。
- 新增公共 API 时必须同步更新 SDK 版本契约、生成器和插件构建引用。
- 文档、清单和构建产物应以当前 csproj、`plugin.json` 和源码为准。
