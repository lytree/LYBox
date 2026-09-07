# 插件 SDK 版本契约

## 当前版本

宿主仓库 `version.props` 的 `LyboxVersion` 当前为 `2.3.0-preview.3`，同时作为 HostVersion、SDK NuGet 版本和 `PluginSdkContract.CurrentVersion` 的来源。插件业务版本由各插件 csproj 的 `PluginVersion` 独立维护。

构建时可用 `--host-version` 覆盖宿主与 SDK，`--sdk-version` 覆盖插件解析的 SDK 版本；环境变量 `LYBOX_HOST_VERSION` 也可参与版本解析，具体优先级以 `Directory.Build.props` 和构建脚本为准。

## SDK 包

- `LYBox.Plugin.Generators`：Roslyn 增量源生成器。
- `LYBox.Plugin.CommandLine`：CLI 插件契约。
- `LYBox.Plugin.Shared`：核心插件契约、服务、模型和 Avalonia 组件。
- `LYBox.Plugin.Shared.Web`：WebView、Kestrel、RPC、SSE、Channel 和浏览器 SDK。

## 兼容性规则

插件清单中的 `minPluginSdkVersion` 来自 `MinPluginSdkVersion`。宿主在安装期和加载期都校验该值：版本必须可解析为三段 SemVer，Major 必须与宿主一致，Minor/Build 不得高于宿主能力范围；解析失败按不兼容处理。未声明时使用 `0.0.0`。

不兼容插件不会被加载；应升级宿主、重新编译插件，或在构建时指定匹配的 SDK 包版本。修改共享公共 API 时应同步评估版本含义，并重新构建 SDK 与插件。

## 清单相关属性

普通插件：`PluginId`、`PluginName`、`PluginAuthor`、`PluginDescription`、`PluginVersion`、`MinPluginSdkVersion`。Web 插件增加 `PluginKind=Web`、`PluginWwwroot`、`PluginEntryPage`。CLI 插件可增加 `GeneratePluginCliIndex`、`PluginCliAlias` 等属性。

插件运行时还可通过 `shared-assemblies.txt` 声明需要由宿主默认 ALC 共享的程序集；其余第三方依赖保持在插件独立 ALC 中。
