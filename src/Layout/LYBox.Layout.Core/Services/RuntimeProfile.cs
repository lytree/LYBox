using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Services;

namespace LYBox.Layout.Core.Services;

/// <summary>
/// 宿主侧 <see cref="IRuntimeProfile"/> 默认实现。
/// <para>
/// 跨平台值采用以下策略:
/// <list type="bullet">
/// <item><see cref="UserHomeDirectory"/>:Windows 走 <see cref="Environment.SpecialFolder.UserProfile"/>;
///       Linux/macOS 走 <c>$HOME</c>(环境变量优先,<c>getpwuid</c> 退化路径)。</item>
/// <item><see cref="DefaultDocumentsDirectory"/>:Windows = <see cref="Environment.SpecialFolder.MyDocuments"/>;
///       Linux/macOS = <c>{UserHome}/Documents</c>(遵循 XDG)。</item>
/// <item><see cref="DefaultDownloadsDirectory"/>:Windows = <see cref="Environment.SpecialFolder.UserProfile"/> + "Downloads";
///       Linux 优先 <c>$XDG_DOWNLOAD_DIR</c>,否则 <c>{UserHome}/Downloads</c>;
///       macOS = <c>{UserHome}/Downloads</c>。</item>
/// <item><see cref="IsPortableMode"/> = <c>LYBOX_DATA_ROOT</c> 环境变量是否被设置(与 <see cref="IPluginHostEnvironment.IsPortableMode"/> 一致)。</item>
/// </list>
/// </para>
/// </summary>
public sealed class RuntimeProfile : IRuntimeProfile
{
    public HostPlatform Platform
    {
        get
        {
            if (OperatingSystem.IsWindows()) return HostPlatform.Windows;
            if (OperatingSystem.IsMacOS()) return HostPlatform.MacOs;
            if (OperatingSystem.IsLinux()) return HostPlatform.Linux;
            return HostPlatform.Unknown;
        }
    }

    public bool IsPortableMode
        => !string.IsNullOrWhiteSpace(LYBoxEnv.Get(LYBoxEnv.DataRootKey));

    public bool IsSelfContained =>
        // AppContext.GetData("Microsoft.DotNet.AppContext.BootstrapContext") 在自包含发布时存在;
        // 也可由宿主在 Program.cs 早期写入一个环境变量 / 属性。为最小依赖,采用以下启发式:
        //   - 若 AppContext.BaseDirectory 与用户主目录不在同一盘符 / 不在同一文件系统,
        //     不太准确;直接看 hostfxr 是否是单文件模式。
        AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY") is string baseDir
        && !string.IsNullOrEmpty(baseDir)
        && baseDir.IndexOf(Path.DirectorySeparatorChar + "dotnet" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) < 0;

    public string UserHomeDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Linux / macOS: 优先 HOME,退化到 getpwuid(getuid())->pw_dir
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home)) return home;

            try
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "_lybox-home-fallback");
            }
        }
    }

    public string DefaultDocumentsDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var xdg = Environment.GetEnvironmentVariable("XDG_DOCUMENTS_DIR");
            if (!string.IsNullOrEmpty(xdg)) return xdg;
            return Path.Combine(UserHomeDirectory, "Documents");
        }
    }

    public string DefaultDownloadsDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows 没有内置 Downloads 常量,采用约定 {UserProfile}\Downloads。
                return Path.Combine(UserHomeDirectory, "Downloads");
            }

            if (OperatingSystem.IsLinux())
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
                if (!string.IsNullOrEmpty(xdg)) return xdg;
            }

            return Path.Combine(UserHomeDirectory, "Downloads");
        }
    }
}