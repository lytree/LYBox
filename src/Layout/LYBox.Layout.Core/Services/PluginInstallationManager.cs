using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;

namespace LYBox.Layout.Core.Services;

public sealed class PluginInstallationManager : IPluginInstallationManager
{
    private const int MaxPackageEntries = 4096;
    private const long MaxPackageUncompressedBytes = 256L * 1024 * 1024;

    private readonly IPluginLoader _pluginLoader;
    private readonly string _pluginsDirectory;
    private readonly IReadOnlySet<string> _readOnlyPluginIds;

    public event EventHandler<PluginInfo>? PluginInstalled;
    public event EventHandler<PluginInfo>? PluginUninstalled;
    public event EventHandler<PluginInfo>? PluginUpgradeScheduled;

    public PluginInstallationManager(
        IPluginLoader pluginLoader,
        string? pluginsDirectory = null,
        IReadOnlySet<string>? readOnlyPluginIds = null)
    {
        _pluginLoader = pluginLoader;
        _pluginsDirectory = Path.GetFullPath(
            pluginsDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"));
        _readOnlyPluginIds = readOnlyPluginIds ?? new HashSet<string>(StringComparer.Ordinal);
        Directory.CreateDirectory(_pluginsDirectory);
    }

    public string GetPluginInstallDirectory() => _pluginsDirectory;

    public string GetPluginDirectory(string pluginId) =>
        PluginPathValidator.GetDirectChildPath(_pluginsDirectory, pluginId);

    public async Task<PluginInstallResult> InstallFromFileAsync(string packageFilePath, IProgress<double>? progress = null)
    {
        if (!File.Exists(packageFilePath))
        {
            return PluginInstallResult.Failed(
                PluginManagementErrorCode.NotFound,
                "Package file not found");
        }

        // .zip 扩展名校验统一由 InstallFromStreamAsync 处理（该方法是公共入口，需自行校验）
        await using var stream = new FileStream(
            packageFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await InstallFromStreamAsync(stream, Path.GetFileName(packageFilePath), progress);
    }

    public async Task<PluginInstallResult> InstallFromStreamAsync(Stream stream, string fileName, IProgress<double>? progress = null)
    {
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return PluginInstallResult.Failed(
                PluginManagementErrorCode.InvalidPackage,
                "Only .zip plugin packages are supported");
        }

        PluginInfo? pluginInfo = null;
        string? tempDir = null;

        try
        {
            tempDir = Path.Combine(Path.GetTempPath(), $"plugin_install_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entries = archive.Entries;
                if (entries.Count == 0)
                {
                    return PluginInstallResult.Failed(
                        PluginManagementErrorCode.InvalidPackage,
                        "Invalid plugin package: empty archive");
                }
                if (entries.Count > MaxPackageEntries)
                {
                    return PluginInstallResult.Failed(
                        PluginManagementErrorCode.InvalidPackage,
                        "Invalid plugin package: too many files");
                }
                if (entries.Sum(entry => entry.Length) > MaxPackageUncompressedBytes)
                {
                    return PluginInstallResult.Failed(
                        PluginManagementErrorCode.InvalidPackage,
                        "Invalid plugin package: package is too large");
                }

                var totalEntries = entries.Count;
                var processed = 0;

                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var destinationPath = Path.GetFullPath(Path.Combine(tempDir, entry.FullName));

                    if (!PluginPathValidator.IsWithinDirectory(tempDir, destinationPath))
                    {
                        return PluginInstallResult.Failed(
                            PluginManagementErrorCode.InvalidPackage,
                            "Security: Path traversal detected in package");
                    }

                    var dir = Path.GetDirectoryName(destinationPath);
                    if (dir != null) Directory.CreateDirectory(dir);

                    entry.ExtractToFile(destinationPath, overwrite: true);

                    processed++;
                    progress?.Report((double)processed / totalEntries * 0.5);
                }
            }

            pluginInfo = await ParsePluginManifestAsync(tempDir);

            if (pluginInfo == null)
            {
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.InvalidPackage,
                    "Invalid plugin package: no valid plugin.json manifest found");
            }

            var validationError = ValidatePluginPackage(tempDir, pluginInfo);
            if (validationError is not null)
            {
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.InvalidPackage,
                    validationError);
            }

            // 修复 #11：安装时即校验 MinPluginSdkVersion，避免安装后启动失败。
            if (!PluginLoader.IsPluginSdkCompatible(pluginInfo.MinPluginSdkVersion))
            {
                var required = string.IsNullOrWhiteSpace(pluginInfo.MinPluginSdkVersion)
                    ? "0.0.0" : pluginInfo.MinPluginSdkVersion!;
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.Conflict,
                    $"Plugin requires Plugin SDK >= {required}, but host provides " +
                    $"{PluginSdkContract.CurrentVersion}. Update the host application " +
                    "or contact the plugin author.");
            }

            var existingPlugin = _pluginLoader.GetPlugin(pluginInfo.PluginId);
            if (_readOnlyPluginIds.Contains(pluginInfo.PluginId) &&
                (existingPlugin is null ||
                 string.IsNullOrWhiteSpace(existingPlugin.InstallPath) ||
                 !PluginPathValidator.IsWithinDirectory(_pluginsDirectory, existingPlugin.InstallPath)))
            {
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.Conflict,
                    $"Plugin '{pluginInfo.PluginId}' is provided by an external read-only plugin directory.");
            }
            if (existingPlugin?.IsBuiltIn == true)
            {
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.Conflict,
                    "Built-in plugins cannot be replaced");
            }
            if (existingPlugin?.State == PluginState.PendingUninstall)
            {
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.Conflict,
                    $"Plugin '{pluginInfo.PluginId}' is pending uninstall.");
            }

            if (existingPlugin != null)
            {
                // The host has no cross-process hot-unload guarantee. Every replacement is
                // staged and applied only by the next desktop startup.
                return await ScheduleUpgradeAsync(tempDir, pluginInfo, existingPlugin, progress);
            }

            var installDir = GetPluginDirectory(pluginInfo.PluginId);
            Directory.CreateDirectory(installDir);

            var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            var totalFiles = files.Length;
            var copiedFiles = 0;

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(tempDir, file);
                var destPath = Path.GetFullPath(Path.Combine(installDir, relativePath));

                if (!PluginPathValidator.IsWithinDirectory(installDir, destPath))
                {
                    return PluginInstallResult.Failed(
                        PluginManagementErrorCode.InvalidPackage,
                        "Security: Path traversal detected during installation");
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                using var srcStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                using var dstStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
                await srcStream.CopyToAsync(dstStream, 65536);

                copiedFiles++;
                progress?.Report(0.5 + (double)copiedFiles / totalFiles * 0.5);
            }

            var mainAssembly = !string.IsNullOrEmpty(pluginInfo.AssemblyPath)
                ? Path.GetFullPath(Path.Combine(installDir, pluginInfo.AssemblyPath))
                : Directory.GetFiles(installDir, "*.dll", SearchOption.AllDirectories)
                    .FirstOrDefault(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));

            pluginInfo = pluginInfo.WithInstallInfo(installDir, mainAssembly ?? string.Empty, PluginState.Installed, DateTime.UtcNow);

            // 在注册到 PluginLoader 前为插件分配数据目录（Data/{PluginId}/）。
            // 目录创建由 PluginDataDirectoryProvider 负责，这里仅写入路径以供后续插件代码使用。
            var dataDir = PluginDataDirectoryProvider.ResolveHostDataRoot() is var hostRoot
                && !string.IsNullOrWhiteSpace(pluginInfo.PluginId)
                ? Path.Combine(hostRoot, pluginInfo.PluginId)
                : string.Empty;
            if (!string.IsNullOrEmpty(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                pluginInfo = pluginInfo.WithDataDirectory(dataDir);
            }

            _pluginLoader.RegisterPlugin(pluginInfo);

            PluginInstalled?.Invoke(this, pluginInfo);

            return new PluginInstallResult { Success = true, PluginInfo = pluginInfo };
        }
        catch (InvalidDataException ex)
        {
            return PluginInstallResult.Failed(
                PluginManagementErrorCode.InvalidPackage,
                $"Invalid plugin package: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return PluginInstallResult.Failed(
                PluginManagementErrorCode.PermissionDenied,
                $"Installation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return PluginInstallResult.Failed(
                PluginManagementErrorCode.HostError,
                $"Installation failed: {ex.Message}");
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    public Task<bool> UninstallAsync(string pluginId)
    {
        var pluginInfo = _pluginLoader.GetPlugin(pluginId);
        if (pluginInfo == null) return Task.FromResult(false);

        if (pluginInfo.IsBuiltIn) return Task.FromResult(false);
        if (_readOnlyPluginIds.Contains(pluginId) &&
            !PluginPathValidator.IsWithinDirectory(_pluginsDirectory, pluginInfo.InstallPath))
        {
            return Task.FromResult(false);
        }

        // 在标记卸载前先快照数据目录路径：
        // 卸载的实际清理发生在 ProcessPendingUninstalls（启动期扫描 PendingUninstall 目录）。
        // 标记时插件目录还在，数据目录路径直接由 manifest 推出。
        var dataDir = string.IsNullOrWhiteSpace(pluginInfo.DataDirectory)
            ? Path.Combine(PluginDataDirectoryProvider.ResolveHostDataRoot(), pluginId)
            : pluginInfo.DataDirectory;

        _pluginLoader.MarkForUninstall(pluginId);

        // 若 UI 已展示过该插件的数据目录，触发事件让前端提示"将一并删除数据"。
        PluginUninstalled?.Invoke(this, pluginInfo);
        return Task.FromResult(true);
    }

    public Task<bool> CancelUpgradeAsync(string pluginId)
    {
        // 实现依据：docs/Plugin-Upgrade-Evaluation.md（潜在问题 1：重启前用户取消升级）
        var cancelled = _pluginLoader.CancelPendingUpgrade(pluginId);
        return Task.FromResult(cancelled);
    }

    /// <summary>
    /// 调度升级：把新版本（已解压在 tempDir）搬到 plugins/.pending/{PluginId}.new/，
    /// 写入 .upgrade.json，并通过 PluginLoader.MarkPendingUpgrade 把插件状态置为 PendingUpgrade。
    ///
    /// 实现依据：docs/Plugin-Upgrade-Evaluation.md
    ///   - 潜在问题 7（并发安装与升级）：若 .pending/{PluginId}.upgrade.json 已存在，
    ///     直接覆盖 .new/ 并更新 .upgrade.json 版本号，保证原子性。
    /// </summary>
    private async Task<PluginInstallResult> ScheduleUpgradeAsync(
        string tempDir,
        PluginInfo newPluginInfo,
        PluginInfo existingPlugin,
        IProgress<double>? progress)
    {
        var pendingDir = Path.Combine(_pluginsDirectory, ".pending");
        Directory.CreateDirectory(pendingDir);

        PluginPathValidator.ValidatePluginId(newPluginInfo.PluginId);
        var newVersionDir = Path.Combine(pendingDir, $"{newPluginInfo.PluginId}.new");
        var upgradeJsonPath = Path.Combine(pendingDir, $"{newPluginInfo.PluginId}.upgrade.json");
        var previousPendingInfo = _pluginLoader.GetPendingUpgrade(newPluginInfo.PluginId);

        // 潜在问题 7：覆盖已存在的 .new/（用户连续点击两次升级）
        if (Directory.Exists(newVersionDir))
        {
            try { Directory.Delete(newVersionDir, true); }
            catch (Exception ex)
            {
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.HostError,
                    $"Cannot overwrite previous pending upgrade at '{newVersionDir}': {ex.Message}");
            }
        }

        // 把整个 tempDir 移动到 .pending/{PluginId}.new/（原子性高于复制）
        try
        {
            Directory.Move(tempDir, newVersionDir);
            // tempDir 已被 Move 走，finally 块的 Directory.Exists 检查会安全跳过
        }
        catch (Exception ex)
        {
            // 跨卷场景 Move 可能失败，回退到复制+删除
            try
            {
                PluginUtilities.CopyDirectory(tempDir, newVersionDir);
            }
            catch (Exception copyEx)
            {
                return PluginInstallResult.Failed(
                    PluginManagementErrorCode.HostError,
                    $"Failed to stage new version for upgrade (move: {ex.Message}; copy: {copyEx.Message})");
            }
        }

        progress?.Report(1.0);

        // 写 .upgrade.json
        var upgradeInfo = new PendingUpgradeInfo
        {
            PluginId = newPluginInfo.PluginId,
            NewVersion = newPluginInfo.Version,
            ScheduledAt = DateTime.UtcNow,
            PreserveState = true,
            // 记录旧状态：Loaded 时迁移后应回到 Installed（不能直接 Loaded，必须重新加载）；
            // 其他状态按字面保留（仅 Disabled/Installed 合法）。
            OldStateToPreserve = previousPendingInfo?.OldStateToPreserve ?? existingPlugin.State.ToString(),
            NewVersionPath = newVersionDir
        };

        try
        {
            var json = JsonSerializer.Serialize(upgradeInfo, PluginUtilities.JsonOptions);
            await File.WriteAllTextAsync(upgradeJsonPath, json);
        }
        catch (Exception ex)
        {
            // 回滚：删除已就位的新版本目录
            try { Directory.Delete(newVersionDir, true); } catch { }
            return PluginInstallResult.Failed(
                PluginManagementErrorCode.HostError,
                $"Failed to write upgrade marker: {ex.Message}");
        }

        // 让 PluginLoader 把内存中的插件状态改为 PendingUpgrade 并保存 manifest
        _pluginLoader.MarkPendingUpgrade(newPluginInfo.PluginId, upgradeInfo);

        var scheduledInfo = _pluginLoader.GetPlugin(newPluginInfo.PluginId);
        if (scheduledInfo != null)
        {
            PluginUpgradeScheduled?.Invoke(this, scheduledInfo);
        }

        return new PluginInstallResult
        {
            Success = true,
            PluginInfo = scheduledInfo ?? newPluginInfo
        };
    }

    public Task<bool> EnablePluginAsync(string pluginId)
    {
        return Task.FromResult(_pluginLoader.EnablePlugin(pluginId));
    }

    public Task<bool> DisablePluginAsync(string pluginId)
    {
        return Task.FromResult(_pluginLoader.DisablePlugin(pluginId));
    }

    private async Task<PluginInfo?> ParsePluginManifestAsync(string directory)
    {
        var manifestFile = Path.Combine(directory, "plugin.json");
        if (File.Exists(manifestFile))
        {
            var json = await File.ReadAllTextAsync(manifestFile);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginUtilities.JsonOptions);
            if (manifest != null)
            {
                return PluginManifestMapper.FromManifest(
                    manifest,
                    manifest.Assembly ?? string.Empty,
                    hasMetadata: !string.IsNullOrWhiteSpace(manifest.PluginId));
            }
        }

        var dllFiles = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
        foreach (var dll in dllFiles)
        {
            try
            {
                var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(dll);
                return PluginManifestMapper.FromAssembly(
                    assemblyName,
                    Path.GetRelativePath(directory, dll),
                    pluginIdFallback: Guid.NewGuid().ToString("N"),
                    name: assemblyName.Name ?? "Unknown",
                    versionFallback: "1.0.0",
                    author: "Unknown");
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? ValidatePluginPackage(string rootDirectory, PluginInfo pluginInfo)
    {
        try
        {
            PluginPathValidator.ValidatePluginId(pluginInfo.PluginId);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        if (string.IsNullOrWhiteSpace(pluginInfo.Name))
            return "Invalid plugin package: name is required";
        if (string.IsNullOrWhiteSpace(pluginInfo.Version))
            return "Invalid plugin package: version is required";
        if (string.IsNullOrWhiteSpace(pluginInfo.AssemblyPath))
            return "Invalid plugin package: assembly is required";
        if (Path.IsPathRooted(pluginInfo.AssemblyPath))
            return "Invalid plugin package: assembly path must be relative";
        if (!pluginInfo.AssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return "Invalid plugin package: assembly must be a .dll file";

        var assemblyPath = Path.GetFullPath(Path.Combine(rootDirectory, pluginInfo.AssemblyPath));
        if (!PluginPathValidator.IsWithinDirectory(rootDirectory, assemblyPath))
            return "Security: assembly path traversal detected in manifest";
        if (!File.Exists(assemblyPath))
            return "Invalid plugin package: assembly file not found";

        try
        {
            _ = System.Reflection.AssemblyName.GetAssemblyName(assemblyPath);
        }
        catch (Exception ex)
        {
            return $"Invalid plugin package: assembly is not loadable ({ex.Message})";
        }

        return null;
    }
}
