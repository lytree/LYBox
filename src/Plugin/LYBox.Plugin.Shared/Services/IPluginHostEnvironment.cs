namespace LYBox.Plugin.Shared.Services;

using Microsoft.Extensions.Logging;

/// <summary>
/// 宿主运行时环境信息。插件通过该接口统一获取宿主侧的路径 / 进程 / 日志等上下文，
/// 避免在插件内部硬编码 <c>AppContext.BaseDirectory</c> / 环境变量 / 回退路径等"猜测宿主布局"的逻辑。
///
/// 设计目标：
/// <list type="bullet">
/// <item>单一来源：所有路径由宿主解析并暴露，插件只读不写。</item>
/// <item>可测试：插件可通过注入自定义实现来跑单元测试。</item>
/// <item>可观测：宿主变更路径布局（例如日志目录改名）不需要修改插件。</item>
/// <item>统一日志：插件通过 <see cref="LoggerFactory"/> 创建的 <see cref="ILogger"/> 自动落到宿主
///       日志（控制台 + 滚动文件），由宿主集中管理。</item>
/// </list>
///
/// 使用方式：
/// <list type="number">
/// <item>宿主在 DI 容器里注册 <see cref="IPluginHostEnvironment"/> 的实现。</item>
/// <item>插件在 <c>InitializeAsync</c> / <c>RegisterAsync</c> 中从 <c>IServiceProvider</c>
///       解析该接口，并把值注入到自己的静态路径解析器（如 <c>TdlPaths.Initialize(env)</c>）。</item>
/// <item>插件后续读取路径一律通过注入的实例，禁止再调 <c>AppContext.BaseDirectory</c>
///       或读 <c>LYBOX_DATA_ROOT</c> 环境变量。</item>
/// <item>插件创建 logger 一律通过 <see cref="LoggerFactory"/>.CreateLogger&lt;T&gt;()，
///       禁止再 new 自己的日志文件 / 调 Console.WriteLine 输出"日志性质"的内容。</item>
/// </list>
/// </summary>
public interface IPluginHostEnvironment
{
    /// <summary>宿主根数据目录（含 appdata.db、各插件子目录等）。</summary>
    string HostDataRoot { get; }

    /// <summary>宿主可执行文件所在目录（启动器根目录，<c>AppContext.BaseDirectory</c> 等价）。</summary>
    string AppBaseDirectory { get; }

    /// <summary>宿主日志目录（滚动日志落盘目录）。</summary>
    string LogsDirectory { get; }

    /// <summary>宿主可执行文件版本（与 <c>HostVersion</c> 同步）。</summary>
    string AppVersion { get; }

    /// <summary>是否运行在便携 / CI 模式（数据根目录被环境变量覆盖时为 true）。</summary>
    bool IsPortableMode { get; }

    /// <summary>
    /// 宿主 <see cref="ILoggerFactory"/>：插件创建的所有 <see cref="ILogger"/> 都通过它产出，
    /// 日志会同时落到宿主控制台与滚动日志文件中（统一格式 / 统一滚动策略）。
    /// </summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// 当前插件的日志目录：<c>{LogsDirectory}/plugins/{PluginId}/</c>。
    /// 每个插件独立一份滚动日志文件，便于按插件归档 / 排查。
    /// </summary>
    string PluginLogsDirectory { get; }
}