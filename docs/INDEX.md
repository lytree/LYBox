# LYBox 文档总索引

`docs/` 目录下所有文档按主题分组。当前代码版本：`2.3.0-preview.3`。

---

## 入门

| 文档 | 主题 | 适用读者 |
|---|---|---|
| [USAGE.md](USAGE.md) | 当前功能范围、启动 / 管理、构建命令、内置插件一览 | 用户、新人 |
| [DEVELOPMENT.md](DEVELOPMENT.md) | 仓库边界、分层、启动流程、SDK、构建调试、版本表 | 开发者 |
| [FAQ.md](FAQ.md) | 24 个常见问题（插件加载 / UI / 设置 / Web / 调试等） | 所有开发者 |

---

## 插件开发

| 文档 | 主题 |
|---|---|
| [Plugin-API-Reference.md](Plugin-API-Reference.md) | `IPlugin` / `IPluginMetadata` / `IGeneratedPluginModule` 接口、特性清单、服务接口签名、csproj 元数据 |
| [Plugin-Components-Guide.md](Plugin-Components-Guide.md) | Ursa 控件、Avalonia 内置控件、项目 Fluent 样式类、画刷、图标、ServiceLocator、ViewLocator、完整示例 |
| [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md) | `HostVersion` / `PluginVersion` 两层版本号、`minPluginSdkVersion` 优先级、`IsPluginSdkCompatible` SemVer 比对 |
| [Plugin-Upgrade-Evaluation.md](Plugin-Upgrade-Evaluation.md) | 安装 / 升级 / 卸载设计、`Data/{PluginId}/` 数据目录约定、`schemaVersion` 数据库迁移 |

---

## 实现与设计

| 文档 | 主题 |
|---|---|
| [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) | 宿主 / SDK / 插件侧当前实现现状、强制约束 |
| [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md) | SDK 包边界、简化原则、目录结构、关键依赖 |
| [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md) | WebHost 优化设计方案（**设计中，未实施** —— 含破坏性变更、阶段规划） |

---

## WebView 插件

| 文档 | 主题 |
|---|---|
| [WEBVIEW_IPC.md](WEBVIEW_IPC.md) | WebView IPC 当前实现要点（组件、路由、消息信封、错误码、信任边界、生命周期） |
| [WebView-IPC-Guide.md](WebView-IPC-Guide.md) | WebView IPC 详细使用指南（API、前端 SDK、消息协议、完整示例、测试、平台支持） |
| [WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) | Web 插件整体指南（零依赖 vanilla JS 模板、`PluginKind=Web` 声明） |

---

## 文档结构（README 入口）

- 仓库根 [README.md](../README.md) — 入门、构建命令、架构、插件开发指南、打包与部署
- [AGENTS.md](../AGENTS.md) — 智能体 / 维护者精简指令（UI 规范、Cake.Sdk、平台目标、依赖版本、WebView 调研）
- 插件仓库 [LYBox.Plugins/README.md](../../LYBox.Plugins/README.md) — 插件构建与调试
- 各插件目录下的 README — 12 个内置插件 + 2 个模板各自说明

---

## 推荐阅读顺序

1. 新用户：[README.md](../README.md) → [USAGE.md](USAGE.md)
2. 新插件作者：[Plugin-Components-Guide.md](Plugin-Components-Guide.md) → [Plugin-API-Reference.md](Plugin-API-Reference.md) → [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md)
3. 宿主维护者：[DEVELOPMENT.md](DEVELOPMENT.md) → [Plugin-Implementation-Analysis.md](Plugin-Implementation-Analysis.md) → [Plugin-SDK-Optimization-Analysis.md](Plugin-SDK-Optimization-Analysis.md)
4. Web 插件作者：[WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) → [WebView-IPC-Guide.md](WebView-IPC-Guide.md) → [WEBVIEW_IPC.md](WEBVIEW_IPC.md)
5. 出问题：[FAQ.md](FAQ.md) → 主题相关文档

---

## 当前文档状态

| 文档 | 状态 | 备注 |
|---|---|---|
| `INDEX.md` | ✅ 新增 | 本索引 |
| `USAGE.md` | ✅ 已重写 | 与 `README.md` 区分：README 入门，USAGE 功能与命令速查 |
| `DEVELOPMENT.md` | ✅ 已重写 | 上一轮重写 |
| `FAQ.md` | ✅ 已重写 | 按场景分组，24 个问题 |
| `Plugin-API-Reference.md` | ⚠️ 旧 | 仍保留，建议下一轮与 `Plugin-Components-Guide` 合并精简 |
| `Plugin-Components-Guide.md` | ⚠️ 旧 | 仍保留（620 行大文档），与 `AGENTS.md` UI 规范有重叠 |
| `Plugin-SDK-Versioning.md` | ✅ 状态良好 | 与 `AGENTS.md` 内容互补 |
| `Plugin-Upgrade-Evaluation.md` | ✅ 状态良好 | 数据目录约定 + schema 迁移 |
| `Plugin-Implementation-Analysis.md` | ✅ 已重写 | 从 21 行扩展到完整结构 |
| `Plugin-SDK-Optimization-Analysis.md` | ✅ 已重写 | 从 19 行扩展到完整结构 |
| `WEBVIEW_IPC.md` | ✅ 已重写 | 与 `WebView-IPC-Guide` 区分：前者速查，后者详细 |
| `WebView-IPC-Guide.md` | ⚠️ 旧 | 仍保留（1084 行大文档） |
| `WEB_PLUGIN_GUIDE.md` | ⚠️ 旧 | 与 `web-plugin-vanilla` 模板 README 重复 |
| `WebHost-Optimization-Design.md` | ⚠️ 设计中 | 明确标注"未实施"，保留作为方案讨论 |
