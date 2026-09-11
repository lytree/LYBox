# 插件安装、卸载与升级设计

## 当前实现

实现位于 `src/Layout/LYBox.Layout.Core/Services/PluginInstallationManager.cs` 与 `PluginLoader.cs`。宿主不依赖热卸载：运行中的插件 DLL 可能被锁定，因此变更采用延迟处理。

## 插件数据目录约定（主体 Data/{PluginId}/）

**所有插件的可写数据均落在主体的 Data 根目录下：**

| 平台    | 宿主 Data 根（默认）              | 环境变量覆盖                |
| ----- | -------------------------- | --------------------- |
| Windows | `%LOCALAPPDATA%/LYBox`     | `LYBOX_DATA_ROOT`      |
| Linux | `~/.config/LYBox`          | `LYBOX_DATA_ROOT`      |
| macOS | `~/Library/Application Support/LYBox`（XDG → `~/.config/LYBox`） | `LYBOX_DATA_ROOT` |

目录结构：

```text
{DataRoot}/
├── appdata.db                       # 宿主共享 SQLite（设置、插件状态）
└── {PluginId}/                      # 每个插件独立子目录
    ├── settings.json                # 仅当 ISettingsService 不可用时的 Downloader JSON 回退
    ├── Exports/                     # ProDataGrid 导出文件
    ├── cache/ResPool.ryx            # BTSou 资源池缓存
    ├── data/forward-{chatId}.db     # TDLSharp 转发去重 DB
    ├── history/history-{scriptId}.db
    └── tdl/                         # TDLSharp TDLib 数据根（默认 TDL.TdlRootPath）
```

**契约**：`src/Plugin/LYBox.Plugin.Shared/Services/IPluginDataDirectoryProvider.cs`

```csharp
public interface IPluginDataDirectoryProvider
{
    string HostDataRoot { get; }
    string GetPluginDataDirectory(string pluginId);
    string GetSubDirectory(string pluginId, string subPath);
}
```

注册方式：`ServiceCollectionExtensions.AddAvaloniaServices` 中注册为单例。

**升级与卸载时的行为：**

- **升级（覆盖安装）**：`Data/{PluginId}/` 子目录被**保留**——这正是本约定解决的核心问题。
  宿主在 `ProcessSinglePendingUpgrade` 步骤 2（重命名旧目录之前）读取旧 manifest 的 `SchemaVersion`，
  暂存到 in-memory 的 `_pendingPreviousSchemaVersions` 字典；在 `LoadAllPluginManifests` 之后回填到
  对应 `PluginInfo.PreviousSchemaVersion`，由 `RegisterAsync` 透传给插件。
- **卸载**：`ProcessPendingUninstalls` 在删除 `plugins/{PluginId}/` 的同时清理 `Data/{PluginId}/`。
  若插件声明 `PreserveDataOnUninstall`（TODO）则保留数据。

## 升级场景下的数据库迁移

plugin.json 新增两个可选字段（与 SDK 契约独立，由插件作者自行维护）：

| 字段                       | 类型      | 说明 |
| ------------------------ | ------- | -- |
| `schemaVersion`          | string  | 当前版本的插件数据 schema 版本号。缺省 "0"。每次不兼容变更时递增。 |
| `requiresDataMigration`  | bool    | 当前升级是否需要数据迁移。true 时宿主会等待插件执行迁移钩子（由插件实现决定如何迁移）。 |

宿主行为：

1. 启动期扫描 `plugins/.pending/*.upgrade.json` 时读取旧 manifest 的 `schemaVersion`（若存在）。
2. 加载完成后回填到 `PluginInfo.PreviousSchemaVersion` 和 `PluginInfo.CurrentSchemaVersion`。
3. 插件 `RegisterAsync` 中可对比这两个字段自行决定是否执行迁移：
   - `PreviousSchemaVersion != CurrentSchemaVersion` ⇒ 需要迁移。
   - `RequiresDataMigration == true` ⇒ 升级作者明确声明需要迁移。
4. 迁移执行后，插件把当前 schema 版本持久化到本地 DB（如 `PRAGMA user_version`）。

## 安装

安装器读取 zip，限制条目数量和解压总大小，校验路径不能越界，再读取 `plugin.json` 并校验 PluginId、SDK 版本和目标目录。成功后写入插件目录或待处理目录；同时调用 `PluginDataDirectoryProvider` 为插件创建 `Data/{PluginId}/`。

## 覆盖升级

```text
安装新包 → plugins/.pending/{PluginId}.new/
       → 写入 {PluginId}.upgrade.json
       → 提示重启
重启 → PluginLoader.ProcessPendingUpgrades()
       → 迁移新目录、保留必要状态
       → 删除指令文件
       → 重新发现并加载
```

迁移失败时保留可恢复的旧目录并将插件置为错误状态，避免半更新目录进入正常加载流程。连续升级时以最后一次待处理版本为准。

## 卸载与禁用

卸载写入 PendingUninstall，由启动早期处理；禁用只改变 manifest 状态，不触发运行时重载。启用后通常恢复为 Installed，重启后重新加载。

## 约束

不要在插件仍运行时手工覆盖 DLL；不要修改外部开发目录；安装包必须由构建脚本产出。插件应在 `ShutdownAsync` 中释放原生客户端、文件句柄、任务和事件订阅。

**插件数据文件位置约束：**

- 插件的可写文件（缓存、本地 DB、导出、用户数据等）必须通过 `IPluginDataDirectoryProvider` 解析到 `Data/{PluginId}/` 下，禁止再次回退到 `AppContext.BaseDirectory` / `Environment.SpecialFolder.UserProfile` / `Environment.SpecialFolder.ApplicationData` 等位置。
- 早期版本遗留数据可通过一次性 `File.Copy` 迁移到新位置（参考 `Downloader/DownloadSettingsStore.cs`、`TDLSharp/Services/ForwardDb.cs`）。
- 升级时若 `Data/{PluginId}/` 已存在同名文件，应优先保留并由插件 `RegisterAsync` 在对比 `PreviousSchemaVersion`/`CurrentSchemaVersion` 后决定是否迁移 schema。
