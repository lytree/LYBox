/**
 * LYBox 适配层 dev 实现 —— Vite 开发期专用。
 *
 * @version 2.3.0-preview.7   ← 与 LYBox/version.props 的 <LyboxVersion> 保持一致（CI 同步）。
 *
 * 形态：
 *   - Vite dev（5173）：浏览器视角所有请求都来自 5173；经 vite.config.ts 的同源代理
 *     访问宿主 HTTP 端点（/__bridge /sse /__lybox）。本文件实现完整的 HTTP 桥接 + SSE 逻辑。
 *   - 浏览器 mock：若宿主不可达，自动回退 window.LyboxPlugin（lybox-mock 注入）。
 *   - WebView 生产：本文件**不应被加载**；生产构建由 Vite alias 整体替换为 ./lybox.prod.ts。
 *
 * 共享契约：本文件所有方法签名必须与 LYBox.Plugin.Shared.Web/Assets/lybox-plugin-sdk.js
 * （以及 .d.ts）一一对应；任一端升级必须同步修改。
 *
 * SDK 版本号注释：本文件头部 @version 与 LYBox/version.props 的 <LyboxVersion> 同步；
 * CI 通过 build.cs 的 ReplaceSdkVersionToken 在打包前同步所有副本。
 *
 * 双形态适配：
 *   1. WebView 形态（生产）：页面由宿主静态服务托管，ipc.js 注入 window.__lybox，
 *      调用走原生 WebView IPC（无 HTTP 开销，宿主自动管理会话）。
 *   2. Vite 形态（开发）：页面跑在 Vite dev server（localhost:5173，完整 HMR），
 *      经 vite.config.ts 的同源代理访问宿主 HTTP 端点：
 *        - RPC：  POST /__bridge/{pluginId}/rpc（X-LYBox-Session 请求头）
 *        - 事件：  GET  /sse/{pluginId}?session=（query token；EventSource 无法带自定义头）
 *        - 会话：  POST /__lybox/debug/session/{pluginId}（仅 Debug 构建宿主，外部浏览器自助签发）
 */

// 注：本文件的 @version 头注释由 CI 同步 LYBox/version.props 的 <LyboxVersion>。
// 当前固定为 2.3.0-preview.7，发布升级时全局替换。
export const SDK_VERSION = '2.3.0-preview.7';

/** 当前插件的 ID（与插件程序集 PluginId / Web 描述符一致）。由业务侧覆盖。 */
export const PLUGIN_ID = 'demo-plugin';

const TOKEN_KEY = `lybox-dev-session:${PLUGIN_ID}`;

// —— 类型契约 —— 与 /sdk/lybox-plugin-sdk.d.ts 一一对应 ——

export interface RpcInvokeOptions {
    signal?: AbortSignal;
    timeout?: number;
}

export interface LyboxBridge {
    invoke(method: string, payload?: unknown, options?: RpcInvokeOptions): Promise<unknown>;
    invokeLegacy(method: string, ...args: unknown[]): Promise<unknown>;
    rpc(method: string, ...args: unknown[]): Promise<unknown>;
    on(event: string, cb: (data: unknown) => void): () => void;
    off(event: string, cb?: (data: unknown) => void): void;
    emit(event: string, data?: unknown): void;
    isWebView(): boolean;
    configureRuntime?(pluginId: string, sessionToken?: string): void;
    startSse?(pluginId: string, sessionToken?: string): void;
}

export interface LyboxRuntimeConfig {
    runtimeVersion: '1';
    pluginKey: string;
    pageId: string;
    mockBaseUrl?: string;
    sseUrl?: string;
    apiBaseUrl?: string;
}

export interface LyboxWindow extends Window {
    __lybox?: LyboxBridge;
    __lyboxRuntime?: LyboxRuntimeConfig;
    LyboxPlugin?: LyboxPublicApi;
}

export interface LyboxPublicApi {
    invoke: <TReq, TRes>(method: string, payload: TReq, options?: RpcInvokeOptions) => Promise<TRes>;
    invokeLegacy: (method: string, ...args: unknown[]) => Promise<unknown>;
    on: (event: string, cb: (data: unknown) => void) => () => void;
    off: (event: string, cb?: (data: unknown) => void) => void;
    request: (path: string, options?: RequestInit) => Promise<Response>;
    getJson: <T = unknown>(path: string, options?: RequestInit) => Promise<T>;
}

declare global {
    interface Window {
        __lybox?: LyboxBridge;
        __lyboxRuntime?: LyboxRuntimeConfig;
        LyboxPlugin?: LyboxPublicApi;
    }
}

// —— 浏览器全局访问：window.LyboxPlugin（宿主 SDK）/ window.__lybox（WebView 注入）——

function getRuntime(): LyboxWindow['LyboxPlugin'] | LyboxBridge | undefined {
    if (typeof window === 'undefined') return undefined;
    return window.LyboxPlugin ?? window.__lybox;
}

/** 当前是否运行在宿主 WebView 内（window.__lybox 存在）。 */
export function isWebView(): boolean {
    return typeof window !== 'undefined' && typeof window.__lybox?.rpc === 'function';
}

/** 是否暴露原生 WebView IPC 桥（兼容别名）。 */
export function isLyboxBridgeAvailable(): boolean {
    return typeof window !== 'undefined' && typeof window.__lybox?.rpc === 'function';
}

/** 获取 Vite 形态的调试会话 token（WebView 形态返回空——宿主自动管理）。缓存于 sessionStorage。 */
async function getDevSession(): Promise<string> {
    if (isWebView()) return '';

    let token = sessionStorage.getItem(TOKEN_KEY);
    if (token) return token;

    const resp = await fetch(`/__lybox/debug/session/${encodeURIComponent(PLUGIN_ID)}`, {
        method: 'POST',
    });
    if (!resp.ok) {
        throw new Error(
            `调试会话签发失败（HTTP ${resp.status}）。请确认：宿主以 Debug 构建运行，` +
                `插件 '${PLUGIN_ID}' 已注册，且 LYBOX_WEB_PORT 与 vite.config.ts 代理端口一致。`,
        );
    }
    token = (await resp.json()).session as string;
    sessionStorage.setItem(TOKEN_KEY, token);
    return token;
}

/** 调用宿主 RPC 命令。WebView 形态走原生 bridge；Vite 形态走 HTTP 桥接。 */
export async function invoke<T = unknown>(
    method: string,
    payload?: unknown,
    options?: RpcInvokeOptions,
): Promise<T> {
    const runtime = getRuntime();
    if (runtime && typeof runtime.invoke === 'function') {
        return (await runtime.invoke(method, payload, options)) as T;
    }

    const token = await getDevSession();
    const resp = await fetch(`/__bridge/${encodeURIComponent(PLUGIN_ID)}/rpc`, {
        method: 'POST',
        headers: { 'content-type': 'application/json', 'X-LYBox-Session': token },
        body: JSON.stringify({
            version: 2,
            kind: 'plugin-rpc-call',
            pluginId: PLUGIN_ID,
            method,
            payload: payload ?? null,
        }),
        ...(options?.signal ? { signal: options.signal } : {}),
    });
    const envelope = (await resp.json()) as { payload?: T; error?: unknown };
    if (!resp.ok || envelope.error !== undefined) {
        throw new Error(`RPC '${method}' 失败: ${JSON.stringify(envelope.error ?? resp.status)}`);
    }
    if (options?.timeout) {
        // 简易 timeout 由调用方借助 Promise.race 自行处理；此处仅占位提示
    }
    return envelope.payload as T;
}

/** 兼容旧版多参调用（按位置参数序列化）。 */
export async function invokeLegacy<T = unknown>(method: string, ...args: unknown[]): Promise<T> {
    const runtime = getRuntime();
    if (runtime && typeof runtime.invokeLegacy === 'function') {
        return (await runtime.invokeLegacy(method, ...args)) as T;
    }

    const token = await getDevSession();
    const resp = await fetch(`/__bridge/${encodeURIComponent(PLUGIN_ID)}/rpc`, {
        method: 'POST',
        headers: { 'content-type': 'application/json', 'X-LYBox-Session': token },
        body: JSON.stringify({ name: method, args }),
    });
    const envelope = (await resp.json()) as { result?: T; error?: unknown };
    if (!resp.ok || envelope.error !== undefined) {
        throw new Error(`RPC '${method}' 失败: ${JSON.stringify(envelope.error ?? resp.status)}`);
    }
    return envelope.result as T;
}

/** 创建一个最简 transport，可直接喂给生成器的 createXxxClient。 */
export function createLyboxClient(): {
    invoke: <TReq, TRes>(method: string, payload: TReq, options?: RpcInvokeOptions) => Promise<TRes>;
    invokeLegacy: (method: string, ...args: unknown[]) => Promise<unknown>;
} {
    return {
        invoke: <TReq, TRes>(method: string, payload: TReq, options?: RpcInvokeOptions) =>
            invoke<TRes>(method, payload, options),
        invokeLegacy: (method: string, ...args: unknown[]) => invokeLegacy(method, ...args),
    };
}

/** 订阅宿主事件。返回取消订阅函数。 */
export function on<T = unknown>(event: string, cb: (data: T) => void): () => void {
    const handler = cb as (data: unknown) => void;
    if (isWebView()) {
        return window.__lybox!.on(event, handler);
    }

    // Vite 形态：惰性建立单条 SSE 连接，按事件名分发
    const listeners = getEmitter();
    listeners.set(event, [...(listeners.get(event) ?? []), handler]);
    void connectSse();
    return () => {
        const set = listeners.get(event);
        if (!set) return;
        const i = set.indexOf(handler);
        if (i >= 0) set.splice(i, 1);
        if (set.length === 0) listeners.delete(event);
    };
}

/** 取消事件订阅（on 返回值的别名）。 */
export function off(event: string, cb?: (data: unknown) => void): void {
    if (isWebView()) {
        window.__lybox?.off(event, cb);
        return;
    }
    const set = emitterListeners.get(event);
    if (!set) return;
    if (cb) {
        const i = set.indexOf(cb);
        if (i >= 0) set.splice(i, 1);
    } else {
        set.length = 0;
    }
    if (set.length === 0) emitterListeners.delete(event);
}

/** 发送 HTTP 请求到宿主 apiBaseUrl（Vite dev 形态一般无此端点，保留兼容）。 */
export async function request(path: string, options?: RequestInit): Promise<Response> {
    const config = getLyboxRuntime();
    if (!config?.apiBaseUrl) {
        throw new Error('LYBox plugin API base URL is unavailable.');
    }
    return fetch(new URL(path, config.apiBaseUrl), options ?? {});
}

/** HTTP GET + JSON 解析便捷方法。 */
export async function getJson<T = unknown>(path: string, options?: RequestInit): Promise<T> {
    const response = await request(path, options);
    if (!response.ok) {
        throw new Error(`API request failed with status ${response.status}.`);
    }
    return (await response.json()) as T;
}

/** 安装 LYBox 运行时配置（浏览器 mock 模式由 lybox-mock 调用）。 */
export function installLyboxRuntime(config: LyboxRuntimeConfig): LyboxRuntimeConfig {
    if (typeof window !== 'undefined') {
        window.__lyboxRuntime = config;
    }
    return config;
}

/** 获取当前运行时配置（未安装返回 undefined）。 */
export function getLyboxRuntime(): LyboxRuntimeConfig | undefined {
    return typeof window !== 'undefined' ? window.__lyboxRuntime : undefined;
}

/** 注册一组本地 mock 处理（仅浏览器模式、mockBaseUrl 不可达时生效）。 */
export function registerLyboxMocks(_mocks: Record<string, (...args: unknown[]) => unknown>): void {
    // 本模板 dev 形态不实现本地 mock 兜底：宿主不可达应直接报错暴露问题，
    // 由 lybox-mock CLI 工具提供完整的 mock 服务（独立进程）。
}

// —— SSE 单连接管理（Vite 形态）——

const emitterListeners = new Map<string, Array<(data: unknown) => void>>();
let ssePromise: Promise<EventSource> | undefined;

function getEmitter() {
    return emitterListeners;
}

async function connectSse(): Promise<EventSource> {
    if (ssePromise) return ssePromise;
    ssePromise = (async () => {
        const token = await getDevSession();
        const es = new EventSource(
            `/sse/${encodeURIComponent(PLUGIN_ID)}?session=${encodeURIComponent(token)}`,
        );
        // 宿主统一以 dispatch 信封推送：{"name":"<事件名>","data":<负载>}
        es.addEventListener('dispatch', (e) => {
            const msg = JSON.parse((e as MessageEvent).data) as { name: string; data: unknown };
            for (const cb of [...(emitterListeners.get(msg.name) ?? [])]) cb(msg.data);
        });
        return es;
    })();
    return ssePromise;
}