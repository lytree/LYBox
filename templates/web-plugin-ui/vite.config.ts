import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

/**
 * LYBox 插件 Vite 配置 —— 双形态适配：
 *
 *  dev (npm run dev)        : 页面由 Vite dev server 提供；src/api/*.ts 经 ./lybox.dev 拿 IDE 提示与 HMR。
 *                             Vite proxy 把 /__bridge /sse /__lybox 转到宿主 HTTP 端点。
 *  prod (npm run build)     : 页面产物 dist/ 拷入插件 wwwroot/。
 *                             src/lybox.ts 重导出层整体替换为 ./lybox.prod（透传宿主 window.LyboxPlugin）。
 *                             dist/*.js 中**不包含** lybox.ts / lybox.dev.ts / lybox.prod.ts 的实现字节；
 *                             运行时由宿主 WebHostService 暴露的 /sdk/lybox-plugin-sdk.js 接管。
 *
 * 端口联动：宿主启动器 --web-vite 时自动探测 LYBOX_WEB_PORT；
 *           本配置读取同一环境变量作为 proxy 目标，保证两端端口一致。
 *
 * @version 2.3.0-preview.7   ← 与 LYBox/version.props 同步（CI 替换）。
 */

const SDK_VERSION = '2.3.0-preview.7';

// 宿主 WebHostService 地址：与启动器侧 LYBOX_WEB_PORT 保持一致（默认 58080）。
// PowerShell:  $env:LYBOX_WEB_PORT='58080'; dotnet run --project src/App/LYBox.Launcher.Desktop
const hostPort = process.env.LYBOX_WEB_PORT ?? '58080';
const hostTarget = `http://127.0.0.1:${hostPort}`;

export default defineConfig(({ mode }) => ({
    // SDK 版本号注入：业务代码可读取 import.meta.env.VITE_LYBOX_SDK_VERSION。
    define: {
        __LYBOX_SDK_VERSION__: JSON.stringify(SDK_VERSION),
    },
    resolve: {
        alias: [
            // 生产构建：把 ./lybox.dev 重写为 ./lybox.prod，
            // Vite tree-shake 后 lybox.dev 的字节完全不会出现在 dist/*.js 中。
            // 注意：仅在 'production' 模式下生效；'development' 保持 dev 实现（HMR + 完整 HTTP 桥）。
            ...(mode === 'production'
                ? [
                    {
                        find: /^\.\/lybox\.dev$/,
                        replacement: fileURLToPath(new URL('./src/lybox.prod.ts', import.meta.url)),
                    },
                    {
                        find: /.*\/src\/lybox\.dev$/,
                        replacement: fileURLToPath(new URL('./src/lybox.prod.ts', import.meta.url)),
                    },
                ]
                : []),
        ],
    },
    build: {
        outDir: 'dist',
        emptyOutDir: true,
        // 生产构建：把所有非宿主 SDK 字节最小化；外部 SDK 由宿主 /sdk/ 路径提供。
        minify: 'esbuild',
        sourcemap: true,
    },
    server: {
        port: 5173,
        // 同源代理：浏览器视角所有请求都在 localhost:5173，绕开跨域（宿主无 CORS 响应头）。
        // WebSocket 代理不需要——宿主事件流走 SSE（HTTP 流）。
        proxy: {
            // RPC 桥接：POST /__bridge/{pluginId}/rpc（X-LYBox-Session 头）
            '/__bridge': { target: hostTarget, changeOrigin: false },
            // SSE 事件流：GET /sse/{pluginId}?session=（query token，EventSource 无法带自定义头）
            '/sse': { target: hostTarget, changeOrigin: false },
            // 调试会话签发（仅 Debug 构建宿主）：POST /__lybox/debug/session/{pluginId}
            //  + SDK 资源（/sdk/lybox-plugin-sdk.js 与 .d.ts），让 IDE 与浏览器共用宿主资源
            '/__lybox': { target: hostTarget, changeOrigin: false },
            '/sdk': { target: hostTarget, changeOrigin: false },
        },
    },
}));