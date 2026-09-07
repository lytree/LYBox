# Web 插件指南

当前 Web 插件不依赖前端 monorepo、pnpm、Vite 或 Vue。插件是普通 `net10.0` 类库，静态页面放在 `wwwroot`，宿主统一注册资源根目录。

## csproj 声明

```xml
<PropertyGroup>
  <PluginKind>Web</PluginKind>
  <PluginWwwroot>wwwroot</PluginWwwroot>
  <PluginEntryPage>index.html</PluginEntryPage>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="LYBox.Plugin.Shared.Web" Version="$(PluginSdkVersion)" PrivateAssets="all" />
</ItemGroup>
```

目录：

```text
wwwroot/index.html
wwwroot/.lybox/mock.json  # 可选开发模拟数据
```

发布后清单中的 `kind`、`web.wwwroot`、`web.entryPage` 来自 csproj。插件自身不调用 `MapPluginRoot`。

## 页面承载

在 Avalonia ViewModel 上使用 `[ViewMap(typeof(WebPluginView))]`，由 `WebPluginView` 加载入口页。可设置 `ShowDevelopmentToolbar` 查看返回、前进、刷新和路由状态。宿主只在存在已注册 Web 插件时启动本机 Kestrel。

## RPC

C# 方法标记 `[RpcCommand]`，生成器会产生绑定和 JS/TS 胶水。前端使用：

```html
<script type="module">
  import { invoke, on } from "/sdk/lybox-plugin-sdk.js";
  const result = await invoke("AddAsync", { Left: 3, Right: 5 });
  const off = on("tick", value => console.log(value));
</script>
```

宿主提供 `OpenFilePicker`、`SaveFilePicker`、`OpenFolderPicker`、`ShowMessageBox`、`ShowConfirmDialog`。高频或持续数据使用事件/SSE 或 `Channel<T>`，不要把长任务阻塞在一次 RPC 中。

## 调试与安全

WebHost 路由按 pluginId 隔离：`/{pluginId}/{**path}`、`/sse/{pluginId}`、`/__bridge/{pluginId}/{action}`、`/__lybox/debug`。每个 WebView 会话带 `X-LYBox-Session`，宿主检查 Origin、pluginId、授权路径和目录边界。仅允许本机回环来源；禁止硬编码端口和使用 `..` 路径。

## 构建

```powershell
.\build.ps1 --build=bin
.\build.ps1 --build=plugin --plugin=LYBox.Plugin.WebTemplate
```

插件发布目录应包含 DLL、`plugin.json`、`wwwroot/index.html` 及静态资源。生成 zip 后安装到宿主并重启验证。
