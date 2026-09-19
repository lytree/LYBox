/**
 * LYBox 插件前端 IPC 适配层 —— 一套代码，两种运行形态：
 *
 *  @version 2.3.0-preview.7   ← 与 LYBox/version.props 的 <LyboxVersion> 保持一致（CI 同步）。
 *
 * 形态 1（Vite dev）：本文件作为业务代码唯一入口；import 由 IDE 解析到 ./lybox.dev.ts（IDE 提示）。
 *                      Vite dev server 经同源代理把 RPC/SSE 转到宿主 HTTP 端点。
 * 形态 2（WebView prod）：npm run build 产物 dist/*.js 中**不含本文件任何字节**。
 *                        业务侧 import 由 Vite alias 替换为指向宿主 /sdk/lybox-plugin-sdk.js；
 *                        IDE 提示来自 /sdk/lybox-plugin-sdk.d.ts（Vite dev 与生产均经宿主端点）。
 *
 * 本文件 = IDE 提示桥 + 运行时分发层：
 *   - export 类型：直接重导出 SDK 契约（保证业务侧看到的 API 与生产 SDK 完全一致）。
 *   - 运行时实现：./lybox.dev.ts（Vite dev）/ window.LyboxPlugin（生产 WebView + 浏览器 mock）。
 *
 * SDK 版本号自动从 ./lybox.dev.ts 顶部 @version 解析；CI 同步 version.props 时同时改本文件头注释。
 *
 * 使用：
 *   1) 修改下方 PLUGIN_ID
 *   2) pnpm dev → Vite 开发（宿主：LYBOX_WEB_PORT 与 vite.config 保持一致）
 *   3) pnpm build → dist/ 拷入插件 wwwroot/，运行时由宿主嵌入式 SDK 接替本文件
 */

export const SDK_VERSION = '2.3.0-preview.7';

/** 当前插件的 ID（与插件程序集 PluginId / Web 描述符一致）。由业务侧覆盖。 */
export const PLUGIN_ID = 'demo-plugin';

// —— 类型契约：与 /sdk/lybox-plugin-sdk.d.ts 一一对应 ——
export type { RpcInvokeOptions, LyboxBridge, LyboxWindow, LyboxRuntimeConfig, LyboxPublicApi }
    from './lybox.dev';

// —— 运行时实现：Vite dev 直接走 ./lybox.dev；生产构建时由 Vite alias 整体替换为 window.LyboxPlugin ——
export {
    invoke,
    invokeLegacy,
    createLyboxClient,
    isWebView,
    isLyboxBridgeAvailable,
    on,
    off,
    request,
    getJson,
    installLyboxRuntime,
    getLyboxRuntime,
    registerLyboxMocks,
} from './lybox.dev';