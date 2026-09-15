namespace LYBox.Plugin.Shared;

public interface IPluginMetadata
{
    /// <summary>
    /// 插件名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 插件版本
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 插件作者
    /// </summary>
    string Author { get; }

    /// <summary>
    /// 插件描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 插件唯一标识
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// 该插件所需的最低 Plugin SDK 契约版本。
    /// 默认 "0.0.0" 表示无约束（向后兼容未声明的旧插件）。
    /// 主体程序加载时与 PluginSdkContract.CurrentVersion 比对，
    /// 若插件要求版本高于当前 SDK 版本则拒绝加载。
    /// </summary>
    string MinPluginSdkVersion => "0.0.0";

    /// <summary>
    /// 该插件支持的操作系统平台列表。
    /// 平台标识（大小写不敏感）：
    ///   - "windows" — Microsoft Windows
    ///   - "linux"   — Linux 桌面发行版
    ///   - "osx"     — Apple macOS
    /// 返回 null / 空数组 / 包含通配项 "*" 时表示无平台约束（向后兼容）。
    /// 主体程序加载时与当前进程 <see cref="System.Runtime.InteropServices.RuntimeInformation"/>
    /// 推导的宿主 OS 比对，若当前 OS 不在列表中则跳过加载并标记为错误。
    /// </summary>
    string[]? SupportedPlatforms => null;

}


