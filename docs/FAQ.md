# 常见问题

按场景分组：插件加载、UI 注册、设置、WebView IPC、热加载、调试。

---

## 插件加载

### 1. 插件显示 Error 状态怎么办？

按顺序排查：

1. **plugin.json 缺失或字段缺失** —— 检查构建产物 `artifacts/publish/plugins/{Name}/publish/plugin.json`
2. **DLL 依赖未解析** —— 查看启动日志的 `LoadException`，多为 `FileNotFoundException` 或 `TypeLoadException`
3. **`MinPluginSdkVersion` 不兼容** —— 当前宿主版本 `2.3.0-preview.3` 要求插件 ≥ `2.3.0-preview.3`，且 Major 必须一致
4. **共享程序集清单缺失** —— 插件用了非默认共享的框架程序集，需 `shared-assemblies.txt` 显式声明
5. **入口类异常** —— `[GenerateMetadata]` 的 partial 类未实现 `IPluginMetadata` 属性或抛异常

### 2. SDK 版本解析失败如何修复？

`MinPluginSdkVersion` 必须是三段 SemVer（如 `2.3.0-preview.3`）。宿主在安装期与加载期都校验：

| 校验项 | 行为 |
|---|---|
| Major 不一致 | fail-closed，拒绝加载 |
| Minor / Build 高于宿主能力 | fail-closed，拒绝加载 |
| 解析失败（含空字符串） | fail-closed |
| 未声明 | 视为 `0.0.0`，无约束 |

详见 [Plugin-SDK-Versioning.md](Plugin-SDK-Versioning.md)。

### 3. 如何解决 `The plugin manifest is missing or invalid`？

- 确认 csproj 至少有 `PluginId`、`PluginName`、`PluginAuthor`、`PluginDescription` 四个属性
- 清理 `artifacts/obj/{Plugin}/` 后重新构建（`GeneratePluginManifest` target 在 obj 下生成 plugin.json）
- 检查 `GeneratePluginManifest` 是否被任何 BeforeTargets / AfterTargets 干扰

---

## UI 注册

### 4. 菜单或导航不显示？

逐项核对：

- ViewModel 上是否标注 `[ViewMap(typeof(Page))]`、`[NavigationItem("key")]`、`[Menu(...)]`
- `[GenerateMetadata]` 是否在插件入口类上 —— 没有它就没有 `IGeneratedPluginModule.Ui`
- 插件状态是否为可加载（`Loaded` / `Installed`）
- `RegisterAsync` 是否抛异常（被 `PluginLoader` 捕获后插件会被标记 `Error`）

### 5. 菜单父子层级如何构建？

通过 `[Menu]` 的 `ParentKey` 字段：

```csharp
[Menu("NAVI_Plugin", "My Plugin", ParentKey = null)]
[Menu("NAVI_Plugin_Page", "My Page", ParentKey = "NAVI_Plugin")]
public partial class MyPageViewModel : ViewModelBase { }
```

`ParentKey = null` 表示顶级。宿主 `MenuItemTreeBuilder.BuildTree()` 自动构建树；缺失的 ParentKey 会创建虚拟分组节点。

### 6. ViewLocator 自动解析失败？

约定映射：

| ViewModel | View |
|---|---|
| `FooViewModel` | `FooView` |
| `FooPageViewModel` | `FooPage` |

- 检查 ViewModel 类名后缀（`ViewModel` → 去 `Model`）
- View 必须是 `UserControl` 或 Avalonia `Window` 派生
- 缓存通过 `ConditionalWeakTable` 实现，VM 与 View 之间循环引用不会泄漏

### 7. View 与 ViewModel 如何建立 CompiledBinding？

`AvaloniaUseCompiledBindingsByDefault=true` 已全局开启。`Binding` 必须有 `x:DataType`：

```xml
<UserControl xmlns:vm="clr-namespace:MyPlugin.ViewModels"
             x:DataType="vm:MyPageViewModel">
    <TextBlock Text="{Binding Message}" />
</UserControl>
```

---

## 设置

### 8. 设置项不显示？

`ISettingsService` 在 `RegisterAsync` 中调用：

```csharp
settingsService.RegisterSettings(
[
    SettingDefinition.Path("MY.Key", "Label", "Hint", "Group", order, subOrder, defaultValue, PluginId),
    // ...
]);
```

- `PluginId` 必须与 csproj 一致
- Group / Order 决定渲染顺序与分组
- 定义持久化到 `%LOCALAPPDATA%/LYBox/appdata.db`（SQLite）

### 9. 如何在插件读取设置值？

```csharp
var value = ServiceLocator.GetService<ISettingsService>()?.GetValue("MY.Key");
```

或在 VM 构造函数中通过 DI 注入。

---

## 热加载

### 10. 是否支持热加载 / 热卸载？

**不支持常规热加载 / 热卸载。** 全部插件在应用启动时一次性加载，状态变更（安装 / 卸载 / 升级 / 启用 / 禁用）均需重启生效。

UI 中触发安装 / 卸载 / 启用 / 禁用时务必提示用户重启。

### 11. 既然 `AssemblyLoadContext` 是 `isCollectible=true`，为什么运行时不卸载？

`PluginLoadContext` 标记为可收集，但运行时不调用 `Unload()`。原因：

- 插件仍可能被 ViewLocator / NavigationService / MenuConfigurationService 等静态字典持有
- 长生命周期 ServiceLocator 引用可能跨插件
- 原生资源（TDLib、COM 互操作、HttpClient 等）的释放时机不可控

强制卸载会引发难以诊断的 `AccessViolation` 或 `MissingMethodException`。插件应在 `ShutdownAsync` 释放自身持有的资源。

---

## Web 插件

### 12. Web 插件调用失败？

按顺序排查：

1. csproj 是否声明 `<PluginKind>Web</PluginKind>` + `<PluginWwwroot>wwwroot</PluginWwwroot>` + `<PluginEntryPage>index.html</PluginEntryPage>`
2. `wwwroot/index.html` 是否存在
3. 是否引用 `LYBox.Plugin.Shared.Web` NuGet 包
4. 前端是否引用 `/sdk/lybox-plugin-sdk.js`（不要硬编码端口）
5. 浏览器 DevTools 控制台是否有 `invokeCSharpAction` 错误或 origin 拒绝
6. 调试面板 `/{BaseUrl}/__lybox/debug` 查看 session token 与事件流

### 13. 嵌入式 SDK 来自哪里？

`lybox-plugin-sdk.js` 与 `lybox-plugin-theme.css` 是 `LYBox.Plugin.Shared.Web` 的嵌入资源（`PluginWebSdkResources`），由宿主 `WebHostService` 在 `/sdk/` 路径提供。**前端无需任何 npm / pnpm / 构建工具**。

### 14. Linux 上 WebView 不工作？

Linux 后端是 **WPE WebKit**，目前为实验性（EGL 支持未完成，issue #14）。生产前需在 Windows 完成 PoC，Linux 验证 WPE 稳定性；不稳定时降级到 `NativeWebDialog`（独立窗口）使用 WebKitGTK 后端。

### 15. JS 调 C# 不返回 Promise 怎么办？

Avalonia WebView 的 `invokeCSharpAction` 是 fire-and-forget。要实现 Promise 模型：

1. JS 端发 `C` 前缀信封 + CallbackId
2. C# 侧 `WebViewIpcHost.HandleCallAsync` 处理
3. C# 侧 `webView.InvokeScript($"window.__lybox.resolve('{callbackId}', null, JSON.stringify(result))")` 回推

参考 [WebView-IPC-Guide.md](WebView-IPC-Guide.md) §完整示例。

---

## 通信

### 16. 插件间如何通信？

SDK 没有独立的插件间消息总线。可行做法：

- **共享服务**：通过 DI 注册共享服务（如 `IConfigurationService`）
- **共享配置**：使用 `ISettingsService`（按 PluginId 隔离）
- **WeakReferenceMessenger**：宿主提供，插件可发布 / 订阅自定义消息

不要依赖未经契约定义的全局消息通道。

### 17. 第三方 NuGet 包如何使用？

在插件 csproj 添加 `PackageReference`：

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

默认第三方程序集在插件 ALC 中**私有加载**，与其他插件隔离。仅当必须与宿主共享类型时才加入 `shared-assemblies.txt`，并评估版本契约影响。

---

## 调试

### 18. 如何调试插件 C# 代码？

VS Code 启动配置位于 `LYBox.Plugins/.vscode/launch.json`：

```jsonc
{
  "name": "Debug Plugin - {Name}",
  "type": "coreclr",
  "request": "launch",
  "preLaunchTask": "build-plugin: {Name}",
  "program": "${workspaceFolder}/../LYBox/artifacts/bin/LYBox.Launcher.Desktop/Debug/LYBox.Launcher.Desktop.dll",
  "env": {
    "AVALONIA_EXTRA_PLUGINS_PATH": "${workspaceFolder}/artifacts/bin/{Name}/Debug"
  }
}
```

注意：调试插件**不会**自动构建宿主，需手动先 `dotnet build LYBox.Launcher.Desktop` 一次。

### 19. 源生成器不生效？

- 清理 `artifacts/obj/{Plugin}/` 后重新构建
- 检查 `LYBox.Plugin.Generators` 是否以 `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` 引用
- 查看生成产物 `obj/Debug/net10.0/{Plugin}.Module.g.cs` 是否存在
- IDE 看不到 `[RpcCommand]` 生成的 `.d.ts` 时，至少 `dotnet build` 一次

### 20. Web 插件 Vite 开发模式如何启动？

宿主自动托管 Vite dev server：

```powershell
dotnet run --project src/App/LYBox.Launcher.Desktop -- --web-vite
```

浏览器打开 `http://localhost:5173` 即可享受 HMR + 同源代理联调宿主。

### 21. 如何查看 Web 插件 IPC 调试信息？

仅 Debug 构建：`/{BaseUrl}/__lybox/debug` —— 调试面板（RPC 命令清单 + SSE 事件流 + session 签发）。Release 构建需启用 `--web-dev`。

---

## 其他

### 22. 为什么可收集 ALC 不代表可以随时卸载？

见问题 §11。简言之：**静态/长生命周期字典持有 + 原生资源生命周期**导致强制卸载不安全。`ShutdownAsync` + `ServiceProvider.Dispose()` 是当前唯一的释放路径。

### 23. 插件数据应该放哪里？

**必须**通过 `IPluginDataDirectoryProvider` 解析到 `%LOCALAPPDATA%/LYBox/{PluginId}/` 下。**禁止**回退到：

- `AppContext.BaseDirectory`
- `Environment.SpecialFolder.UserProfile`
- `Environment.SpecialFolder.ApplicationData`
- 插件安装目录（`plugins/{PluginId}/`，可能被覆盖安装清空）

早期版本遗留数据可在 `RegisterAsync` 早期一次性 `File.Copy` 迁移到新位置。

### 24. 覆盖安装失败 / DLL 被锁？

运行中的插件 DLL 被 ALC 持有，文件被锁。覆盖安装会被 `PluginInstallationManager` 拒绝。解决方案：

1. 关闭宿主应用
2. 重新执行安装

详见 [Plugin-Upgrade-Evaluation.md](Plugin-Upgrade-Evaluation.md)。
