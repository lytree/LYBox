// LYBox WebView IPC 引导脚本（Wails v2 模型 + HTTP 传输回退）
// 由宿主经 ExecuteScriptAsync 注入页面（WebView 模式），或由 mock-server 直接 <script> 引入（浏览器模式）。
// 提供统一入口 window.__lybox.rpc(name, ...args): Promise<T>。
//
// 传输层自动检测环境：
//   - WebView 模式：invokeCSharpAction 存在 → 走原生 IPC（前缀字符 + JSON）
//   - 浏览器模式：invokeCSharpAction 不存在 → 走 HTTP（POST /__bridge/{pluginId}/{action}）
//
// C# → JS 推送：
//   - WebView 模式：宿主执行 window.__lybox.resolve / dispatch
//   - 浏览器模式：复用 SSE（EventSource /sse/{pluginId}），dispatch 由 SSE 监听器触发
(function () {
  if (window.__lybox) return;

  var callbacks = new Map();             // callbackId -> {resolve, reject}
  var eventListeners = Object.create(null); // name -> Set<cb>
  var channelListeners = Object.create(null);// id -> Set<cb>
  var runtimePluginId = null;
  var runtimeSession = null;

  // —— 传输层抽象 ——
  var isWebView = (typeof invokeCSharpAction === 'function');

  // —— console 桥（仅 WebView 模式）：hook console.log/info/warn/error → 'L' 信封转发宿主日志 ——
  // 浏览器模式不安装（开发者可直接用原生 DevTools）。宿主端由 WebConsoleBridge 解析并写 ILogger。
  // 限制：单参数截断至 2000 字符、最多 8 个参数，防止高频/超长输出刷爆 IPC 与日志。
  if (isWebView) {
    var CONSOLE_LEVELS = ['log', 'info', 'warn', 'error'];
    var CONSOLE_MAX_ARGS = 8;
    var CONSOLE_MAX_LEN = 2000;
    for (var li = 0; li < CONSOLE_LEVELS.length; li++) {
      (function (level) {
        var original = console[level] ? console[level].bind(console) : function () {};
        console[level] = function () {
          original.apply(null, arguments);
          try {
            var parts = [];
            for (var i = 0; i < arguments.length && i < CONSOLE_MAX_ARGS; i++) parts.push(serializeConsoleArg(arguments[i]));
            if (arguments.length > CONSOLE_MAX_ARGS) parts.push('…(' + (arguments.length - CONSOLE_MAX_ARGS) + ' more)');
            if (parts.length) send('L' + JSON.stringify({ l: level, a: parts }));
          } catch (e) { /* 静默：console 桥失败不得影响页面本身 */ }
        };
      })(CONSOLE_LEVELS[li]);
    }
  }

  function serializeConsoleArg(value) {
    var text;
    if (value instanceof Error) {
      text = value.name + ': ' + value.message + (value.stack ? '\n' + value.stack : '');
    } else if (typeof value === 'string') {
      text = value;
    } else {
      try {
        text = JSON.stringify(value);
        if (text === undefined) text = String(value);
      } catch (e) { text = String(value); }
    }
    if (text.length > CONSOLE_MAX_LEN) text = text.slice(0, CONSOLE_MAX_LEN) + '…[truncated]';
    return text;
  }

  // HTTP 桥接统一入口（S4 BC-6）：POST /__bridge/{pluginId}/{action}
  function bridge(action) {
    return '/__bridge/' + encodeURIComponent(runtimePluginId || 'mock-plugin') + '/' + action;
  }

  function send(body) {
    if (isWebView) {
      invokeCSharpAction(body);
    } else {
      // 浏览器模式：HTTP 桥接
      var prefix = body[0];
      var payload = body.substring(1);
      if (prefix === 'C') {
        httpRpc(payload);
      } else if (prefix === 'E') {
        // 事件 emit 走 POST /__bridge/{pluginId}/emit（浏览器模式一般用不到，保留通道）
        fetch(bridge('emit'), { method: 'POST', headers: httpHeaders(), body: payload })['catch'](function (e) { console.error('[__lybox] HTTP emit 失败', e); });
      } else if (prefix === 'X') {
        fetch(bridge('channel-close'), { method: 'POST', headers: httpHeaders('text/plain'), body: payload })['catch'](function (e) { console.error('[__lybox] HTTP channel close 失败', e); });
      }
    }
  }

  // HTTP RPC：POST /__bridge/{pluginId}/rpc，body = {name, args, callbackId}
  // 响应 {result: ...} 或 {error: "..."}
  function httpRpc(payload) {
    var msg;
    try { msg = JSON.parse(payload); } catch (e) { return; }
    fetch(bridge('rpc'), {
      method: 'POST',
      headers: httpHeaders(),
      body: JSON.stringify(msg)
    }).then(function (r) { return r.json(); })
      .then(function (r) {
        resolve(
          msg.id || msg.callbackId,
          r.ok === false ? r.error : (r.error || null),
          Object.prototype.hasOwnProperty.call(r, 'payload') ? r.payload : r.result);
      })['catch'](function (e) {
        resolve(msg.id || msg.callbackId, {
          code: 'transport_error',
          message: e && e.message ? e.message : String(e)
        }, null);
      });
  }

  function httpHeaders(contentType) {
    var headers = { 'Content-Type': contentType || 'application/json' };
    if (runtimeSession) headers['X-LYBox-Session'] = runtimeSession;
    return headers;
  }

  // Canonical single-payload API.
  function invoke(name, payload, options) {
    return createInvocation(name, {
      version: 2,
      kind: 'plugin-rpc-call',
      pluginId: runtimePluginId,
      method: name,
      payload: payload === undefined ? null : payload
    }, options);
  }

  // Legacy positional arguments API.
  function invokeLegacy(name) {
    var args = Array.prototype.slice.call(arguments, 1);
    return createInvocation(name, { name: name, args: args }, null);
  }

  function createInvocation(name, message, options) {
    return new Promise(function (resolve, reject) {
      var id = name + '-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 10);
      var timeoutId = null;
      var abortHandler = null;
      var finish = function () {
        if (timeoutId !== null) clearTimeout(timeoutId);
        if (options && options.signal && abortHandler)
          options.signal.removeEventListener('abort', abortHandler);
      };
      callbacks.set(id, { resolve: resolve, reject: reject, finish: finish });

      if (options && Number.isFinite(options.timeout) && options.timeout > 0) {
        timeoutId = setTimeout(function () {
          callbacks.delete(id);
          finish();
          reject(toError({ code: 'timeout', message: 'RPC call timed out.' }));
        }, options.timeout);
      }
      if (options && options.signal) {
        abortHandler = function () {
          callbacks.delete(id);
          finish();
          reject(toError({ code: 'cancelled', message: 'RPC call was cancelled.' }));
        };
        if (options.signal.aborted) {
          abortHandler();
          return;
        }
        options.signal.addEventListener('abort', abortHandler, { once: true });
      }

      message.id = id;
      message.callbackId = id;
      send('C' + JSON.stringify(message));
    });
  }

  // C# → JS：回传调用结果（err 为字符串则 reject；result 带 __channel 则包装为 Channel）。
  function resolve(id, err, result) {
    var cb = callbacks.get(id);
    if (!cb) return;
    callbacks.delete(id);
    if (cb.finish) cb.finish();
    if (err) {
      cb.reject(toError(err));
    } else if (result && typeof result === 'object' && result.__channel) {
      cb.resolve(makeChannel(result.id, result.itemType));
    } else {
      cb.resolve(result);
    }
  }

  function makeChannel(id, itemType) {
    return {
      id: id,
      itemType: itemType,
      on: function (cb) {
        if (!channelListeners[id]) channelListeners[id] = new Set();
        channelListeners[id].add(cb);
        return function () { (channelListeners[id] || new Set()).delete(cb); };
      },
      close: function () { send('X' + id); }
    };
  }

  function channelOnData(id, data) {
    var set = channelListeners[id];
    if (set) set.forEach(function (cb) { try { cb(data); } catch (e) { console.error(e); } });
  }
  function channelOnClose(id) {
    var set = channelListeners[id];
    if (set) set.forEach(function (cb) { try { cb(null, true); } catch (e) { console.error(e); } });
    delete channelListeners[id];
  }

  // —— 事件系统（Wails EventsOn/Emit 语义）——
  function on(name, cb) {
    if (!eventListeners[name]) eventListeners[name] = new Set();
    eventListeners[name].add(cb);
    return function () { (eventListeners[name] || new Set()).delete(cb); };
  }
  function off(name, cb) { (eventListeners[name] || new Set()).delete(cb); }
  function emit(name, data) { send('E' + JSON.stringify({ name: name, data: data })); }
  function dispatch(name, data) {
    var set = eventListeners[name];
    if (set) set.forEach(function (cb) { try { cb(data); } catch (e) { console.error(e); } });
  }

  // —— 内置：开发模式热刷新（宿主 dev wwwroot 文件变化 → SSE dispatch __lybox:reload → 自动刷新页面）——
  on('__lybox:reload', function () { try { location.reload(); } catch (e) { /* 刷新失败静默 */ } });

  // 宿主注入绑定清单（WebView 模式）。浏览器模式由 mock-server 提供 /__lybox/bindings.json。
  // 当前实现为 noop：rpc 入口已统一为 __lybox.rpc(name, args)，无需构建 window.go。
  // manifest 仅供调试面板展示命令列表，不影响运行时调用。
  function setBindings(manifestJson) {
    window.__lyboxBindings = manifestJson; // 保留供调试工具读取
  }

  // —— SSE 监听器（C# → JS 主动推送）——
  // WebView 模式由宿主在 ipc.js 注入完成后显式调用 startSse(pluginId) 启动。
  // 浏览器模式自动启动（mock-server 或 Avalonia Kestrel 均提供 /sse/{pluginId}）。
  var eventSource = null;
  var activeSseUrl = null;
  function configureRuntime(pluginId, sessionToken) {
    runtimePluginId = pluginId || runtimePluginId;
    runtimeSession = sessionToken || runtimeSession;
  }

  function startSse(pluginId, sessionToken) {
    if (!pluginId || typeof EventSource === 'undefined') return;
    try {
      configureRuntime(pluginId, sessionToken);
      var suffix = runtimeSession ? '?session=' + encodeURIComponent(runtimeSession) : '';
      var url = '/sse/' + encodeURIComponent(pluginId) + suffix;
      if (eventSource && activeSseUrl === url) return;
      if (eventSource) eventSource.close();
      var es = new EventSource(url);
      eventSource = es;
      activeSseUrl = url;
      es.addEventListener('dispatch', function (e) {
        try {
          var msg = JSON.parse(e.data);
          dispatch(msg.name, msg.data);
        } catch (err) { console.error('[__lybox] SSE dispatch 解析失败', err); }
      });
      es.addEventListener('channel-data', function (e) {
        try {
          var msg = JSON.parse(e.data);
          channelOnData(msg.id, msg.data);
        } catch (err) { console.error('[__lybox] SSE channel-data 解析失败', err); }
      });
      es.addEventListener('channel-close', function (e) {
        try {
          var msg = JSON.parse(e.data);
          channelOnClose(msg.id);
        } catch (err) { console.error('[__lybox] SSE channel-close 解析失败', err); }
      });
      es.addEventListener('channel-error', function (e) {
        try {
          var msg = JSON.parse(e.data);
          var set = channelListeners[msg.id];
          if (set) set.forEach(function (cb) { try { cb(null, true, toError(msg.error)); } catch (err) { console.error(err); } });
        } catch (err) { console.error('[__lybox] SSE channel-error parse failed', err); }
      });
      es.onerror = function () { /* 浏览器会自动重连，无需处理 */ };
    } catch (e) {
      console.error('[__lybox] SSE 初始化失败', e);
      eventSource = null;
      activeSseUrl = null;
    }
  }

  window.__lybox = {
    invoke: invoke,
    rpc: invokeLegacy,
    invokeLegacy: invokeLegacy,
    resolve: resolve,
    on: on,
    off: off,
    emit: emit,
    dispatch: dispatch,
    setBindings: setBindings,
    configureRuntime: configureRuntime,
    startSse: startSse,
    channel: { on: function (id, cb) { return makeChannel(id, '').on(cb); }, onData: channelOnData, onClose: channelOnClose },
    isWebView: isWebView   // 供前端检测当前环境
  };

  if (typeof window.dispatchEvent === 'function' && typeof CustomEvent === 'function') {
    window.dispatchEvent(new CustomEvent('lybox:bridge-ready', { detail: window.__lybox }));
  }

  function toError(value) {
    var detail = value && typeof value === 'object'
      ? value
      : { code: 'handler_error', message: String(value || 'RPC call failed.') };
    var error = new Error(detail.message || 'RPC call failed.');
    error.code = detail.code || 'handler_error';
    if (detail.details !== undefined) error.details = detail.details;
    if (detail.traceId) error.traceId = detail.traceId;
    return error;
  }

  // 通知宿主运行时就绪（WebView 模式握手；浏览器模式无监听者，无副作用）。
  send('E' + JSON.stringify({ name: '__lybox:ready', data: null }));
})();
