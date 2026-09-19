/**
 * LYBox WebView IPC 浏览器 SDK（ES Module）
 *
 * @version 2.3.0-preview.7   ← 与 LYBox/version.props 的 <LyboxVersion> 保持一致；
 *                                 CI 通过 build.cs 的 ReplaceSdkVersionToken 在打包前同步。
 *
 * 为浏览器开发模式（无 WebView）提供与原生 LYBox 宿主一致的 RPC 调用体验。
 * 运行环境检测：
 *   - WebView 模式：window.__lybox（由 LYBox 嵌入式 ipc.js 注入）存在 → 直接调用 window.__lybox.rpc
 *   - 浏览器模式：window.__lybox.rpc 不存在 → 通过 HTTP 桥接（POST /__bridge/{pluginId}/{action}）
 *     与 SSE（EventSource /sse/{pluginId}）访问宿主
 *
 * 正式调用走 lyboxInvoke(method, payload, options): Promise<T>。
 * 旧位置参数调用由 lyboxInvokeLegacy(method, ...args) 保留兼容。
 * 事件订阅走 lyboxOn(eventName, handler): unsubscribe。
 * HTTP 请求走 lyboxRequest(path, options) / lyboxGetJson(path, options)。
 *
 * 与 templates/web-plugin-ui/src/lybox.ts 方法签名一一对应；两端任一升级必须同步修改。
 * 宿主侧运行时校验：WebHostService 启动时读取本资源，校验 @version 头是否与 <PluginSdkVersion> 一致。
 */

const REQUEST_KIND = "lybox-ipc-request";
const RESPONSE_KIND = "lybox-ipc-response";
const BRIDGE_READY_EVENT = "lybox:bridge-ready";

let runtimeConfig = globalThis.window?.__lyboxRuntime;
let mockRegistry = Object.create(null);
const eventListeners = new Map();
const bridgeSubscriptions = new Map();
const eventSourceEventNames = new Set();
let eventSource;
let activeEventStreamUrl;
let bridgeReadyListenerInstalled = false;

/**
 * 安装 LYBox 运行时配置（mock 模式下由 lybox-mock 注入）。
 * @param {{runtimeVersion: '1', pluginKey: string, pageId: string, mockBaseUrl?: string, sseUrl?: string, apiBaseUrl?: string}} config
 */
export function installLyboxRuntime(config) {
  validateRuntimeConfig(config);
  runtimeConfig = Object.freeze({ ...config });
  if (typeof window !== "undefined" && !window.__lyboxRuntime) window.__lyboxRuntime = runtimeConfig;
  installBridgeReadyListener();
  configureEventTransport();
  return runtimeConfig;
}

export function getLyboxRuntime() {
  return runtimeConfig ?? globalThis.window?.__lyboxRuntime;
}

/**
 * 注册一组本地 mock 处理（仅在浏览器模式、mockBaseUrl 不可达时生效），
 * 让前端可以在没有宿主的情况下本地试跑。
 */
export function registerLyboxMocks(mocks) {
  if (!mocks || typeof mocks !== "object" || Array.isArray(mocks)) {
    throw new TypeError("Plugin mocks must be an object keyed by method name.");
  }
  const next = Object.create(null);
  for (const [method, handler] of Object.entries(mocks)) {
    if (!method.trim() || typeof handler !== "function") {
      throw new TypeError("Each plugin mock must have a non-empty method and function handler.");
    }
    next[method] = handler;
  }
  mockRegistry = next;
}

/**
 * 调用宿主注册的 RPC 命令。WebView 模式走 window.__lybox.rpc（Promise），
 * 浏览器模式走 HTTP 桥接或本地 mock。
 */
export async function lyboxInvoke(method, payload, options) {
  if (typeof method !== "string" || !method.trim()) {
    throw structuredError("invalid_method", "IPC method must not be empty.");
  }
  const values = Array.prototype.slice.call(arguments, 1);
  if (isLegacyInvocation(values)) {
    return await lyboxInvokeLegacy(method, ...values);
  }

  const lybox = globalThis.window?.__lybox;
  if (lybox && typeof lybox.invoke === "function") {
    return await lybox.invoke(method, payload ?? null, options);
  }
  if (lybox && typeof lybox.rpc === "function") {
    return await withInvokeOptions(lybox.rpc(method, payload ?? null), options);
  }
  const config = getLyboxRuntime();
  if (config?.mockBaseUrl) {
    return await invokeMockHttp(config, method, payload ?? null, options);
  }
  const mock = mockRegistry[method];
  if (mock) return await withInvokeOptions(Promise.resolve(mock(payload ?? null)), options);
  throw structuredError("bridge_unavailable", "LYBox WebView IPC bridge is unavailable.");
}

export async function lyboxInvokeLegacy(method, ...args) {
  if (typeof method !== "string" || !method.trim()) {
    throw structuredError("invalid_method", "IPC method must not be empty.");
  }
  const lybox = globalThis.window?.__lybox;
  if (lybox && typeof lybox.invokeLegacy === "function") {
    return await lybox.invokeLegacy(method, ...args);
  }
  if (lybox && typeof lybox.rpc === "function") {
    return await lybox.rpc(method, ...args);
  }
  const config = getLyboxRuntime();
  if (config?.mockBaseUrl) {
    return await invokeMockHttp(config, method, null, undefined, args);
  }
  const mock = mockRegistry[method];
  if (mock) return await mock(...args);
  throw structuredError("bridge_unavailable", "LYBox WebView IPC bridge is unavailable.");
}

export function createLyboxClient() {
  return Object.freeze({ invoke: lyboxInvoke, invokeLegacy: lyboxInvokeLegacy });
}

/** 当前环境是否暴露原生 WebView IPC 桥。 */
export function isLyboxBridgeAvailable() {
  if (typeof window === "undefined") return false;
  return typeof window.__lybox?.rpc === "function";
}

/** 订阅宿主事件。返回取消订阅函数。 */
export function lyboxOn(eventName, handler) {
  if (typeof eventName !== "string" || !eventName.trim()) {
    throw new TypeError("Plugin event name must not be empty.");
  }
  if (typeof handler !== "function") {
    throw new TypeError("Plugin event handler must be a function.");
  }
  const listeners = eventListeners.get(eventName) ?? new Set();
  listeners.add(handler);
  eventListeners.set(eventName, listeners);
  configureEventTransport();
  return () => {
    listeners.delete(handler);
    if (listeners.size === 0) {
      eventListeners.delete(eventName);
      unbindBridgeEvent(eventName);
    }
  };
}

/** 取消事件订阅（lyboxOn 返回值的别名）。 */
export function lyboxOff(eventName, handler) {
  const listeners = eventListeners.get(eventName);
  if (!listeners) return;
  if (handler) listeners.delete(handler);
  else listeners.clear();
  if (listeners.size === 0) {
    eventListeners.delete(eventName);
    unbindBridgeEvent(eventName);
  }
}

/** 发送 HTTP 请求到宿主 apiBaseUrl。 */
export function lyboxRequest(path, options) {
  const runtime = requireRuntimeConfig();
  if (!runtime.apiBaseUrl) {
    throw structuredError("api_unavailable", "The plugin API base URL is unavailable.");
  }
  return fetch(new URL(path, runtime.apiBaseUrl), options ?? {});
}

/** HTTP GET + JSON 解析便捷方法。 */
export async function lyboxGetJson(path, options) {
  const response = await lyboxRequest(path, options);
  if (!response.ok) {
    throw structuredError("api_error", `API request failed with status ${response.status}.`);
  }
  return await response.json();
}

async function invokeMockHttp(config, method, payload, options, legacyArgs) {
  const request = legacyArgs ? {
    kind: REQUEST_KIND,
    id: createRequestId(),
    pluginKey: config.pluginKey,
    method,
    args: legacyArgs,
    payload: legacyArgs.length <= 1 ? (legacyArgs[0] ?? null) : legacyArgs,
  } : {
    version: 2,
    kind: "plugin-rpc-call",
    id: createRequestId(),
    pluginId: config.pluginKey,
    pluginKey: config.pluginKey,
    method,
    payload,
  };
  let response;
  try {
    response = await fetch(new URL("/__lybox/ipc", config.mockBaseUrl), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(request),
    });
  } catch (error) {
    throw toStructuredError(error, "mock_unavailable");
  }
  let value;
  try { value = await response.json(); }
  catch { throw structuredError("mock_invalid_response", "Mock server returned invalid JSON."); }
  if (!value || value.kind !== RESPONSE_KIND || value.id !== request.id) {
    throw structuredError("mock_invalid_response", "Mock server returned an invalid IPC response.");
  }
  if (response.ok && value.ok) return await withInvokeOptions(Promise.resolve(value.payload), options);
  throw value.error ?? structuredError("ipc_error", "Mock IPC request failed.");
}

function isLegacyInvocation(values) {
  if (values.length <= 1) return false;
  if (values.length > 2) return true;
  return !isInvokeOptions(values[1]);
}

function isInvokeOptions(value) {
  return !!value && typeof value === "object" && !Array.isArray(value)
    && (Object.prototype.hasOwnProperty.call(value, "timeout")
      || Object.prototype.hasOwnProperty.call(value, "signal"));
}

function withInvokeOptions(promise, options) {
  if (!options) return promise;
  return new Promise((resolve, reject) => {
    let timeoutId;
    const finish = () => {
      if (timeoutId !== undefined) clearTimeout(timeoutId);
      options.signal?.removeEventListener?.("abort", abort);
    };
    const abort = () => {
      finish();
      reject(structuredError("cancelled", "RPC call was cancelled."));
    };
    if (options.signal?.aborted) return abort();
    options.signal?.addEventListener?.("abort", abort, { once: true });
    if (Number.isFinite(options.timeout) && options.timeout > 0) {
      timeoutId = setTimeout(() => {
        finish();
        reject(structuredError("timeout", "RPC call timed out."));
      }, options.timeout);
    }
    promise.then(
      value => { finish(); resolve(value); },
      error => { finish(); reject(toStructuredError(error, "ipc_error")); });
  });
}

function installBridgeReadyListener() {
  if (bridgeReadyListenerInstalled || typeof window === "undefined" || typeof window.addEventListener !== "function") return;
  bridgeReadyListenerInstalled = true;
  window.addEventListener(BRIDGE_READY_EVENT, configureEventTransport);
}

function configureEventTransport() {
  installBridgeReadyListener();
  if (isLyboxBridgeAvailable()) {
    closeEventStream();
    for (const eventName of eventListeners.keys()) bindBridgeEvent(eventName);
    return;
  }
  configureEventStream();
}

function bindBridgeEvent(eventName) {
  const bridge = globalThis.window?.__lybox;
  if (!bridge || typeof bridge.on !== "function" || bridgeSubscriptions.has(eventName)) return false;
  const unsubscribe = bridge.on(eventName, data => dispatchPluginEvent({ eventName, data }));
  bridgeSubscriptions.set(eventName, typeof unsubscribe === "function" ? unsubscribe : () => {});
  return true;
}

function unbindBridgeEvent(eventName) {
  const unsubscribe = bridgeSubscriptions.get(eventName);
  bridgeSubscriptions.delete(eventName);
  try { unsubscribe?.(); } catch { }
}

function closeEventStream() {
  eventSource?.close();
  eventSource = undefined;
  activeEventStreamUrl = undefined;
  eventSourceEventNames.clear();
}

function configureEventStream() {
  const nextUrl = getLyboxRuntime()?.sseUrl ?? undefined;
  if (nextUrl === activeEventStreamUrl) return;
  closeEventStream();
  activeEventStreamUrl = nextUrl;
  if (!nextUrl || typeof EventSource !== "function") return;
  eventSource = new EventSource(nextUrl);
  eventSource.onmessage = event => dispatchPluginEvent({ eventName: "message", data: parseData(event.data) });
  eventSource.addEventListener("dispatch", event => {
    const value = parseData(event.data);
    dispatchPluginEvent({ eventName: value?.name, data: value?.data });
  });
  for (const eventName of eventListeners.keys()) {
    ensureEventSourceListener(eventName);
  }
}

function ensureEventSourceListener(eventName) {
  if (!eventSource || eventName === "message" || eventSourceEventNames.has(eventName)) return;
  eventSourceEventNames.add(eventName);
  eventSource.addEventListener(eventName, event => dispatchPluginEvent({ eventName, data: parseData(event.data) }));
}

function dispatchPluginEvent(value) {
  const envelope = parseEventEnvelope(value);
  if (!envelope) return;
  const listeners = eventListeners.get(envelope.eventName);
  if (!listeners) return;
  for (const listener of [...listeners]) {
    try { listener(envelope.data); }
    catch (error) { queueMicrotask(() => { throw error; }); }
  }
}

function parseEventEnvelope(value) {
  let candidate = value;
  if (typeof candidate === "string") {
    try { candidate = JSON.parse(candidate); } catch { return undefined; }
  }
  if (!candidate || typeof candidate !== "object") return undefined;
  if (typeof candidate.eventName !== "string" || !candidate.eventName.trim()) return undefined;
  return { eventName: candidate.eventName, data: candidate.data };
}

function parseData(value) {
  try { return JSON.parse(value); } catch { return value; }
}

function createRequestId() {
  return `lybox-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`}`;
}

function requireRuntimeConfig() {
  const config = getLyboxRuntime();
  if (config) return config;
  throw structuredError("runtime_unavailable", "LYBox plugin runtime is not installed.");
}

function validateRuntimeConfig(config) {
  if (!config || typeof config !== "object"
    || config.runtimeVersion !== "1"
    || typeof config.pluginKey !== "string" || !config.pluginKey.trim()
    || typeof config.pageId !== "string" || !config.pageId.trim()) {
    throw new TypeError("LYBox plugin runtime configuration is invalid.");
  }
}

function structuredError(code, message, details) {
  return { code, message, details };
}
function toStructuredError(error, fallbackCode) {
  if (error && typeof error === "object" && typeof error.code === "string") return error;
  return structuredError(fallbackCode, error instanceof Error ? error.message : String(error));
}

const api = Object.freeze({
  installLyboxRuntime,
  getLyboxRuntime,
  registerLyboxMocks,
  invoke: lyboxInvoke,
  invokeLegacy: lyboxInvokeLegacy,
  createLyboxClient,
  isLyboxBridgeAvailable,
  on: lyboxOn,
  off: lyboxOff,
  request: lyboxRequest,
  getJson: lyboxGetJson,
});

if (typeof window !== "undefined") {
  installBridgeReadyListener();
  window.LyboxPlugin = api;
  if (window.__lyboxRuntime) installLyboxRuntime(window.__lyboxRuntime);
}

export {
  lyboxInvoke as invoke,
  lyboxInvokeLegacy as invokeLegacy,
  lyboxOn as on,
  lyboxOff as off,
  lyboxRequest as request,
  lyboxGetJson as getJson,
};

export default api;
