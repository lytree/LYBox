# Plugin SDK 当前结构

本文档描述当前已落地的 SDK 结构，不记录历史方案或待实施设计。

## 包边界

`LYBox.Plugin.Shared` 提供 Avalonia 插件的核心契约、服务接口、模型、ViewModel 基类和共享控件；`LYBox.Plugin.Shared.Web` 单独承载 WebView、嵌入式 Kestrel、RPC、SSE、Channel 与浏览器 SDK；`LYBox.Plugin.CommandLine` 提供 CLI 契约；`LYBox.Plugin.Generators` 提供元数据和 RPC 源生成器。

## 简化原则

- 插件元数据由 csproj 属性和生成器统一产生，避免手写重复清单。
- Web 静态资源由宿主按 `PluginKind/Webroot/EntryPage` 统一注册。
- 前端 SDK 作为嵌入资源提供，不要求插件仓库维护前端工作区。
- 插件依赖通过独立 ALC 隔离；公共框架程序集按共享清单转发。
- 安装期和加载期复用同一 SDK 兼容性校验。

## 现状核对

宿主当前版本为 `2.3.0-preview.3`。插件仓库包含 12 个插件，按 `LYBox.Plugin.*` 命名。Web 示例为 `LYBox.Plugin.WebTemplate`，使用 `/sdk/lybox-plugin-sdk.js` 和 `[RpcCommand]`。本文件不再把旧的 pnpm/Vite/Vue、旧 IPC 类型或不存在的项目路径作为当前功能描述。
