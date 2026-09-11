using System.Reflection;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;

namespace LYBox.Layout.Core.Services;

/// <summary>
/// 把 <see cref="PluginManifest"/> / <see cref="AssemblyName"/> 统一映射为 <see cref="PluginInfo"/>。
/// 清单发现（<see cref="PluginInventoryCatalog"/>）与安装包解析（<see cref="PluginInstallationManager"/>）
/// 之前各自内联了相同的字段默认化逻辑，此处收敛以避免规则漂移。
/// </summary>
internal static class PluginManifestMapper
{
    public static PluginInfo FromManifest(
        PluginManifest manifest,
        string assemblyPath,
        bool hasMetadata,
        string? installPath = null,
        PluginState? state = null,
        bool isBuiltIn = false)
    {
        var pluginId = string.IsNullOrWhiteSpace(manifest.PluginId) ? string.Empty : manifest.PluginId;
        return new PluginInfo
        {
            PluginId = pluginId,
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? pluginId : manifest.Name,
            Version = manifest.Version ?? "1.0.0",
            Author = manifest.Author ?? string.Empty,
            Description = manifest.Description ?? string.Empty,
            Dependencies = manifest.Dependencies ?? [],
            SharedAssemblies = manifest.SharedAssemblies ?? [],
            InstallPath = installPath ?? string.Empty,
            AssemblyPath = assemblyPath,
            InstallTime = manifest.InstallTime,
            IsBuiltIn = isBuiltIn,
            HasMetadata = hasMetadata,
            MinPluginSdkVersion = manifest.MinPluginSdkVersion,
            Kind = string.IsNullOrWhiteSpace(manifest.Kind) ? "Avalonia" : manifest.Kind,
            Web = manifest.Web,
            State = state ?? PluginState.Installed,
            CurrentSchemaVersion = string.IsNullOrWhiteSpace(manifest.SchemaVersion) ? "0" : manifest.SchemaVersion,
            RequiresDataMigration = manifest.RequiresDataMigration
        };
    }

    public static PluginInfo FromAssembly(
        AssemblyName assemblyName,
        string assemblyPath,
        string? pluginIdFallback = null,
        string? name = null,
        string? installPath = null,
        string versionFallback = "0.0.0",
        string? author = null)
    {
        var pluginId = assemblyName.Name ?? pluginIdFallback ?? string.Empty;
        return new PluginInfo
        {
            PluginId = pluginId,
            Name = name ?? (assemblyName.Name ?? "Unknown"),
            Version = assemblyName.Version?.ToString() ?? versionFallback,
            Author = author ?? string.Empty,
            Description = string.Empty,
            InstallPath = installPath ?? string.Empty,
            AssemblyPath = assemblyPath,
            State = PluginState.Installed,
            HasMetadata = false
        };
    }
}