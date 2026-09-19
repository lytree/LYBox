---
name: lybox-web-plugin
description: "LYBox Web 插件开发规范：PluginKind 声明与 Web 描述符、wwwroot 静态资源、WebHostService 懒启动、WebView IPC（RPC/事件/Channel/SSE）、会话与 origin 约束、前端零依赖开发（window.__lybox）。新建或修改 plugins/ 下含 WebView/wwwroot 前端页面的插件时使用。"
risk: unknown
source: project
date_added: "2026-08-16"
---

# LYBox Web 插件开发规范

> 适用范围：`plugins/` 下含 WebView 页面、`wwwroot/` 前端资源、`[RpcCommand]` 后端命令的插件。
> 纯 Avalonia 原生插件请使用 `lybox-plugin` skill。
> 标 ⏳ 的条目为 `docs/WebHost-Optimization-Design.md` 中的**设计中**变更，实施前以现状为准。

---

## 🎯 何时使用本 Skill

- 新建 Web 插件（前端页面 + WebView 承载 + 宿主通信）
- 为 Web 插件添加 RPC 命令、事件推送、数据 Channel
- 编写插件前端页面（原生 `window.__lybox`，零依赖）或排查 WebView 加载问题
- 处理 wwwroot 打包、开发期资源回退、浏览器模式联调

---

## 🧬 Web 插件与非 Web 插件的核心差异

| 维度 | 非 Web 插件 | Web 插件 |
|------|------------|----------|
| 入口契约 | `IPluginMetadata` + `[GenerateMetadata]` | 额外 `<PluginKind>Web</PluginKind>` + 生成 `IWebPluginDescriptor` |
| 前端资源 | 无 | `wwwroot/`（入口页默认 `index.html`） |
| 后端命令 | 无 RPC | `[RpcCommand]` 方法 + 源生成器绑定 |
| 宿主服务 | 常规 DI 服务 | 额外使用 `WebHostService`（Kestrel 懒启动） |
| 页面承载 | Avalonia View | `WebPluginView`（封装 NativeWebView + IPC + 会话） |

**强制前提（当前实现）**：Web 插件在 csproj 声明 `<PluginKind>Web</PluginKind>`，构建期写入 `plugin.json` 的 `kind`/`web` 字段；宿主 `PluginLoader` 依据 `kind=Web` 识别并自动调用 `WebHostService.MapPluginRoot` 注册前端根目录（声明即注册），无需插件手动注册。

---

## 📦 csproj 与入口类（现状）

csproj 与非 Web 插件模板相同（见 `lybox-plugin` skill），差异在：声明 `<PluginKind>Web</PluginKind>`，并引用 Web 包 `LYBox.Plugin.Shared.Web`。入口类：

```csharp
[GenerateMetadata]   // 源生成器从 csproj 生成 IPluginMetadata + IWebPluginDescriptor
public partial class MyWebPlugin : IPluginMetadata
{
    // 元数据属性全部由源生成器从 csproj 注入，无需手写
}
```

`IWebPluginDescriptor`（`IWebPlugin.cs`，由源生成器生成）：
- `WwwrootPath` — 默认 `{PluginBaseDir}/wwwroot`；
- `EntryPage` — 默认 `index.html`。

宿主在 `RegisterWebPlugins` 阶段读取该描述符并自动调用 `WebHostService.MapPluginRoot`，插件**无需**手写注册逻辑。

⚠️ **wwwroot 拷贝现状**：普通 `dotnet build` **不拷贝** wwwroot；仅 `.\build.ps1 --build=plugin`（`CopyPluginWwwroot`，`build.cs`）拷贝。开发期直接 `dotnet run` 依赖 `ResolveDevWwwroot` 回退（`WebHostService.cs`）：
1. 环境变量 `LYBOX_PLUGIN_SRC_{PluginId}`（连字符转下划线）；
2. 从 `AVALONIA_EXTRA_PLUGINS_PATH` 向上最多 6 层找含 `wwwroot` 的祖先目录。
VS Code 调试用 "Debug Plugin - {Name}" 配置设置 `AVALONIA_EXTRA_PLUGINS_PATH` 指向 `artifacts/bin/{ProjectName}/debug`。

---

## 🖼️ 页面承载

```xml
<web:WebPluginView PluginId="{Binding PluginId}" />
```

- 命名空间：`xmlns:web="using:LYBox.Plugin.Shared.Web"`；
- `WebPluginView` 内部完成：WebView 创建 → 导航完成 → 会话创建（`CreateSession`）→ ipc.js 注入 → `configureRuntime`/`startSse` → 绑定注入（`OnNavigationCompleted`，`WebPluginView.axaml.cs:285-317`）；
- 绑定注册：页面首次初始化时 `WebPluginBindings.Register` 扫描插件程序集所有 `[RpcCommand]` 生成的 `IRpcBindingSource`（`WebPluginBindings.cs:26-44`）；
- 卸载：脱离视觉树时自动清理会话、IPC host、transport——不要在外部持有这些对象。

---

## 🔌 RPC 命令（C# → 前端暴露）

```csharp
public static class GreetCommands
{
    [RpcCommand]
    public static async Task<string> GreetAsync(string name)
        => $"Hello, {name}!";
}
```

- 实例命令类需公共无参构造（生成器建 `__instance` 单例，`RpcCommandGenerator.cs:81-85`）；
- 命令名用**短名**（如 `GreetAsync`），前端 `rpc('GreetAsync', ...)` 调用；
- `[RpcCommand]` 的 XML 注释已与实际调用方式一致：前端经 `window.__lybox.rpc('<Name>', ...args)` 调用。

---

## 🌉 IPC 通道模型（详见 docs/WebView-IPC-Guide.md）

| 通道 | 方向 | 机制 | 适用 |
|------|------|------|------|
| RPC 调用 | JS → C# | `invokeCSharpAction(body)` → `WebViewIpcHost`（`C` 前缀信封）→ Promise 回推 `window.__lybox.resolve(id,err,result)` | 请求/响应 |
| 事件推送 | C# → JS | `EmitEventAsync` 优先 SSE（`/sse/{pluginId}`），降级 `InvokeScript` | 低频通知 |
| Channel 流 | C# → JS | SSE `channel-data` 帧；关闭走 `X` 前缀信封或 `/__bridge/{pluginId}/channel-close` | 高频/流式数据 |

**关键约束**：
- JS→C# 原生通道 fire-and-forget，Promise 闭环由宿主 `ResolveAsync` 回推实现；
- 握手事件 `__lybox:ready`（ipc.js）仅用于宿主就绪状态机；绑定注入在 `configureRuntime` 之后立即执行；
- 序列化全部 string（JSON），无 binary 通道；
- `ResetDocument` 会取消进行中的 RPC——页面导航后旧的 pending Promise 不会 resolve。

**安全约束**：
- HTTP 统一入口 `POST /__bridge/{pluginId}/{action}`（`action = rpc | emit | channel-close`），需会话 token：`X-LYBox-Session` 头（SSE 用 `?session=` query），由 `WebPluginView` 创建/撤销；
- origin 校验（`TryAuthorize`，`WebHostService.cs`）：请求带 `Origin` 时须等于宿主 `BaseUrl` 或 `WebHostService` 构造函数注入的 `AllowedOrigins` 白名单；
- WebView 导航被 `PluginWebViewDevTools.IsAllowedNavigation` 限制在授权基 URI 内（scheme/host/port/路径前缀全匹配）——外链需走系统浏览器，不要尝试在 WebView 内导航。

---

## 💻 前端开发

### 原生 window.__lybox（零依赖）

```html
<script>
  (function wait() {
    if (window.__lybox) { main(); } else { setTimeout(wait, 100); }
  })();
  async function main() {
    const result = await window.__lybox.rpc('GreetAsync', 'world');
    window.__lybox.on('EventName', (data) => {});
  }
</script>
```

### 浏览器开发模式（脱离宿主）

宿主 WebView 是前端运行验证的唯一入口。前端开发时通过宿主调试配置在 WebView 内加载插件页面（见 `docs/WebView-IPC-Guide.md`），Mock 数据声明于前端项目 `src/.lybox/mock.json`。

---

## 🧪 验证清单

- [ ] csproj 声明 `<PluginKind>Web</PluginKind>`，入口类 `[GenerateMetadata]` + `partial`
- [ ] `wwwroot/index.html`（或描述符 `EntryPage`）存在
- [ ] `[RpcCommand]` 命令可从前端调用并 resolve
- [ ] 事件/Channel 在页面刷新后无泄漏（重复 on 不叠加）
- [ ] `.\build.ps1 --build=bin` 后 `--build=plugin`，zip 内含 `wwwroot/`
- [ ] 开发调试：VS Code "Debug Plugin - {Name}" 配置，`AVALONIA_EXTRA_PLUGINS_PATH` 指向 debug 输出
- [ ] 外链在系统浏览器打开（WebView 内导航被拦截）
- [ ] 无 Web 资源场景：确认占位提示友好展示

---

## ❌ 反模式

- 在**非 Web 插件**里引用 `Avalonia.Controls.WebView` 或 `LYBox.Plugin.Shared.Web`（SDK 拆包后应引用核心包 `LYBox.Plugin.Shared`）
- 手动创建 `NativeWebView` 而非使用 `WebPluginView`（会丢失会话/IPC/绑定注入）
- 绕过会话 token 直接 fetch 宿主端点（origin 校验会拒绝）
- 前端轮询代替事件/SSE 推送
- 在 WebView 内导航外部域名
- 忘记 `--build=bin` 就构建插件（SDK NuGet 本地源 `artifacts/packages/sdk/` 为空导致还原失败）
