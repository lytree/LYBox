using LYBox.Plugin.Shared.Services;

namespace LYBox.Layout.Core.Services;

/// <summary>
/// 宿主侧 <see cref="IPluginDataDirectoryProvider"/> 实现。
/// 根目录默认位于：
/// <list type="bullet">
/// <item>Windows: <c>%LOCALAPPDATA%/LYBox</c></item>
/// <item>Linux/macOS: <c>~/.config/LYBox</c>（遵循 XDG Base Directory）</item>
/// </list>
/// 可通过 <c>LYBOX_DATA_ROOT</c> 环境变量覆盖（CI / 便携模式）。
/// </summary>
public sealed class PluginDataDirectoryProvider : IPluginDataDirectoryProvider
{
    /// <summary>允许通过环境变量覆盖数据根目录，便于便携/CI 场景。</summary>
    public const string DataRootEnvironmentVariable = "LYBOX_DATA_ROOT";

    public string HostDataRoot { get; }

    public PluginDataDirectoryProvider()
    {
        HostDataRoot = ResolveHostDataRoot();
        Directory.CreateDirectory(HostDataRoot);
    }

    /// <summary>
    /// 计算宿主数据根目录（不创建）。供 db factory 等需要在 DI 容器构建之前使用。
    /// </summary>
    public static string ResolveHostDataRoot()
    {
        var envOverride = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(envOverride)
            ? Path.GetFullPath(envOverride)
            : Path.Combine(
                Environment.GetFolderPath(
                    OperatingSystem.IsWindows()
                        ? Environment.SpecialFolder.LocalApplicationData
                        : Environment.SpecialFolder.ApplicationData),
                "LYBox");
    }

    public string GetPluginDataDirectory(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var dir = Path.Combine(HostDataRoot, pluginId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetSubDirectory(string pluginId, string subPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subPath);
        var dir = Path.Combine(GetPluginDataDirectory(pluginId), subPath);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
