# LYBox Web 插件前端模板（Vite）

一套代码，两种运行形态：

| 形态 | 页面来源 | IPC 通道 | 适用 |
| ---- | -------- | -------- | ---- |
| **Vite 开发** | Vite dev server（完整 HMR） | HTTP 同源代理 → 宿主 `/__bridge` / `/sse` | 日常 UI 功能开发 |
| **WebView 生产** | 插件 `wwwroot/`（宿主静态服务） | 原生 `window.__lybox` bridge + 宿主 `/sdk/lybox-plugin-sdk.js` | 发布形态 / 真实行为联调 |

业务代码统一 `import { invoke, on, ... } from './lybox'`，由 Vite alias 在 dev/prod 间自动切换：

- dev: `./lybox.dev.ts` —— 完整 HTTP 桥接 + SSE 实现，IDE 提示直接来自本文件
- prod: `./lybox.prod.ts` —— 透传宿主 `window.LyboxPlugin`（`dist/*.js` 不含 SDK 字节）

宿主运行时 SDK：`/sdk/lybox-plugin-sdk.js` 与 `/sdk/lybox-plugin-sdk.d.ts` 来自 `LYBox.Plugin.Shared.Web` 嵌入资源（`LYBox/version.props` 的 `<LyboxVersion>`）。

@version 2.3.0-preview.7

## 使用步骤

1. 复制本目录为插件 UI 工程，修改 `src/lybox.ts` 中的 `PLUGIN_ID`。
2. 启动宿主（Debug 构建，固定端口）：

   ```powershell
   # PowerShell —— 端口需与 vite.config.ts 的 LYBOX_WEB_PORT（默认 58080）一致
   $env:LYBOX_WEB_PORT='58080'
   dotnet run --project src/App/LYBox.Launcher.Desktop
   ```

3. 启动 Vite：

   ```powershell
   npm install
   npm run dev     # http://localhost:5173
   ```

4. 浏览器打开 `http://localhost:5173` 开发。首次 RPC/SSE 会自动经
   `POST /__lybox/debug/session/{pluginId}` 签发调试会话（仅 Debug 构建宿主提供）。

## IDE 提示来源

| 提示内容 | 来源 |
|---------|------|
| `invoke` / `on` / `isWebView` / `transport` 签名 | `src/lybox.dev.ts`（`tsconfig.json` paths 把 `/sdk/lybox-plugin-sdk` 指向本地文件） |
| `[RpcCommand]` 生成的 `{Class}Client` 强类型客户端 | `webroot/.lybox/{Class}.client.d.ts`（源生成器产出） |
| 宿主运行时 SDK（`window.LyboxPlugin`）契约 | 宿主 `/sdk/lybox-plugin-sdk.d.ts`（嵌入式资源，Vite dev 经同源代理转发） |

升级 SDK 时：
- 同步修改 `LYBox.Plugin.Shared.Web/Assets/lybox-plugin-sdk.{js,d.ts}` 与本模板 `src/lybox.dev.ts` / `src/lybox.prod.ts` 的方法签名；
- 同步更新文件头 `@version` 注释（CI 替换为 `version.props` 的 `<LyboxVersion>`）。

## 发布

```powershell
npm run build     # 产物 dist/
# 将 dist/ 内容拷贝到插件的 wwwroot/（csproj 中随插件打包）
# 产物在 dist/*.js 中**不含** lybox.ts / lybox.dev.ts 的实现字节；
# 运行时由宿主嵌入式 SDK（/sdk/lybox-plugin-sdk.js）接替。
```

产物在 WebView 内运行时，适配层自动切换到宿主原生 bridge，无需修改任何代码。

## 切换 React / Vue

本模板为零依赖 vanilla TS。切换框架时安装对应 Vite 插件即可，IPC 适配层不受影响：

```powershell
npm install react react-dom @vitejs/plugin-react   # 然后 vite.config.ts 添加 plugins: [react()]
npm install vue                                      # 或 vue + @vitejs/plugin-vue
```

## 约束（当前宿主能力边界）

- 宿主 HTTP 端点无 CORS 响应头：**必须**经 Vite `server.proxy` 同源代理访问，不可直连宿主地址。
- 调试会话签发端点仅 Debug 构建宿主提供；联调请使用 Debug 构建。
- 事件推送为 SSE（HTTP 流），无 WebSocket；连接由适配层单例管理。
- 生产构建时 `dist/*.js` 不应包含 `lybox.ts` 实现字节；如出现，请检查 `vite.config.ts` 的 alias 是否生效。