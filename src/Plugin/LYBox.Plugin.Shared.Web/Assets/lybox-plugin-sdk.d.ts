/**
 * LYBox WebView IPC 浏览器 SDK —— TypeScript 类型声明
 *
 * @version 2.3.0-preview.7   ← 与 lybox-plugin-sdk.js 同源；CI 同步 version.props。
 *
 * 与 lybox-plugin-sdk.js 一一对应；任一端升级必须同步修改。
 * Vite dev 与生产 WebView 均经由宿主 /sdk/lybox-plugin-sdk.d.ts 提供类型提示（dev 经 vite proxy）。
 *
 * 业务侧统一：
 *   import { invoke, on, off, isWebView, PLUGIN_ID, transport } from '/sdk/lybox-plugin-sdk';
 */

/** SDK 版本号（与 LYBox/version.props 的 <LyboxVersion> 一致）。 */
export const SDK_VERSION: string;

/** 当前插件的 ID（与插件程序集 PluginId / Web 描述符一致）。由业务侧覆盖。 */
export const PLUGIN_ID: string;

/** RPC 调用选项：超时与取消。 */
export interface RpcInvokeOptions {
    signal?: AbortSignal;
    timeout?: number;
}

/** 原生 bridge 形态（WebView 模式由 ipc.js 注入；浏览器 mock 模式由 lybox-mock 注入）。 */
export interface LyboxBridge {
    invoke<T = unknown>(method: string, payload?: unknown, options?: RpcInvokeOptions): Promise<T>;
    invokeLegacy(method: string, ...args: unknown[]): Promise<unknown>;
    rpc(method: string, ...args: unknown[]): Promise<unknown>;
    on(event: string, cb: (data: unknown) => void): () => void;
    off(event: string, cb?: (data: unknown) => void): void;
    emit(event: string, data?: unknown): void;
    isWebView(): boolean;
    configureRuntime?(pluginId: string, sessionToken?: string): void;
    startSse?(pluginId: string, sessionToken?: string): void;
}

/** window 上的 LYBox bridge 与运行时声明。 */
export interface LyboxWindow extends Window {
    __lybox?: LyboxBridge;
    __lyboxRuntime?: LyboxRuntimeConfig;
    LyboxPlugin?: LyboxPublicApi;
}

/** 浏览器 mock 模式运行时配置（由 lybox-mock 注入）。 */
export interface LyboxRuntimeConfig {
    runtimeVersion: '1';
    pluginKey: string;
    pageId: string;
    mockBaseUrl?: string;
    sseUrl?: string;
    apiBaseUrl?: string;
}

/** 调用宿主 RPC 命令。WebView 形态走原生 bridge；Vite/浏览器形态走 HTTP 桥接或本地 mock。 */
export function invoke<T = unknown>(
    method: string,
    payload?: unknown,
    options?: RpcInvokeOptions,
): Promise<T>;

/** 兼容旧版多参调用（按位置参数序列化）。 */
export function invokeLegacy<T = unknown>(method: string, ...args: unknown[]): Promise<T>;

/** 创建一个最简 transport，可直接喂给生成器的 createXxxClient。 */
export function createLyboxClient(): {
    invoke: <TReq, TRes>(method: string, payload: TReq, options?: RpcInvokeOptions) => Promise<TRes>;
    invokeLegacy: (method: string, ...args: unknown[]) => Promise<unknown>;
};

/** 当前是否运行在宿主 WebView 内（window.__lybox 存在）。 */
export function isWebView(): boolean;

/** 是否暴露原生 WebView IPC 桥（兼容别名）。 */
export function isLyboxBridgeAvailable(): boolean;

/** 订阅宿主事件。返回取消订阅函数。 */
export function on<T = unknown>(event: string, cb: (data: T) => void): () => void;

/** 取消事件订阅（on 返回值的别名）。 */
export function off(event: string, cb?: (data: unknown) => void): void;

/** 发送 HTTP 请求到宿主 apiBaseUrl。 */
export function request(path: string, options?: RequestInit): Promise<Response>;

/** HTTP GET + JSON 解析便捷方法。 */
export function getJson<T = unknown>(path: string, options?: RequestInit): Promise<T>;

/** 安装 LYBox 运行时配置（浏览器 mock 模式由 lybox-mock 调用）。 */
export function installLyboxRuntime(config: LyboxRuntimeConfig): LyboxRuntimeConfig;

/** 获取当前运行时配置（未安装返回 undefined）。 */
export function getLyboxRuntime(): LyboxRuntimeConfig | undefined;

/** 注册一组本地 mock 处理（仅浏览器模式、mockBaseUrl 不可达时生效）。 */
export function registerLyboxMocks(mocks: Record<string, (...args: unknown[]) => unknown>): void;

/** 默认导出（window.LyboxPlugin 同步挂载同样的 surface）。 */
export interface LyboxPublicApi {
    invoke: typeof invoke;
    invokeLegacy: typeof invokeLegacy;
    createLyboxClient: typeof createLyboxClient;
    isWebView: typeof isWebView;
    isLyboxBridgeAvailable: typeof isLyboxBridgeAvailable;
    on: typeof on;
    off: typeof off;
    request: typeof request;
    getJson: typeof getJson;
    installLyboxRuntime: typeof installLyboxRuntime;
    getLyboxRuntime: typeof getLyboxRuntime;
    registerLyboxMocks: typeof registerLyboxMocks;
}

declare global {
    interface Window {
        __lybox?: LyboxBridge;
        __lyboxRuntime?: LyboxRuntimeConfig;
        LyboxPlugin?: LyboxPublicApi;
    }
}