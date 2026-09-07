# 插件安装、卸载与升级设计

## 当前实现

实现位于 `src/Layout/LYBox.Layout.Core/Services/PluginInstallationManager.cs` 与 `PluginLoader.cs`。宿主不依赖热卸载：运行中的插件 DLL 可能被锁定，因此变更采用延迟处理。

## 安装

安装器读取 zip，限制条目数量和解压总大小，校验路径不能越界，再读取 `plugin.json` 并校验 PluginId、SDK 版本和目标目录。成功后写入插件目录或待处理目录。

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
