namespace LYBox.Plugin.Shared.Services;

/// <summary>
/// LYBox 运行期平台 / 宿主能力画像。
/// <para>
/// 集中暴露"运行在哪个操作系统 / 是否便携 / 用户目录在哪 / 默认下载目录在哪"等
/// 跨平台差异,插件通过 DI 解析本接口后直接读取对应属性,
/// 避免散落在各处的 <c>Environment.GetFolderPath(...)</c> / <c>OperatingSystem.IsWindows()</c> 判断。
/// </para>
/// <para>
/// 实现方:宿主侧 <c>LYBox.Layout.Core</c> 提供单例实现。
/// 插件侧:在 <c>RegisterAsync</c> 中通过 <c>ServiceLocator.GetService&lt;IRuntimeProfile&gt;()</c> 取得。
/// </para>
/// </summary>
public interface IRuntimeProfile
{
    /// <summary>当前运行平台。</summary>
    HostPlatform Platform { get; }

    /// <summary>
    /// 是否便携 / CI 模式(数据根目录被 <c>LYBOX_DATA_ROOT</c> 环境变量覆盖)。
    /// 与 <see cref="IPluginHostEnvironment.IsPortableMode"/> 同语义;此处独立暴露便于插件在
    /// 无 <see cref="IPluginHostEnvironment"/> 注入的场景下也能读取。
    /// </summary>
    bool IsPortableMode { get; }

    /// <summary>
    /// 是否自包含 / 单文件发布(true)或框架依赖部署(false)。
    /// 自包含模式下用户目录通常不能写,需要走宿主 <c>DataRoot</c>。
    /// </summary>
    bool IsSelfContained { get; }

    /// <summary>
    /// 当前用户主目录(HOME / UserProfile),跨平台一致。
    /// 典型值:Windows <c>%USERPROFILE%</c>;Linux/macOS <c>$HOME</c>。
    /// </summary>
    string UserHomeDirectory { get; }

    /// <summary>
    /// 平台"我的文档"目录。Windows = <c>MyDocuments</c>;Linux/macOS 通常为 <c>UserHome/Documents</c>(XDG)。
    /// </summary>
    string DefaultDocumentsDirectory { get; }

    /// <summary>
    /// 平台"下载"目录。Windows = KnownFolders.Downloads;Linux 走 <c>XDG_DOWNLOAD_DIR</c>;macOS <c>UserHome/Downloads</c>。
    /// </summary>
    string DefaultDownloadsDirectory { get; }
}

/// <summary>运行平台枚举(用于 <see cref="IRuntimeProfile.Platform"/>)。</summary>
public enum HostPlatform
{
    Unknown = 0,
    Windows = 1,
    Linux = 2,
    MacOs = 3,
}