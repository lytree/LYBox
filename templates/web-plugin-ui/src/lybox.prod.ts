/**
 * LYBox 适配层 prod 实现 —— 生产构建专用。
 *
 * @version 2.3.0-preview.7   ← 与 LYBox/version.props 的 <LyboxVersion> 保持一致（CI 同步）。
 *
 * 仅在 `vite build` 期间被引用（vite.config.ts 的 alias：./lybox.dev → ./lybox.prod）。
 * 本文件**不应**在 Vite dev 时被加载；本地 tsc / IDE 仅作类型检查用途。
 *
 * 所有运行时方法委托给宿主嵌入式 SDK（window.LyboxPlugin，由 /sdk/lybox-plugin-sdk.js 挂载）。
 * 本文件仅做：
 *   1. 等待 SDK 就绪（首次访问时挂载，挂载前访问会抛错）
 *   2. 透传方法调用
 *   3. 暴露与 .dev.ts 完全一致的签名（保证 IDE 提示 / 编译期类型一致）
 *
 * 字节量：本文件 + lybox.ts（重导出层）在 Vite tree-shake 后几乎为零；
 *        所有 SDK 字节来自宿主 /sdk/lybox-plugin-sdk.js（运行时解析）。
 */

export const SDK_VERSION = '2.3.0-preview.7';

export const PLUGIN_ID = 'demo-plugin';

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
    installLyboxRuntime?: (config: LyboxRuntimeConfig) => LyboxRuntimeConfig;
    getLyboxRuntime?: () => LyboxRuntimeConfig | undefined;
    registerLyboxMocks?: (mocks: Record<string, (...args: unknown[]) => unknown>) => void;
}

declare global {
    interface Window {
        __lybox?: LyboxBridge;
        __lyboxRuntime?: LyboxRuntimeConfig;
        LyboxPlugin?: LyboxPublicApi;
    }
}

function getApi(): LyboxPublicApi {
    if (typeof window === 'undefined' || !window.LyboxPlugin) {
        throw new Error(
            'LYBox SDK 未挂载：生产构建需在 HTML 头部加入 <script src="/sdk/lybox-plugin-sdk.js">，' +
                '并由宿主 WebHostService 在 /sdk/ 路径下提供该资源。',
        );
    }
    return window.LyboxPlugin;
}

export function isWebView(): boolean {
    return typeof window !== 'undefined' && typeof window.__lybox?.rpc === 'function';
}

export function isLyboxBridgeAvailable(): boolean {
    return typeof window !== 'undefined' && typeof window.__lybox?.rpc === 'function';
}

export function invoke<T = unknown>(
    method: string,
    payload?: unknown,
    options?: RpcInvokeOptions,
): Promise<T> {
    return getApi().invoke(method, payload, options) as Promise<T>;
}

export function invokeLegacy<T = unknown>(method: string, ...args: unknown[]): Promise<T> {
    return getApi().invokeLegacy(method, ...args) as Promise<T>;
}

export function createLyboxClient(): {
    invoke: <TReq, TRes>(method: string, payload: TReq, options?: RpcInvokeOptions) => Promise<TRes>;
    invokeLegacy: (method: string, ...args: unknown[]) => Promise<unknown>;
} {
    return {
        invoke: <TReq, TRes>(method: string, payload: TReq, options?: RpcInvokeOptions) =>
            getApi().invoke(method, payload, options),
        invokeLegacy: (method: string, ...args: unknown[]) =>
            getApi().invokeLegacy(method, ...args),
    };
}

export function on<T = unknown>(event: string, cb: (data: T) => void): () => void {
    return getApi().on(event, cb as (data: unknown) => void);
}

export function off(event: string, cb?: (data: unknown) => void): void {
    const api = getApi();
    if (typeof api.off === 'function') api.off(event, cb);
}

export async function request(path: string, options?: RequestInit): Promise<Response> {
    const api = getApi();
    if (typeof api.request !== 'function') {
        throw new Error('LYBox SDK does not expose request(); use a runtime-specific helper instead.');
    }
    return api.request(path, options);
}

export async function getJson<T = unknown>(path: string, options?: RequestInit): Promise<T> {
    const api = getApi();
    if (typeof api.getJson !== 'function') {
        throw new Error('LYBox SDK does not expose getJson(); use a runtime-specific helper instead.');
    }
    return api.getJson(path, options) as Promise<T>;
}

export function installLyboxRuntime(config: LyboxRuntimeConfig): LyboxRuntimeConfig {
    const api = getApi();
    if (typeof api.installLyboxRuntime === 'function') {
        return api.installLyboxRuntime(config);
    }
    window.__lyboxRuntime = config;
    return config;
}

export function getLyboxRuntime(): LyboxRuntimeConfig | undefined {
    const api = getApi();
    if (typeof api.getLyboxRuntime === 'function') return api.getLyboxRuntime();
    return typeof window !== 'undefined' ? window.__lyboxRuntime : undefined;
}

export function registerLyboxMocks(mocks: Record<string, (...args: unknown[]) => unknown>): void {
    const api = getApi();
    if (typeof api.registerLyboxMocks === 'function') {
        api.registerLyboxMocks(mocks);
    } else {
        throw new Error('LYBox SDK does not expose registerLyboxMocks(); run lybox-mock locally.');
    }
}