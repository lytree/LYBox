using System.Runtime.InteropServices;

namespace LYBox.Plugin.Shared;

/// <summary>
/// 插件支持的操作系统平台判定工具。
/// 与 <see cref="IPluginMetadata.SupportedPlatforms"/>、<c>plugin.json</c> 中的
/// <c>supportedPlatforms</c> 字段语义保持一致。
/// </summary>
public static class PluginPlatform
{
    /// <summary>windows</summary>
    public const string Windows = "windows";

    /// <summary>linux</summary>
    public const string Linux = "linux";

    /// <summary>macOS（osx）</summary>
    public const string Osx = "osx";

    /// <summary>通配项：声明此值表示无平台约束。</summary>
    public const string Any = "*";

    /// <summary>
    /// 当前进程所在桌面操作系统对应的 LYBox 平台标识
    /// （"windows" / "linux" / "osx"）。无法识别时返回 "*"。
    /// </summary>
    public static string CurrentOS
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return Linux;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return Osx;
            return Any;
        }
    }

    /// <summary>
    /// 给定声明的平台列表（null / 空 / 包含 "*" 视为无约束），
    /// 返回当前 OS 是否被支持。
    /// </summary>
    public static bool IsCurrentOSSupported(IEnumerable<string?>? supportedPlatforms)
    {
        if (supportedPlatforms is null) return true;

        var current = CurrentOS;
        var sawAny = false;
        var sawConcrete = false;
        foreach (var token in supportedPlatforms)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            var t = token.Trim();
            if (string.Equals(t, Any, StringComparison.OrdinalIgnoreCase)) sawAny = true;
            else if (string.Equals(t, current, StringComparison.OrdinalIgnoreCase)) return true;
            else sawConcrete = true;
        }
        // 仅声明了 "*" 或仅空白 -> 无约束；其余都未命中当前 OS -> 不支持
        return sawAny || !sawConcrete;
    }
}
