# WebView IPC 设计（当前实现）

## 组件

`WebHostService` 是进程内唯一的 Kestrel 宿主；`WebPluginView` 承载页面；`WebViewIpcHost` 负责 WebView 传输；`PluginRpcDispatcher` 分发命令；`SseEventPusher` 推送事件；`Channel<T>` 支持流式数据。浏览器 SDK 以嵌入资源提供在 `/sdk/lybox-plugin-sdk.js` 与 `/sdk/lybox-plugin-theme.css`。

## 路由

- `/{pluginId}/{**path}`：插件静态资源与入口页。
- `/sse/{pluginId}`：事件流。
- `/__bridge/{pluginId}/{action}`：桥接请求。
- `/{pluginId}/.lybox/{artifact}`：生成的 JS/TS 绑定等资源。
- `/__lybox/debug`：调试诊断。

静态资源仅允许 GET/HEAD，真实文件优先；无扩展名 History 路由才回退到入口页。宿主拒绝目录穿越、绝对路径、重解析点和插件目录外目标。

## 消息模型

IPC 使用 RPC envelope。调用、事件、通道关闭分别使用 `C`、`E`、`X` 前缀；规范化消息类型为 `plugin-rpc-call` 和 `plugin-rpc-result`。错误码包括 `invalid_request`、`invalid_payload`、`method_not_found`、`plugin_mismatch`、`cancelled`、`timeout`、`busy`、`handler_error`、`transport_error`、`channel_closed`、`slow_consumer`。

生成器扫描 `[RpcCommand]`，为绑定方法生成 `IRpcBindingSource` partial、客户端 JS 和 `.d.ts`。多参数方法需按生成器告警调整 payload 形状。

## 信任边界

每个 WebView 创建会话令牌；请求必须同时满足：session 有效、Origin 在允许列表、当前文档仍属于授权 Base URI、pluginId 匹配、桥接已启用。浏览器模拟模式只使用前端 mock，不获得宿主业务权限。通用消息大小有上限，调试控制台还有频率限制。

## 生命周期

插件注册后由宿主统一映射 Web 根目录，再按需启动 WebHost。页面握手完成后注入 SDK、注册系统命令、注入 RPC manifest 并启动事件订阅。插件关闭时应停止任务、释放客户端和通道；宿主反向调用 `ShutdownAsync`。

## 开发约束

不要依赖已删除的 `PluginWebAppService`、`PluginWebViewPage`、`PluginWebIpcService`、`@avalonia-template/plugin-sdk` 或前端 pnpm 工作区。新代码使用 `LYBox.Plugin.Shared.Web`、`WebPluginView`、`IRpcHost` 和嵌入式 SDK。
