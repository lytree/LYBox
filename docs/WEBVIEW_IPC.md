# WebView IPC 设计（当前实现）

> 本文档描述 WebView IPC 的**当前实现要点**。详细使用指南（API、前端 SDK、消息协议、示例、测试）见 [WebView-IPC-Guide.md](WebView-IPC-Guide.md)；架构优化方案见 [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md)。

---

## 1. 组件

| 组件 | 路径 | 作用 |
|---|---|---|
| `WebHostService` | `src/Plugin/LYBox.Plugin.Shared.Web/Web/` | 进程内唯一的 Kestrel 宿主（`127.0.0.1:0`），懒启动 |
| `WebPluginView` | 同上 | 承载前端页面的 Avalonia 控件（基于 `Avalonia.Controls.WebView` 12.0.1） |
| `WebViewIpcHost` | `Rpc/` | IPC 运行时，实现 `IRpcHost` |
| `WebViewIpcTransport` | `Rpc/` | 包装 `Avalonia.Controls.WebView` 的 `IRpcTransport` 适配 |
| `PluginRpcDispatcher` | `Rpc/` | 命令分发与回推 |
| `SseEventPusher` | `Rpc/` | 事件推送（SSE） |
| `Channel<T>` | `Rpc/` | 流式通道（Tauri 风格） |
| `SystemCommands` | `Web/` | 自动注册系统级 RPC（文件选择器、对话框） |
| 嵌入式 SDK | `Rpc/Assets/ipc.js` + `LYBox.Plugin.Shared.Web/Assets/lybox-plugin-sdk.{js,css}` | 由 `WebHostService` 在 `/sdk/` 路径提供 |

---

## 2. 路由

| 路径 | 用途 |
|---|---|
| `/{pluginId}/{**path}` | 插件静态资源 + 入口页 |
| `/sse/{pluginId}` | 事件流（SSE，GET） |
| `/__bridge/{pluginId}/{action}` | 桥接请求（`action = rpc` / `emit` / `channel-close`，POST） |
| `/{pluginId}/.lybox/{artifact}` | 生成器产出的 JS/TS 绑定 |
| `/sdk/lybox-plugin-sdk.js` + `/sdk/lybox-plugin-theme.css` | 嵌入式浏览器 SDK（按 `pluginId` 共享） |
| `/__lybox/debug` | **仅 Debug 构建**：Web 调试面板（RPC 命令 + SSE 事件流 + session 签发） |
| `/__lybox/ipc.js` | WebView IPC 引导脚本（由 `WebViewIpcHost` 注入页面，**仅 WebView 模式**） |

**静态资源路由规则**：

- 仅允许 `GET` / `HEAD`
- 真实文件优先；无扩展名 History 路由才回退到入口页
- 拒绝目录穿越（`..`）、绝对路径、重解析点（reparse points）、插件目录外目标

---

## 3. 消息模型

### 3.1 消息信封（前缀）

调用、事件、通道关闭分别使用单字符前缀：

| 前缀 | 消息类型 | 方向 |
|---|---|---|
| `C` | `CallMessage` | JS → C#（RPC 调用） |
| `E` | `EventMessage` | JS → C# / C# → JS（事件 emit） |
| `X` | 通道 ID 字符串 | JS → C#（关闭通道） |

规范化消息类型：`plugin-rpc-call`、`plugin-rpc-result`。

### 3.2 错误码

| 错误码 | 含义 |
|---|---|
| `invalid_request` | 请求格式错误（缺字段、非 JSON） |
| `invalid_payload` | payload 反序列化失败 |
| `method_not_found` | 命令未注册 |
| `plugin_mismatch` | session 的 pluginId 与请求不一致 |
| `cancelled` | 调用被取消（通道关闭 / session 撤销） |
| `timeout` | 调用超时 |
| `busy` | 处理器忙（已弃用，保留兼容） |
| `handler_error` | 处理器抛异常 |
| `transport_error` | InvokeScript / WebMessageReceived 失败 |
| `channel_closed` | 通道已关闭，写入被丢弃 |
| `slow_consumer` | 通道消费者速度不足 |

### 3.3 `[RpcCommand]` 生成器

`LYBox.Plugin.Generators/RpcCommandGenerator.cs` 扫描 `[RpcCommand]` 标注的方法，为每个含此特性的类生成 `IRpcBindingSource` partial（仅 `RegisterBindings`）。多参数方法需按生成器告警调整 payload 形状（建议用 DTO record）。

---

## 4. 信任边界

每个 WebView 创建**短期 session token**。请求必须同时满足：

- session 有效
- Origin 在允许列表（默认 BaseUrl，可经 `WebHostService.AllowedOrigins` 注入）
- 当前文档仍属于授权 Base URI
- pluginId 与 session 绑定一致
- 桥接已启用（`/{BaseUrl}/__bridge/...`）

**浏览器模拟模式**只使用前端 mock，不获得宿主业务权限。

**通用限制**：

- 消息大小有上限
- 调试控制台有频率限制
- 路径只能落在插件 `wwwroot` 边界内

---

## 5. 生命周期

```text
插件 RegisterAsync
  → 宿主注册 wwwroot 根目录（MapPluginRoot）
  → 按需启动 WebHost（无 Web 插件时 Kestrel 不启动）
  → WebView NavigationCompleted
    → 注入 ipc.js（WebView 模式）
    → WebViewIpcHost.InitializeAsync + InjectBindingsAsync
    → 握手（__lybox:ready）
    → 前端可发起 RPC / 创建 Channel / 订阅事件
  → 插件关闭
    → Stop Tasks / Dispose HttpClient / Close Channels
    → 宿主反向调用 ShutdownAsync
```

**Session 撤销触发条件**：

- 页面导航（WebView `NavigationCompleted`）
- WebView 卸载 / 重建
- 显式 session 过期

撤销会让该页正在执行的 RPC 收到取消信号。

---

## 6. 开发约束

### 必须使用

- `LYBox.Plugin.Shared.Web` NuGet 包
- `WebPluginView`（基于 `Avalonia.Controls.WebView` 12.0.1）
- `IRpcHost` 注册命令
- `IWebPlugin` 入口（`[GenerateMetadata]` 自动实现 `Web` 描述符）
- 嵌入式 SDK（`/sdk/lybox-plugin-sdk.js`）

### 已删除，不要使用

- ❌ `PluginWebAppService`
- ❌ `PluginWebViewPage`
- ❌ `PluginWebIpcService`
- ❌ `@avalonia-template/plugin-sdk` npm 包（已移除）
- ❌ `frontend/` monorepo（已移除，前端 SDK 改为嵌入资源）
- ❌ `tools/LYBox.MockServer`（lybox-mock dotnet tool，已移除）

---

## 7. 关键文件

| 路径 | 作用 |
|---|---|
| `src/Plugin/LYBox.Plugin.Shared.Web/Rpc/WebViewIpcHost.cs` | IPC 运行时 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Rpc/IRpcTransport.cs` | 传输抽象 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Rpc/Channel.cs` | 流式通道 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Rpc/SseEventPusher.cs` | 事件推送 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Rpc/RpcEnvelope.cs` | 消息信封 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Rpc/Assets/ipc.js` | WebView IPC 引导脚本 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Web/WebHostService.cs` | Kestrel 宿主 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Web/SystemCommands.cs` | 系统级命令 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Web/WebPluginView.axaml(.cs)` | Avalonia 页面承载 |
| `src/Plugin/LYBox.Plugin.Shared.Web/Assets/lybox-plugin-sdk.js` | 嵌入式浏览器 SDK |
| `src/Plugin/LYBox.Plugin.Shared.Web/Assets/lybox-plugin-theme.css` | 嵌入式主题 CSS |
| `src/Plugin/LYBox.Plugin.Shared.Web/PluginWebSdkResources.cs` | SDK 资源契约 |
| `src/Plugin/LYBox.Plugin.Generators/RpcCommandGenerator.cs` | RPC 绑定源生成器 |

---

## 8. 相关文档

- [WebView-IPC-Guide.md](WebView-IPC-Guide.md) — 详细使用指南（API、前端 SDK、消息协议、示例、测试）
- [WebHost-Optimization-Design.md](WebHost-Optimization-Design.md) — WebHost 优化设计方案（设计中，未实施）
- [WEB_PLUGIN_GUIDE.md](WEB_PLUGIN_GUIDE.md) — Web 插件整体指南（vanilla JS 模板）
- [AGENTS.md](../AGENTS.md#webview-ipc-调研结论特性分支-feat-avalonia-webview-ipc) — WebView IPC 调研结论与平台支持矩阵
