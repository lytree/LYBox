# LYBox 文档总索引

`docs/` 目录下所有文档按主题分组。当前代码版本：`2.3.0-preview.3`。

> **状态说明**：上一轮"重新整理文档"任务中已重写 5 份（USAGE / FAQ / WEBVIEW_IPC / Plugin-Implementation-Analysis / Plugin-SDK-Optimization-Analysis）并新增 INDEX。当前 14 份文档均保留并定位清晰——不再有"⚠️ 旧"的过时文档；Plugin-Components-Guide 与 Plugin-API-Reference、WebView-IPC-Guide 与 WEBVIEW_IPC 互补分工（非冗余）。

---

## 1. 入门

| 文档 | 主题 | 适用读者 |
|---|---|---|
| [USAGE.md](USAGE.md) | 当前功能范围、启动 / 管理、构建命令、内置插件一览 | 用户、新人 |
| [DEVELOPMENT.md](DEVELOPMENT.md) | 仓库边界、分层、启动流程、SDK、构建调试、版本表 | 开发者 |
| [FAQ.md](FAQ.md) | 24 个常见问题（插件加载 / UI / 设置 / Web / 调试等） | 所有开发者 |

---

## 2. 插件开发

| 文档 | 主题 |
|---|---|
| [Plugin-API-Reference.md](Plugin-API-Reference.md) | `IPlugin` / `IPluginMetadata` / `IGeneratedPluginModule` 接口签名、特性清单、服务接口、csproj 元数据、完整入口示例（93 行精炼） |
| [Plugin-Components-Guide.md](Plugin-Components-Guide.md) | Ursa 控件、Avalonia 内置控件、项目 Fluent 样式类、画刷、图标、ServiceLocator、ViewLocator、完整 XAML 页面示例（620 行完整组件手册） |
| [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md) | `HostVersion` / `PluginVersion` 两层版本号、`minPluginSdkVersion` 优先级、`IsPluginSdkCompatible` SemVer 比对规则 |
| [Plugin-Upgrade-Evaluation.md](Plugin-Upgrade-Evaluation.md) | 安装 / 升级 / 卸载设计、`Data/{PluginId}/` 数据目录约定、`schemaVersion` 数据库迁移 |

> Plugin-API-Reference 与 Plugin-Components-Guide 分工：
> - **API-Reference**：以"接口 / 特性 / 服务"为主，**回答"插件代码怎么写"**
> - **Components-Guide**：以"控件清单 / 样式类"为主，**回答"UI 怎么搭"**

---

## 3. 实现与设计

| 文档 | 主题 |
|---|---|
| [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) | 宿主 / SDK / 插件侧当前实现现状、强制约束（热加载禁用、原生资源释放、Web 插件契约、SDK 兼容性） |
| [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md) | SDK 4 个包的边界与职责、简化原则、目录结构、关键依赖版本表 |
| [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md) | WebHost 优化设计方案（**设计中，未实施** —— 含破坏性变更 BC-1~BC-7、五阶段规划 S1~S5） |

> 三者分工：
> - **Implementation-Analysis**：**当前实现**是什么（与代码同步）
> - **SDK-Optimization-Analysis**：SDK **包结构与简化原则**（与代码同步）
> - **WebHost-Optimization-Design**：**未来优化方案**（不实施，仅讨论）

---

## 4. WebView 插件

| 文档 | 主题 |
|---|---|
| [WEBVIEW_IPC.md](WEBVIEW_IPC.md) | WebView IPC 当前实现要点（组件 / 路由 / 消息信封 / 错误码 / 信任边界 / 生命周期 / 关键文件） |
| [WebView-IPC-Guide.md](WebView-IPC-Guide.md) | WebView IPC 详细使用指南（API / 前端 SDK / 消息协议 / Channel 流式 / SystemCommands / 完整示例 / 测试） |
| [WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) | Web 插件整体指南（零依赖 vanilla JS 模板、`PluginKind=Web` 声明、`IWebPlugin` 入口） |

> 三者分工：
> - **WEBVIEW_IPC**：**当前实现的速查表**（"组件 / 路由 / 信封"一目了然）
> - **WebView-IPC-Guide**：**使用指南**（"API 怎么调、SDK 怎么用、怎么测试"）
> - **WEB_PLUGIN_GUIDE**：**Web 插件整体入门**（"如何从 0 写一个 Web 插件"）

---

## 5. 文档结构（README 入口）

- 仓库根 [README.md](../README.md) — 入门、构建命令、架构、插件开发指南、打包与部署
- [AGENTS.md](../AGENTS.md) — 智能体 / 维护者精简指令（UI 规范、Cake.Sdk、平台目标、依赖版本、WebView 调研）
- 插件仓库 [LYBox.Plugins/README.md](../../LYBox.Plugins/README.md) — 插件构建与调试
- 各插件目录下的 README — 12 个内置插件 + 2 个模板各自说明

---

## 6. 推荐阅读顺序

1. **新用户**：[README.md](../README.md) → [USAGE.md](USAGE.md)
2. **新插件作者**：[Plugin-Components-Guide.md](Plugin-Components-Guide.md) → [Plugin-API-Reference.md](Plugin-API-Reference.md) → [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md)
3. **宿主维护者**：[DEVELOPMENT.md](DEVELOPMENT.md) → [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) → [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md)
4. **Web 插件作者**：[WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) → [WEBVIEW_IPC.md](WEBVIEW_IPC.md) → [WebView-IPC-Guide.md](WebView-IPC-Guide.md)
5. **遇到问题**：[FAQ.md](FAQ.md) → 主题相关文档

---

## 7. 当前文档清单

| 文档 | 行数 | 角色 |
|---|---|---|
| [INDEX.md](INDEX.md) | 84 | 总索引（本文档） |
| [USAGE.md](USAGE.md) | 163 | 功能与命令速查 |
| [DEVELOPMENT.md](DEVELOPMENT.md) | 351 | 仓库边界 / 分层 / 启动流程 / 构建调试 |
| [FAQ.md](FAQ.md) | 262 | 24 个常见问题（按场景分组） |
| [Plugin-API-Reference.md](Plugin-API-Reference.md) | 93 | 插件契约 / 特性 / 服务接口（精炼参考） |
| [Plugin-Components-Guide.md](Plugin-Components-Guide.md) | 620 | 控件与样式完整手册 |
| [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md) | 26 | SDK 版本契约（与 AGENTS.md 互补） |
| [Plugin-Upgrade-Evaluation.md](Plugin-Upgrade-Evaluation.md) | 101 | 安装 / 升级 / 卸载 + 数据目录约定 |
| [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) | 141 | 当前实现现状（与代码同步） |
| [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md) | 147 | SDK 包边界与简化原则 |
| [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md) | 251 | WebHost 优化方案（设计中，未实施） |
| [WEBVIEW_IPC.md](WEBVIEW_IPC.md) | 171 | WebView IPC 实现速查 |
| [WebView-IPC-Guide.md](WebView-IPC-Guide.md) | 1084 | WebView IPC 详细使用指南 |
| [WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) | 56 | Web 插件整体入门 |
