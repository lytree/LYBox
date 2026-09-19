/**
 * LYBox 插件 UI 入口 —— 调用宿主 SDK 完成 RPC 与事件订阅。
 *
 * @version 2.3.0-preview.7   ← 与 LYBox/version.props 的 <LyboxVersion> 同步（CI 替换）。
 *
 * 形态 1（Vite dev）：import 自 src/lybox.dev.ts（HTTP 桥接 + SSE，完整 HMR）。
 * 形态 2（WebView prod）：import 自 src/lybox.prod.ts（透传宿主 window.LyboxPlugin）。
 *
 * 业务代码统一 import 自 './lybox'，由 vite alias 在 dev/prod 间自动切换。
 */

import { invoke, on, isWebView, PLUGIN_ID, SDK_VERSION } from './lybox';

declare const __LYBOX_SDK_VERSION__: string;

// —— 环境状态展示：直观确认当前走 WebView bridge 还是 Vite HTTP 桥接 ——
const env = document.getElementById('env')!;
env.textContent = `运行时 SDK 版本 ${__LYBOX_SDK_VERSION__}（${SDK_VERSION}），宿主插件=${PLUGIN_ID}，环境=${
    isWebView() ? 'WebView' : 'Vite/浏览器'
}`;

// —— RPC 示例：调用宿主注册的命令（把 GreetAsync 换成你的插件命令）——
document.getElementById('greet')!.addEventListener('click', async () => {
    const result = document.getElementById('greet-result')!;
    try {
        const greeting = await invoke<string>('GreetAsync', { Name: 'world' });
        result.textContent = `✓ ${greeting}`;
    } catch (err) {
        result.textContent = `✗ ${err instanceof Error ? err.message : String(err)}`;
    }
});

// —— 事件订阅示例：宿主 SseEventPusher / WebViewIpcHost 推送的事件 ——
const eventsList = document.getElementById('events')!;
on<{ message: string }>('DemoEvent', (data) => {
    const li = document.createElement('li');
    li.textContent = `${new Date().toLocaleTimeString()} DemoEvent: ${JSON.stringify(data)}`;
    eventsList.appendChild(li);
});