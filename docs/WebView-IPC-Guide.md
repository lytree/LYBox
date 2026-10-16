# WebView IPC 使用指南

LYBox WebView IPC 是一套基于 `Avalonia.Controls.WebView` 的双向通讯框架，采用 **Wails v2 模型为主干 + Tauri Channel 流式扩展** 的混合策略，让宿主 C# 代码与嵌入前端页面之间实现类型安全、可流式推送的 RPC 通讯。

> **适用版本**：HostVersion 2.1.0+  
> **依赖项目**：`LYBox.Plugin.Shared`（契约）、`LYBox.Plugin.Shared.Web`（IPC 运行时/WebHost）、`LYBox.Plugin.Generators`  
> **测试覆盖**：WebView IPC 与生产 HTTP 会话均由 `tests/LYBox.Tests/Rpc/` 下的 TUnit 测试覆盖。

---

## 目录

- [设计概述](#设计概述)
- [架构分层](#架构分层)
- [快速开始](#快速开始)
- [C# 端 API](#c-端-api)
  - [`[RpcCommand]` 特性](#rpccommand-特性)
  - [`IRpcTransport` 传输抽象](#irpctransport-传输抽象)
  - [`IRpcHost` 主机接口](#irpchost-主机接口)
  - [`WebViewIpcHost` 运行时](#webviewipchost-运行时)
  - [`Channel<T>` 流式通道](#channelt-流式通道)
  - [系统级命令（SystemCommands）](#系统级命令systemcommands)
- [前端 JS API](#前端-js-api)
- [前端 SDK（嵌入式）](#前端-sdk嵌入式)
- [源生成器](#源生成器)
- [消息协议](#消息协议)
- [完整示例](#完整示例)
- [测试](#测试)
- [设计约束与已知限制](#设计约束与已知限制)

---

## 设计概述

### 混合策略

| 来源 | 借鉴点 | 说明 |
|------|--------|------|
| **Wails v2（主干）** | 前缀信封 `C`/`E`/`X` | 单字符串 fire-and-forget 通道，与 Avalonia WebView 的 `invokeCSharpAction` + `InvokeScript` 原语完全对齐 |
| **Wails v2（主干）** | `InvokeScript` 回推 Promise | 弥补 Avalonia WebView 的 JS→C# fire-and-forget 缺陷：C# 侧主动执行 `window.__lybox.resolve(callbackId, err, result)` |
| **Wails v2（主干）** | `window.go.*` 绑定表 | 前端通过命名空间路径调用宿主命令 |
| **Tauri（改进）** | 强类型命令 | 用 `LYBox.Plugin.Generators`（Roslyn 增量源生成器）对 `[RpcCommand]` 标注的方法生成 TS 声明 + 运行时校验，类似 `#[tauri::command]` 的编译期保证 |
| **Tauri（改进）** | `Channel<T>` 流式 | 在 Wails `EventsEmit` 之上包一层 `Channel<T>` 抽象（单订阅 + 自动取消），弥补 Wails 无原生流式的缺陷 |

### 控件身份

- **使用**：官方包 `Avalonia.Controls.WebView` 12.0.1（NuGet `avaloniaui` 所有者，Prefix Reserved，MIT）
- **禁止**：已废弃的社区库 `Avalonia.WebView`（ChisterWu/Jianfenghuaite，仅匹配 Avalonia 11.x）

### IPC 原语

Avalonia WebView 抽象层仅提供两个低级通道：

| 方向 | API | 语义 |
|------|-----|------|
| C# → JS | `await webView.InvokeScript(jsExpr:string):Task<string?>` | 执行任意 JS 表达式，返回值 JSON 字符串 |
| JS → C# | JS 调全局 `invokeCSharpAction(body:string)` → C# 订阅 `WebMessageReceived`，`e.Body:string?` | **fire-and-forget**：`invokeCSharpAction` 不返回值给 JS |

**关键约束**：JS→C# 是 fire-and-forget。要实现 Promise 模型，必须复刻 Wails 的 callback-ID 表 + C# 侧 `InvokeScript("window.__lybox.resolve(id,json)")` 回推。

---

## 架构分层

```
┌─────────────────────────────────────────────────────────────┐
│ 前端页面（浏览器/WebView）                                    │
│  ┌────────────────────────────────────────────────────┐     │
│  │ window.__lybox 运行时（ipc.js 注入）                │     │
│  │   - invoke(name, args): Promise                    │     │
│  │   - resolve(id, err, result)                       │     │
│  │   - on/off/emit/dispatch（事件）                   │     │
│  │   - channel.onData / channel.onClose               │     │
│  │   - setBindings(manifestJson) → 构建 window.go.*   │     │
│  └────────────────────────────────────────────────────┘     │
│              ↑ invokeCSharpAction        ↓ __lybox.resolve   │
└──────────────┼───────────────────────────┼──────────────────┘
               │                           │
┌──────────────┼───────────────────────────┼──────────────────┐
│              ▼                           │                  │
│  ┌──────────────────────┐    ┌──────────────────────────┐   │
│  │ IRpcTransport        │    │ WebViewIpcHost           │   │
│  │ （传输抽象）          │    │ （IPC 运行时）            │   │
│  │ - MessageReceived    │◄───│ - OnMessage: C/E/X 分发  │   │
│  │ - ExecuteScriptAsync │───►│ - ResolveAsync: 回推     │   │
│  └──────────────────────┘    │ - HandleCallAsync        │   │
│              ▲               │ - HandleEvent            │   │
│              │               │ - HandleChannelClose     │   │
│  ┌───────────┴────────┐      └──────────────────────────┘   │
│  │ WebViewIpcTransport│               ▲                     │
│  │ （宿主适配器）      │               │ implements          │
│  │ 包装 Avalonia      │      ┌────────┴──────────┐          │
│  │ .Controls.WebView  │      │ IRpcHost          │          │
│  └────────────────────┘      │ - RegisterCommand │          │
│                              │ - EmitEventAsync  │          │
│                              │ - CreateChannel<T>│          │
│                              │ - OnEvent         │          │
│                              └───────────────────┘          │
│                                     ▲                        │
│                                     │ registers              │
│                      ┌──────────────┴──────────────┐        │
│                      │ IRpcBindingSource（源生成器）│        │
│                      │ - RegisterBindings(host)    │        │
│                      └─────────────────────────────┘        │
│                                     ▲                        │
│                     [RpcCommand] 标注的方法                   │
│                     （用户业务代码）                          │
└──────────────────────────────────────────────────────────────┘
```
### 项目职责

| 项目 | 职责 |
|------|------|
| `LYBox.Plugin.Shared` | 契约层（核心包）：`[RpcCommand]`、`RpcCommandAttribute` 等共享契约，不含 Web 依赖 |
| `LYBox.Plugin.Shared.Web` | Web 包（依赖 ASP.NET Core / WebView）：`IRpcTransport`、`IRpcHost`、`IRpcBindingSource`、`Channel<T>`、`RpcEnvelope`、`WebViewIpcHost`、`SseEventPusher`、`WebHostService`、`WebPluginView`、嵌入式 `ipc.js` |
| `LYBox.Plugin.Generators` | Roslyn 增量源生成器：扫描 `[RpcCommand]` 生成 `IRpcBindingSource` partial 实现（仅 `RegisterBindings`） |
| 宿主项目 | 提供 `WebViewIpcTransport`（包装 `Avalonia.Controls.WebView`）+ 演示页面 |

---

## 快速开始

### 1. 定义 RPC 服务

在任意 C# 项目中（引用 `LYBox.Plugin.Shared` + `LYBox.Plugin.Generators`），创建一个 `partial` 类并标注方法：

```csharp
using LYBox.Plugin.Shared.Attributes;
using LYBox.Plugin.Shared.Rpc;

namespace MyApp.Services;

// 必须 partial，源生成器会生成同名 partial 实现 IRpcBindingSource
public partial class CounterService
{
    [RpcCommand]
    public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);

    [RpcCommand]
    public string Greet(string name) => $"Hello, {name}!";

    // 自定义命令名（前端调用 window.go.MyApp.Services.CounterService.Increment）
    [RpcCommand("Increment")]
    public void Increment() => _count++;
}
```

**要求**：
- 类必须声明为 `partial`
- 实例方法所在类须有公共无参构造函数（生成器创建单例实例）
- 方法参数与返回值必须可被 `System.Text.Json` 序列化
- 返回 `Channel<T>` 表示流式通道（见 [Channel\<T\>](#channelt-流式通道)）

### 2. 启动 IPC 主机

在宿主代码中创建 `WebViewIpcHost`，注册命令，注入到 WebView：

```csharp
using LYBox.Plugin.Shared.Rpc;

// transport 由宿主提供，包装 Avalonia.Controls.WebView
IRpcTransport transport = new WebViewIpcTransport(webView);
WebViewIpcHost host = new(transport);

// 注册命令（源生成器生成的 partial 类实现 IRpcBindingSource）
new MyApp.Services.CounterService().RegisterBindings(host);

// 注入引导脚本 + 绑定清单
await host.InitializeAsync();           // 注入 ipc.js
await host.InjectBindingsAsync();       // 注入 window.go.* 绑定
await host.WhenReady;                    // 等待前端握手
```

### 3. 前端调用

前端页面（HTML/JS）无需手动引入任何库——宿主会注入 `ipc.js`。直接调用 `window.go.*`：

```html
<script>
  // 等待运行时就绪
  window.__lybox && window.__lybox.on('__lybox:ready', async () => {
    const sum = await window.go.MyApp.Services.CounterService.AddAsync(3, 4);
    console.log(sum); // 7

    const greeting = await window.go.MyApp.Services.CounterService.Greet('World');
    console.log(greeting); // "Hello, World!"
  });
</script>
```

---

## C# 端 API

### `[RpcCommand]` 特性

**命名空间**：`LYBox.Plugin.Shared.Attributes`  
**文件**：[src/Plugin/LYBox.Plugin.Shared/Attributes/RpcCommandAttribute.cs](../src/Plugin/LYBox.Plugin.Shared/Attributes/RpcCommandAttribute.cs)

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RpcCommandAttribute : Attribute
{
    public string? Name { get; }
    public RpcCommandAttribute() { }
    public RpcCommandAttribute(string name) => Name = name;
}
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string?` | 命令名。缺省为方法名。前端通过 `window.__lybox.rpc('<Name>', ...args)` 调用 |

**约束**：
- 仅可标注方法（`AttributeTargets.Method`）
- 不可重复标注（`AllowMultiple = false`）
- 不可继承（`Inherited = false`）
- 标注类必须为 `partial`

### `IRpcTransport` 传输抽象

**命名空间**：`LYBox.Plugin.Shared.Rpc`  
**文件**：[src/Plugin/LYBox.Plugin.Shared.Web/Rpc/IRpcTransport.cs](../src/Plugin/LYBox.Plugin.Shared.Web/Rpc/IRpcTransport.cs)

```csharp
public interface IRpcTransport
{
    event Action<string?>? MessageReceived;
    Task<string?> ExecuteScriptAsync(string javaScript, CancellationToken cancellationToken = default);
}
```

把 IPC 运行时与具体 WebView 控件解耦。任何能"接收 JS 字符串 + 执行 JS"的载体都可实现此接口。

| 成员 | 方向 | 对应 Avalonia WebView 原语 |
|------|------|---------------------------|
| `MessageReceived` 事件 | JS → C# | `WebMessageReceived` 事件（JS 经 `invokeCSharpAction(body)` 发来） |
| `ExecuteScriptAsync` 方法 | C# → JS | `webView.InvokeScript(js)` |

**测试替身**：`FakeTransport`（`tests/LYBox.Tests/Rpc/FakeTransport.cs`）用于无 WebView 环境下的单元测试。

### `IRpcHost` 主机接口

**命名空间**：`LYBox.Plugin.Shared.Rpc`  
**文件**：[src/Plugin/LYBox.Plugin.Shared.Web/Rpc/IRpcHost.cs](../src/Plugin/LYBox.Plugin.Shared.Web/Rpc/IRpcHost.cs)

```csharp
public delegate Task<object?> RpcCommandHandler(JsonElement[] args, CancellationToken cancellationToken);

public interface IRpcHost
{
    void RegisterCommand(string name, RpcCommandHandler handler);
    Task EmitEventAsync(string name, object? data, CancellationToken cancellationToken = default);
    Channel<T> CreateChannel<T>(string? id = null);
}
```

| 方法 | 方向 | 说明 |
|------|------|------|
| `RegisterCommand` | 注册 | 注册命令处理器。命令名建议为 `"Namespace.Class.Method"` 形式 |
| `EmitEventAsync` | C# → JS | 向前端分发事件，经 `window.__lybox.dispatch(name, data)` |
| `CreateChannel<T>` | C# → JS | 创建流式通道，向前端推送数据 |

**事件订阅**（`WebViewIpcHost` 扩展方法）：

```csharp
// 订阅来自前端的事件（JS 经 window.__lybox.emit 发送）
Action unsub = host.OnEvent("user.click", data => {
    // data: JsonElement?
});
// 取消订阅
unsub();
```

### `WebViewIpcHost` 运行时

**命名空间**：`LYBox.Plugin.Shared.Rpc`  
**文件**：[src/Plugin/LYBox.Plugin.Shared.Web/Rpc/WebViewIpcHost.cs](../src/Plugin/LYBox.Plugin.Shared.Web/Rpc/WebViewIpcHost.cs)

实现 `IRpcHost`，在 `IRpcTransport.MessageReceived` 上按 Wails v2 前缀信封分发。

| 成员 | 说明 |
|------|------|
| `WebViewIpcHost(IRpcTransport transport)` | 构造函数，订阅传输层消息 |
| `Task WhenReady` | 前端 `__lybox` 运行时就绪（握手完成）。注入引导脚本后等待此任务 |
| `Task InitializeAsync(CancellationToken)` | 注入 `ipc.js` 引导脚本到页面。**幂等** |
| `Task InjectBindingsAsync(CancellationToken)` | 把已注册命令的清单注入页面，构建 `window.go`。调用前应完成所有 `RegisterCommand` |
| `void RegisterCommand(string, RpcCommandHandler)` | 注册命令。同名覆盖：后注册生效 |
| `Task EmitEventAsync(string, object?, CancellationToken)` | 向前端分发事件 |
| `Channel<T> CreateChannel<T>(string?)` | 创建流式通道 |
| `Action OnEvent(string, Action<JsonElement?>)` | 订阅前端事件，返回取消订阅函数 |

**典型启动顺序**：

```csharp
WebViewIpcHost host = new(transport);

// 1. 注册所有命令
new CounterService().RegisterBindings(host);
new FileService().RegisterBindings(host);

// 2. 注入引导脚本
await host.InitializeAsync();

// 3. 注入绑定清单（构建 window.go.*）
await host.InjectBindingsAsync();

// 4. 等待前端握手
await host.WhenReady;

// 5. 此后可主动 emit 事件 / 创建 channel
await host.EmitEventAsync("app.started", new { version = "2.1.0" });
```

### `Channel<T>` 流式通道

**命名空间**：`LYBox.Plugin.Shared.Rpc`  
**文件**：[src/Plugin/LYBox.Plugin.Shared.Web/Rpc/Channel.cs](../src/Plugin/LYBox.Plugin.Shared.Web/Rpc/Channel.cs)

借鉴 Tauri Channel，弥补 Wails 无原生流式的缺陷。C# 侧创建并拥有，每次 `WriteAsync` 把数据推送到前端。

```csharp
public sealed class Channel<T> : Channel, IAsyncDisposable
{
    public string Id { get; }
    public bool Closed { get; }

    public ValueTask WriteAsync(T item, CancellationToken cancellationToken = default);
    public override ValueTask CloseAsync();
    public ValueTask DisposeAsync();
}
```

| 方法 | 说明 |
|------|------|
| `WriteAsync(T)` | 向前端推送一条数据。经 `window.__lybox.channel.onData(id, json)` |
| `CloseAsync()` | 关闭通道并通知前端。**幂等**。经 `window.__lybox.channel.onClose(id)` |
| `DisposeAsync()` | `CloseAsync` + 释放 `CancellationTokenSource` |

**特性**：
- **单订阅**：前端通过 `channel.on(cb)` 订阅，返回取消订阅函数
- **自动取消**：`CloseAsync` 时取消内部 `CancellationTokenSource`，未完成的 `WriteAsync` 静默丢弃
- **幂等关闭**：重复 `CloseAsync` 不重复推送
- **关闭后丢弃**：`Closed` 状态下 `WriteAsync` 静默返回

**两种创建方式**：

```csharp
// 方式 1：作为命令返回值（前端 Promise resolve 为通道描述符）
public partial class StreamService
{
    [RpcCommand]
    public Channel<int> StartProgress()
    {
        var ch = host.CreateChannel<int>();  // host 由 DI 注入
        _ = Task.Run(async () => {
            for (int i = 0; i <= 100; i += 10)
            {
                await ch.WriteAsync(i);
                await Task.Delay(100);
            }
            await ch.CloseAsync();
        });
        return ch;
    }
}

// 方式 2：宿主主动创建（非命令路径）
var ch = host.CreateChannel<string>("my-channel-id");
await ch.WriteAsync("hello");
```

**前端订阅**：

```javascript
const ch = await window.go.MyApp.Services.StreamService.StartProgress();
const unsub = ch.on(progress => {
    console.log('进度:', progress);
    if (progress >= 100) unsub();
});
// 前端主动关闭
// ch.close();
```

---

### 系统级命令（SystemCommands）

**文件**：[src/Plugin/LYBox.Plugin.Shared.Web/Web/SystemCommands.cs](../src/Plugin/LYBox.Plugin.Shared.Web/Web/SystemCommands.cs)

`SystemCommands` 是一个静态注册器，在 `WebPluginView` 初始化时自动将系统级 RPC 命令注册到每个 WebView 实例。所有 web 插件无需编写任何 C# 代码即可调用系统能力。

#### 注册机制

```csharp
// WebPluginView.axaml.cs 中的自动注册（开发者无需手动调用）
RegisterPluginBindings(pluginId);
SystemCommands.Register(_host, () => TopLevel.GetTopLevel(this));
```

#### 命令清单

| 命令名 | 底层 API | 参数 | 返回值 | 说明 |
|--------|---------|------|--------|------|
| `OpenFilePicker` | `StorageProvider.OpenFilePickerAsync` | `OpenFilePickerOptions` | `string[]` | 打开文件选择器，返回选中文件路径数组 |
| `SaveFilePicker` | `StorageProvider.SaveFilePickerAsync` | `SaveFilePickerOptions` | `string \| null` | 打开保存文件对话框，返回保存路径或 null（取消） |
| `OpenFolderPicker` | `StorageProvider.OpenFolderPickerAsync` | `OpenFolderPickerOptions` | `string[]` | 打开文件夹选择器，返回路径数组 |
| `ShowMessageBox` | `Ursa.OverlayMessageBox.ShowAsync` | `MessageBoxOptions` | `MessageBoxResult` | 显示消息框 |
| `ShowConfirmDialog` | `Ursa.OverlayMessageBox.ShowAsync` | `ConfirmDialogOptions` | `boolean` | 显示确认对话框（Yes/No） |

#### 参数类型

**文件选择器选项**：

```typescript
// 文件过滤器
interface FileFilter {
  name: string;              // 过滤器显示名称，如"图片"
  extensions: string[];      // 扩展名列表（不含点），如 ['png', 'jpg']
}

// 打开文件选择器
interface OpenFilePickerOptions {
  title?: string;            // 对话框标题
  multiple?: boolean;        // 是否允许多选（默认 false）
  filters?: FileFilter[];    // 文件类型过滤器
}

// 保存文件选择器
interface SaveFilePickerOptions {
  title?: string;
  suggestedFileName?: string;  // 建议的文件名（不含路径）
  filters?: FileFilter[];
}

// 打开文件夹选择器
interface OpenFolderPickerOptions {
  title?: string;
  multiple?: boolean;
}
```

**对话框选项**：

```typescript
type MessageBoxIcon = 'info' | 'warning' | 'error' | 'success';
type MessageBoxButton = 'OK' | 'YesNo' | 'YesNoCancel';

interface MessageBoxOptions {
  message: string;            // 消息内容
  title?: string;             // 对话框标题
  button?: MessageBoxButton;   // 按钮组合（默认 'OK'）
  icon?: MessageBoxIcon;       // 图标（默认 'info'）
}

interface ConfirmDialogOptions {
  message: string;
  title?: string;
  icon?: MessageBoxIcon;       // 默认 'warning'
}

type MessageBoxResult = 'OK' | 'Yes' | 'No' | 'Cancel';
```

#### 前端调用示例

**原生 JS（浏览器环境）**：

```javascript
// 文件选择器
const files = await window.__lybox.rpc('OpenFilePicker', {
  title: '选择图片',
  multiple: true,
  filters: [
    { name: '图片', extensions: ['png', 'jpg', 'jpeg'] },
    { name: '所有文件', extensions: ['*'] }
  ]
});
// files: ["C:/path/to/file1.png", "C:/path/to/file2.jpg"]

// 保存文件对话框
const savePath = await window.__lybox.rpc('SaveFilePicker', {
  suggestedFileName: 'untitled.txt',
  filters: [{ name: '文本文件', extensions: ['txt'] }]
});
// savePath: "C:/path/to/untitled.txt" 或 null（用户取消）

// 消息框
const result = await window.__lybox.rpc('ShowMessageBox', {
  message: '确定要删除吗？',
  title: '确认',
  button: 'YesNo',
  icon: 'warning'
});
// result: "Yes" 或 "No"

// 确认对话框
const confirmed = await window.__lybox.rpc('ShowConfirmDialog', {
  message: '确定要保存更改吗？'
});
// confirmed: true 或 false
```

**嵌入式 SDK（推荐，零构建）**：

```typescript
import { invoke } from '/sdk/lybox-plugin-sdk.js';

const files = await invoke('System.OpenFilePicker', {
  title: '选择文件',
  multiple: true,
  filters: [{ name: '文本', extensions: ['txt', 'md'] }]
});

const confirmed = await invoke('System.ShowConfirmDialog', { message: '确定删除？' });
if (confirmed) {
  // 执行删除
}
```

#### 跨平台说明

- **文件选择器**：基于 Avalonia `StorageProvider`，自动适配 Windows（文件资源管理器）、macOS（Finder）、Linux（GTK 文件对话框）
- **对话框**：基于 Ursa `OverlayMessageBox`，在宿主窗口内以模态覆盖层形式显示，风格与宿主 UI 一致
- **线程安全**：所有命令在 UI 线程执行，`StorageProvider` 与 `OverlayMessageBox` 的异步调用由 Avalonia 调度器自动处理

#### Mock 支持

Mock Server 已内置系统命令的 mock 响应，浏览器调试模式下返回模拟数据：

```json
{
  "OpenFilePicker": { "delay": 100, "result": ["C:/mock/example.txt"] },
  "SaveFilePicker": { "delay": 100, "result": "C:/mock/untitled.txt" },
  "OpenFolderPicker": { "delay": 100, "result": ["C:/mock/projects"] },
  "ShowMessageBox": { "delay": 100, "result": "OK" },
  "ShowConfirmDialog": { "delay": 100, "result": true }
}
```

---

## 前端 JS API

**文件**：[src/Plugin/LYBox.Plugin.Shared.Web/Rpc/Assets/ipc.js](../src/Plugin/LYBox.Plugin.Shared.Web/Rpc/Assets/ipc.js)

宿主通过 `InitializeAsync()` 注入 `ipc.js`，提供 `window.__lybox` 运行时。前端无需手动引入任何库。

### `window.__lybox` API

| 方法 | 签名 | 说明 |
|------|------|------|
| `invoke` | `(name: string, args: any[]): Promise<any>` | 发起 RPC 调用。返回 Promise |
| `resolve` | `(id: string, err: string|null, result: any)` | C# 回推调用结果（前端通常不直接调用） |
| `on` | `(name: string, cb: (data) => void): () => void` | 订阅事件，返回取消订阅函数 |
| `off` | `(name: string, cb: (data) => void)` | 取消订阅事件 |
| `emit` | `(name: string, data: any)` | 向 C# 发送事件 |
| `dispatch` | `(name: string, data: any)` | C# 向前端分发事件（前端通常不直接调用） |
| `setBindings` | `(manifestJson: string)` | 注入命令清单，构建 `window.go.*`（由宿主调用） |
| `channel.on` | `(id: string, cb: (data, closed?) => void): () => void` | 订阅通道数据 |
| `channel.onData` | `(id: string, data: any)` | C# 推送通道数据（由宿主调用） |
| `channel.onClose` | `(id: string)` | C# 通知通道关闭（由宿主调用） |

### `window.go.*` 绑定

宿主调用 `InjectBindingsAsync` 后，根据命令清单构建命名空间路径：

```javascript
// 命令 "MyApp.Services.CounterService.AddAsync" 映射为：
window.go.MyApp.Services.CounterService.AddAsync(a, b) // → Promise<number>
```

每个绑定函数内部调用 `window.__lybox.invoke(id, args)`，返回 Promise。

### 握手协议

`ipc.js` 注入后立即发送 `'E' + __lybox:ready` 事件，C# 侧 `WebViewIpcHost.WhenReady` TaskCompletionSource 等待此事件完成握手。

```javascript
// 前端等待就绪的标准写法
window.__lybox.on('__lybox:ready', async () => {
    // 此后可安全调用 window.go.*
});
```

---


## 前端 SDK（嵌入式）

**来源**：[`src/Plugin/LYBox.Plugin.Shared.Web/Assets/lybox-plugin-sdk.js`](../src/Plugin/LYBox.Plugin.Shared.Web/Assets/lybox-plugin-sdk.js)

LYBox 不再依赖 npm / pnpm / 任何前端构建工具。前端 SDK 与主题 CSS 直接以 **嵌入资源** 形式打包在 `LYBox.Plugin.Shared.Web` 中，由宿主 `WebHostService`（宿主内嵌 Kestrel）在 `/sdk/` 路径下提供。

### 引入方式

```html
<!-- 在插件 wwwroot/index.html 中 -->
<link rel="stylesheet" href="/sdk/lybox-plugin-theme.css" />
<script type="module" src="/sdk/lybox-plugin-sdk.js"></script>
```

#### Vite 工程：改为运行时注入

Vite 会在构建期把 `index.html` 里以 `/` 开头的静态资源当项目内文件解析，上面两行直接写会导致构建失败：
`Failed to resolve /sdk/lybox-plugin-sdk.js from .../index.html`。

Vite 工程请改成运行时注入。SDK 必须仍是 `type="module"`——它是 ES Module（文末有 `export`），
用经典脚本加载会抛 `SyntaxError`，整份脚本不执行、`window.LyboxPlugin` 永远挂不上。
注入的模块脚本不阻塞首屏，而 `window.LyboxPlugin` 只在真正发起 RPC 时才被读取，时序上足够。

```html
<script>
  (function () {
    var theme = document.createElement('link');
    theme.rel = 'stylesheet';
    theme.href = '/sdk/lybox-plugin-theme.css';
    document.head.appendChild(theme);

    var sdk = document.createElement('script');
    sdk.type = 'module';
    sdk.src = '/sdk/lybox-plugin-sdk.js';
    document.head.appendChild(sdk);
  })();
</script>
```

参考实现：`LYBox.Plugins/plugins/LYBox.Plugin.Zitie/ui/index.html`。

加载后 SDK 自动把 `window.LyboxPlugin` 挂到全局。两种导入方式：

```javascript
// 方式 A：从全局命名空间直接调用
const greeting = await window.LyboxPlugin.invoke('GreetAsync', 'World');

// 方式 B：ES Module 导入（仅当构建工具支持相对路径 / SDK 已在同源）
import { invoke, on, request, getJson } from '/sdk/lybox-plugin-sdk.js';
```

### 端点

| 路径 | 提供者 | 用途 |
|------|--------|------|
| `/sdk/lybox-plugin-sdk.js` | `WebHostService`（宿主内嵌 Kestrel） | 浏览器端 SDK（ES Module） |
| `/sdk/lybox-plugin-theme.css` | 同上 | Fluent Design 主题 CSS 变量 |
| `/__lybox/ipc.js` | 由 `WebViewIpcHost` 注入页面 | WebView IPC 引导脚本（`window.__lybox`） |
| `/__bridge/{pluginId}/rpc` | 宿主 | RPC（POST） |
| `/__bridge/{pluginId}/emit` | 宿主 | 事件 emit（POST） |
| `/__bridge/{pluginId}/channel-close` | 宿主 | 通道关闭（POST） |
| `/sse/{pluginId}` | 宿主 | SSE 事件推送（GET） |
| `/{pluginId}/{**path}` | 宿主（`wwwroot`） | 静态资源 |

所有端点由宿主 `LYBox.Plugin.Shared.Web` 内的 Kestrel 应用统一注册。

### 核心 API

#### RPC 调用

```javascript
import { invoke } from '/sdk/lybox-plugin-sdk.js';

// 简单调用
const greeting = await invoke('GreetAsync', 'World');
const sum = await invoke('AddAsync', 3, 5);

// 返回结构化数据
const info = await invoke('GetPluginInfo');
console.log(info.version);
```

`invoke` 会按下列顺序检测可用通道：

1. `window.__lybox.rpc`（WebView 内）→ 原生 Promise 通道
2. `window.__lyboxRuntime.mockBaseUrl`（mock 模式）→ HTTP `POST /__lybox/ipc`
3. `mockRegistry[method]`（纯本地 mock）→ 在浏览器中注册的内置 handler

#### 事件订阅（SSE）

```javascript
import { on } from '/sdk/lybox-plugin-sdk.js';

// 订阅宿主推送的事件
const unsubscribe = on('tick', (data) => {
    console.log(`[${data.time}] #${data.count} ${data.message}`);
});

// 取消订阅
unsubscribe();
```

WebView 模式下事件经 `window.__lybox.onPluginEvent(...)` 派发；mock 模式下 SDK 自动建立 `EventSource` 到 `/sse/{pluginId}`。

#### HTTP 请求

```javascript
import { request, getJson } from '/sdk/lybox-plugin-sdk.js';

// 自定义请求（带 method / headers / body）
const response = await request('/custom-endpoint', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ foo: 'bar' })
});

// 便捷方法：GET + 自动 JSON 解析
const data = await getJson('/api/list');
```

#### 环境检测

```javascript
import { isLyboxBridgeAvailable } from '/sdk/lybox-plugin-sdk.js';

if (isLyboxBridgeAvailable()) {
    // WebView 环境（原生 IPC 可用）
} else {
    // 浏览器环境（走 mock HTTP / SSE）
}
```

### 主题变量

`lybox-plugin-theme.css` 暴露 `--lybox-*` CSS 变量，与宿主 `Theme/FluentDesign/Light.axaml` / `Dark.axaml` 完全一致：

```css
:root {
  --lybox-color-primary: #0078D4;
  --lybox-color-text:    #1A1A1A;
  --lybox-color-surface: #FFFFFF;
  /* ... */
}

@media (prefers-color-scheme: dark) {
  :root:not([data-lybox-theme="light"]) {
    --lybox-color-primary: #60CDFF;
    --lybox-color-text:    #FFFFFF;
    --lybox-color-surface: #2C2C2C;
    /* ... */
  }
}
```

宿主 `WebPluginView` 在**导航完成**与**主题切换**时会向页面根元素写入 `data-lybox-theme="dark|light"`
（取值自 `TopLevel.ActualThemeVariant`），驱动上面的深/浅色分支。

**不要只依赖 `prefers-color-scheme`**：WebView2 的媒体查询取值来自操作系统主题，
宿主内切换主题（与系统不一致时）不会改变它，插件页面就会停留在错误配色。

### 完整 demo

参考 [`plugins/LYBox.Plugin.WebTemplate/wwwroot/index.html`](../plugins/LYBox.Plugin.WebTemplate/wwwroot/index.html)——它是一个零依赖的 vanilla HTML/JS 演示，覆盖 RPC、SSE、文件选择器、对话框等所有 API。

### 调试流程

无需启动 Vite / pnpm / 任何前端 dev server。

#### Avalonia WebView 生产模式

```powershell
dotnet run --project src/App/LYBox.Launcher.Desktop
```

`WebViewIpcHost` 启动时会自动把 `ipc.js`（嵌入资源 `LYBox.Plugin.Shared.Web.Rpc.Assets.ipc.js`）注入到每个 WebView 页面，挂上 `window.__lybox`。前端再按上面“引入方式”加载 SDK 即可。

```
┌─────────────────────────────────────────────────────────────┐
│ 生产模式（Avalonia WebView）                                  │
│                                                             │
│  WebView ←── ipc.js 注入 ── Host (WebViewIpcHost)           │
│       │                                    │                 │
│       │ window.__lybox.rpc()               │ C# [RpcCommand] │
│       │ window.__lybox.on()                │ EmitEventAsync  │
│       └────────────────────────────────────┘                 │
│                                                             │
│  启动：dotnet run（宿主加载插件）                              │
└─────────────────────────────────────────────────────────────┘
```
---

## 源生成器

**命名空间**：`LYBox.Plugin.Generators`  
**文件**：[src/Plugin/LYBox.Plugin.Generators/RpcCommandGenerator.cs](../src/Plugin/LYBox.Plugin.Generators/RpcCommandGenerator.cs)

扫描 `[RpcCommand]` 标注的方法，为每个含此特性的类生成 partial 实现 `IRpcBindingSource`。

### 生成的代码

对于以下标注：

```csharp
public partial class CounterService
{
    [RpcCommand]
    public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
}
```

生成器产出（`CounterService.Rpc.g.cs`）：

```csharp
public partial class CounterService : global::LYBox.Plugin.Shared.Web.Rpc.IRpcBindingSource
{
    private static readonly CounterService __instance = new();

    public void RegisterBindings(global::LYBox.Plugin.Shared.Web.Rpc.IRpcHost host)
    {
        host.RegisterCommand("AddAsync", async (args, ct) =>
        {
            var p0 = 0 < args.Length ? args[0].Deserialize<int>() : default;
            var p1 = 1 < args.Length ? args[1].Deserialize<int>() : default;
            var __r = await __instance.AddAsync(p0, p1);
            return (object?)__r;
        });
    }
}
```

> 命令名使用**短名**（方法名，如 `AddAsync`）注册，前端经 `window.__lybox.rpc('AddAsync', ...)` 调用。前端类型安全由插件作者在自己的前端代码中维护一份 TypeScript 方法表客户端即可（任何方式：手写 interface、JSDoc、生成器均自由）。

### 生成器约束

- 使用 `ForAttributeWithMetadataName` 精准匹配 `LYBox.Plugin.Shared.Attributes.RpcCommandAttribute`
- 按类的 `ToDisplayString()` 分组，每个类生成一个文件
- 实例方法：生成 `__instance` 单例字段，要求类有公共无参构造函数
- 静态方法：直接通过类名调用
- 参数按位置从 `JsonElement[]` 反序列化，越界时使用 `default`
- 返回 `Channel<T>` 时，`WebViewIpcHost` 自动包装为 `{ __channel: true, id, itemType }` 描述符

---

## 消息协议

**文件**：[src/Plugin/LYBox.Plugin.Shared.Web/Rpc/RpcEnvelope.cs](../src/Plugin/LYBox.Plugin.Shared.Web/Rpc/RpcEnvelope.cs)

### JS → C#（经 `invokeCSharpAction(body)`，body 为字符串）

| 前缀 | 负载 | 语义 |
|------|------|------|
| `C` | `CallMessage` JSON | RPC 调用 |
| `E` | `EventMessage` JSON | 事件 emit |
| `X` | 通道 ID（纯字符串） | JS 关闭通道 |

```csharp
public sealed record CallMessage
{
    public string Name { get; init; } = "";
    public JsonElement[] Args { get; init; } = Array.Empty<JsonElement>();
    public string CallbackId { get; init; } = "";
}

public sealed record EventMessage
{
    public string Name { get; init; } = "";
    public JsonElement? Data { get; init; }
}
```

### C# → JS（经 `IRpcTransport.ExecuteScriptAsync`，执行 JS 表达式）

| 操作 | JS 调用 |
|------|---------|
| resolve | `window.__lybox.resolve(callbackId, errJson, resultJson)` |
| 事件分发 | `window.__lybox.dispatch(name, dataJson)` |
| 通道数据 | `window.__lybox.channel.onData(channelId, dataJson)` |
| 通道关闭 | `window.__lybox.channel.onClose(channelId)` |

### JSON 序列化选项

`RpcEnvelope.JsonOptions`：

| 选项 | 值 | 说明 |
|------|-----|------|
| `PropertyNamingPolicy` | `null` | 保持 PascalCase（与 C# 属性名一致） |
| `DefaultIgnoreCondition` | `WhenWritingNull` | 忽略 null 值 |
| `Encoder` | `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` | 不转义非 ASCII（中文/emoji），让 JS 侧调试可读 |
| `Converters` | `JsonStringEnumConverter` | 枚举序列化为字符串 |

---

## 完整示例

### 场景：文件下载进度推送

**C# 端**：

```csharp
using LYBox.Plugin.Shared.Attributes;
using LYBox.Plugin.Shared.Rpc;

namespace MyApp.Services;

public partial class DownloadService
{
    // 注入的宿主（实际项目中通过 DI 获取）
    private IRpcHost _host;

    public DownloadService(IRpcHost host) => _host = host;

    [RpcCommand]
    public async Task<Channel<DownloadProgress>> DownloadAsync(string url)
    {
        var ch = _host.CreateChannel<DownloadProgress>();

        _ = Task.Run(async () =>
        {
            try
            {
                using var client = new HttpClient();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var total = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    read += n;
                    await ch.WriteAsync(new DownloadProgress(read, total));
                }
            }
            finally
            {
                await ch.CloseAsync();
            }
        });

        return ch;
    }
}

public record DownloadProgress(long BytesRead, long TotalBytes);
```

**前端**：

```html
<script>
window.__lybox.on('__lybox:ready', async () => {
    const url = 'https://example.com/large-file.zip';
    const ch = await window.go.MyApp.Services.DownloadService.DownloadAsync(url);

    ch.on(progress => {
        if (progress === null) return; // 关闭信号
        const pct = progress.TotalBytes > 0
            ? Math.round(progress.BytesRead / progress.TotalBytes * 100)
            : 0;
        console.log(`下载进度: ${pct}% (${progress.BytesRead}/${progress.TotalBytes})`);
    });
});
</script>
```

### 场景：双向事件通讯

**C# 端**：

```csharp
// 订阅前端事件
host.OnEvent("user.login", data =>
{
    if (data is not null)
    {
        var username = data.Value.GetProperty("username").GetString();
        Console.WriteLine($"用户登录: {username}");
    }
});

// 向前端推送事件
await host.EmitEventAsync("app.notification", new
{
    title = "系统通知",
    message = "操作已完成",
    level = "info"
});
```

**前端**：

```javascript
// 发送事件到 C#
window.__lybox.emit('user.login', { username: 'alice' });

// 订阅 C# 推送的事件
window.__lybox.on('app.notification', notif => {
    showToast(notif.title, notif.message, notif.level);
});
```

---

## 测试

**文件**：[tests/LYBox.Tests/Rpc/WebViewIpcHostTests.cs](../tests/LYBox.Tests/Rpc/WebViewIpcHostTests.cs)

使用 `FakeTransport`（`tests/LYBox.Tests/Rpc/FakeTransport.cs`）在无 WebView 环境下验证 IPC 运行时。

### 测试覆盖（30 个测试）

| 场景 | 测试数 | 覆盖点 |
|------|--------|--------|
| 握手 `__lybox:ready` | 3 | 未就绪不完成、收到 ready 完成、幂等 |
| 命令分发（C 前缀） | 6 | 已注册回推结果、未注册回推错误、处理器异常、返回 Channel 描述符、非法 JSON 丢弃、无参数 |
| 事件（E 前缀） | 5 | C→JS dispatch、null data、JS→C emit 触发监听、取消订阅、多监听器、单监听器异常隔离 |
| 通道（Channel\<T\>） | 8 | WriteAsync 推送 onData、CloseAsync 推送 onClose、幂等关闭、关闭后丢弃、DisposeAsync、自动 Id 唯一、JS X 前缀关闭、关闭未知通道无副作用 |
| 绑定注入 | 3 | InitializeAsync 幂等、InjectBindingsAsync 清单、空清单 |
| 边界 | 5 | null/空字符串/未知前缀忽略、同名覆盖、无参数分发 |

### 运行测试

```bash
dotnet test tests/LYBox.Tests/LYBox.Tests.csproj --filter "FullyQualifiedName~WebViewIpcHostTests"
```

### 编写自定义测试

```csharp
using LYBox.Plugin.Shared.Rpc;

var transport = new FakeTransport();
var host = new WebViewIpcHost(transport);

host.RegisterCommand("test.echo", (args, ct) =>
    Task.FromResult<object?>(args[0].GetString()));

// 模拟前端调用
transport.SimulateFromScript("C" + JsonSerializer.Serialize(new CallMessage
{
    Name = "test.echo",
    Args = new[] { JsonDocument.Parse("\"hello\"").RootElement.Clone() },
    CallbackId = "cb-1"
}));

// 验证回推
Assert.Contains(transport.ExecutedScripts, s => s.Contains("resolve") && s.Contains("cb-1"));
```

---

## 设计约束与已知限制

### 强制约束（来自 `AGENTS.md`）

| 约束 | 说明 |
|------|------|
| **使用官方包** | `Avalonia.Controls.WebView` 12.0.1（NuGet `avaloniaui` 所有者） |
| **禁止社区库** | 不引入已废弃的 `Avalonia.WebView`（ChisterWu/Jianfenghuaite） |
| **序列化全部走 string** | 双向 `JSON.stringify` 自理，无 binary 通道 |
| **抽象层不提供** | `AddHostObjectToScript`、`SetVirtualHostNameToFolderMapping`、`WebMessageRequested` 不可 cancel。需这些能力只能经 `webView.TryGetPlatformHandle()` 拿平台 COM 指针自行实现（不可移植） |

### 平台支持矩阵

| 平台 | 后端 | `NativeWebView`（嵌入） | `NativeWebDialog`（独立窗口） |
|------|------|-------------------------|------------------------------|
| Windows | WebView2 | ✔ | ✔ |
| macOS | WKWebView | ✔ | ✔ |
| Linux | **WPE WebKit**（v12.0 新增） | ⚠️ 实验性（EGL 支持未完成，issue #14 open） | ✔ |
| iOS/Android | 系统 WebView | ✔ | ✖ |

- **Linux 后端选型**：WPE WebKit。在 WPE 后端成熟前，Linux 上若需稳定 WebView，可降级用 `NativeWebDialog` 独立窗口（WebKitGTK 后端）
- **WebKitGTK 后端不支持嵌入式 `NativeWebView`**，故 Linux 嵌入式场景不使用 WebKitGTK
- macOS/Linux **无离屏渲染**（airspace 问题，issue #3 open）
- Windows WebView2 Runtime 需随安装包分发（Win10 不预装）

### 已知风险

| 风险 | 等级 | 说明 |
|------|------|------|
| Linux WPE 后端实验性 | **高** | EGL 支持未完成（issue #14 open），生产前需 PoC 验证稳定性；不稳定则降级 `NativeWebDialog`（WebKitGTK 独立窗口） |
| 高频推送堆积 | 中 | 需像 Wails 一样做 batch 合并，避免 `InvokeScript` 队列饱和 |
| macOS airspace / 离屏渲染 | 中 | 影响透明叠加、弹层混合 |
| `WebResourceRequested` 不可 cancel | 中 | issue #53 open，拦截 URL 请求能力受限 |

### 后续步骤

1. **Windows PoC**：`InvokeScript` + `WebMessageReceived` 跑通"JS 调 C# 返回 Promise"最小闭环
2. **Linux PoC**：WPE WebKit 嵌入式 `NativeWebView` 稳定性验证；不可用则验证 `NativeWebDialog`（WebKitGTK）降级路径
3. **宿主集成**：在宿主项目添加 `WebViewIpcTransport`（包装 `Avalonia.Controls.WebView`）+ 演示页面
4. **绑定代码生成**：复用 `LYBox.Plugin.Generators` 基础设施，与插件系统统一

---

## 参考

- [AGENTS.md - WebView IPC 调研结论](../AGENTS.md#webview-ipc-调研结论特性分支-feat-avalonia-webview-ipc)
- [AvaloniaUI/Avalonia.Controls.WebView](https://github.com/AvaloniaUI/Avalonia.Controls.WebView)
- [Wails v2 传输模型](https://wails.io/docs/howdoesitwork#the-binding)
- [Tauri Channel](https://tauri.app/v1/guides/features/command/#accessing-raw-ipc)
# 生产宿主安全边界
桌面宿主统一遵循以下安全约束：

- 生产宿主的 RPC 统一入口为 `POST /__bridge/{pluginId}/rpc`，命令处理器按插件隔离，不允许不同插件通过同名命令相互覆盖或调用。`/__emit`、`/__channel/close` 旧端点已合并为 `/__bridge/{pluginId}/{action}`（`action = emit | channel-close`，返回 202）。
- RPC、SSE、emit 与 channel-close 均要求宿主为当前可信 WebView 文档生成的短期 session token。
- session 与 `pluginId` 绑定；页面导航、WebView 卸载或重建后立即撤销，旧页面正在执行的 RPC 也会收到取消信号。
- 请求带有 `Origin` 时，只接受当前 loopback 宿主的 Origin（或 `WebHostService` 构造函数注入的 `AllowedOrigins` 白名单）。
- session 由 `WebPluginView` 在可信导航完成后注入，插件页面不应自行保存、转发或拼接该 token。

## 类型化客户端（推荐）

`invoke()` 只约束单次调用的返回值。需要在编译期同时约束命令名、参数与返回类型时，可在插件前端代码中维护一份 TypeScript 接口 + 包装方法（手写 / JSDoc / 自建生成器均可）。LYBox 不再发布独立 npm 包——所有 SDK 直接来自嵌入资源。

```ts
import { invoke } from '/sdk/lybox-plugin-sdk.js';

interface Methods {
  GreetAsync: (name: string) => Promise<string>;
  AddAsync: (left: number, right: number) => Promise<number>;
}

const client = { invoke: <K extends keyof Methods>(method: K, ...args: Parameters<Methods[K]>) =>
                  invoke<Awaited<ReturnType<Methods[K]>>>(method as string, args[0]) } as { invoke: Methods };

const greeting = await client.invoke.GreetAsync('LYBox');
const total = await client.invoke.AddAsync(2, 3);
```
