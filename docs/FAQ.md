# 常见问题

## 插件为什么显示 Error？

优先检查 `plugin.json`、DLL 依赖、`MinPluginSdkVersion`、共享程序集清单和日志。SDK 版本解析失败或不兼容会在安装期/加载期拒绝插件。

## 菜单或导航不显示？

确认 ViewModel 的 `[ViewMap]`、`[NavigationItem]`、`[Menu]` 特性和 key；插件必须处于可加载状态，且 `RegisterAsync` 未抛异常。菜单父子关系由 `ParentKey` 构建。

## 设置项不显示？

在 `RegisterAsync` 中通过 `ISettingsService` 注册，PluginId 必须与 csproj 一致，设置定义的分组和排序应有效。设置由 SQLite 持久化。

## 如何使用第三方包？

在插件 csproj 添加 `PackageReference`。默认第三方程序集在插件 ALC 中私有加载；只有必须与宿主共享类型时才加入共享程序集清单，并评估版本契约影响。

## 是否支持热加载？

不支持常规热加载、热卸载。安装、升级、卸载、启用和禁用通常在重启后生效。

## 插件间如何通信？

SDK 没有独立的插件间消息总线。可通过 DI 注册共享服务；配置共享使用 `ISettingsService`。不要依赖未经契约定义的全局消息通道。

## Web 插件调用失败怎么办？

确认 `PluginKind=Web`、`PluginWwwroot`、`PluginEntryPage` 和 `wwwroot/index.html` 存在；前端引用 `/sdk/lybox-plugin-sdk.js`，不要硬编码端口。检查会话令牌、Origin、pluginId 和浏览器控制台日志。

## 为什么可收集 ALC 不代表可以随时卸载？

插件仍可能被 View、事件、任务或原生资源引用。宿主只在明确的关闭/清理流程中释放，插件必须在 `ShutdownAsync` 释放自身资源。
