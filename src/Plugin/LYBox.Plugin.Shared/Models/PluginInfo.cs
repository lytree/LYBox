namespace LYBox.Plugin.Shared.Models;

public record PluginInfo
{
    public string PluginId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> Dependencies { get; init; } = [];

    /// <summary>
    /// Additional assembly name patterns declared in the plugin manifest that should be
    /// forwarded to the host's default AssemblyLoadContext. Merged with shared-assemblies.txt
    /// at runtime by PluginLoadContext.
    /// </summary>
    public List<string> SharedAssemblies { get; init; } = [];

    public string InstallPath { get; init; } = string.Empty;
    public string AssemblyPath { get; init; } = string.Empty;
    public PluginState State { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? InstallTime { get; init; }
    public bool IsBuiltIn { get; init; }
    public bool HasMetadata { get; init; }

    /// <summary>
    /// 该插件所需的最低 Plugin SDK 契约版本。null 或空表示无约束（向后兼容旧插件）。
    /// 由 plugin.json manifest 读取，运行时与 PluginSdkContract.CurrentVersion 比对。
    /// </summary>
    public string? MinPluginSdkVersion { get; init; }

    /// <summary>
    /// S1 清单 v2：插件类别。缺省 "Avalonia"（向后兼容）。可能值："Avalonia" | "Web"。
    /// 宿主在清单加载阶段（创建 ALC 之前）即可据此区分 Web 插件（P4）。
    /// </summary>
    public string Kind { get; init; } = "Avalonia";

    /// <summary>
    /// S1 清单 v2：Web 插件前端资源描述。仅当 <see cref="Kind"/> 为 "Web" 时有效。
    /// </summary>
    public WebDescriptor? Web { get; init; }

    /// <summary>
    /// 待升级场景：用户已调度升级但尚未重启时，此字段记录新版本号，便于 UI 显示。
    /// 真相源是 plugins/.pending/{PluginId}.upgrade.json 的 NewVersion 字段。
    /// 仅当 State == PendingUpgrade 时有效。
    /// 实现依据：docs/Plugin-Upgrade-Evaluation.md
    /// </summary>
    public string? PendingUpgradeVersion { get; init; }

    /// <summary>
    /// 升级场景：上一次已安装版本的 schema 版本号（来自旧 manifest）。
    /// 仅当本次是升级（而非全新安装）且旧 manifest 声明过 <c>SchemaVersion</c> 时有效。
    /// 宿主在 <see cref="PluginLoader.RegisterAllPluginsAsync"/> 透传给插件，
    /// 由插件 <c>RegisterAsync</c> 中对比 <see cref="CurrentSchemaVersion"/> 自决定是否迁移。
    /// </summary>
    public string? PreviousSchemaVersion { get; init; }

    /// <summary>
    /// 当前安装版本的 schema 版本号（来自新 manifest，与 <see cref="PreviousSchemaVersion"/> 比对）。
    /// </summary>
    public string? CurrentSchemaVersion { get; init; }

    /// <summary>
    /// 当前安装版本的 manifest 是否声明 <c>RequiresDataMigration=true</c>。
    /// 仅作为提示，宿主不在此处自动执行迁移（由插件自行决定策略）。
    /// </summary>
    public bool RequiresDataMigration { get; init; }

    /// <summary>
    /// 该插件的数据目录绝对路径（由宿主 <see cref="LYBox.Plugin.Shared.Services.IPluginDataDirectoryProvider"/> 解析）。
    /// 插件 <c>RegisterAsync</c> 中可直接使用该字段作为持久化根目录。
    /// </summary>
    public string DataDirectory { get; init; } = string.Empty;

    public PluginInfo WithState(PluginState state, string? errorMessage = null) =>
        this with { State = state, ErrorMessage = errorMessage };

    public PluginInfo WithInstallPath(string installPath) =>
        this with { InstallPath = installPath };

    public PluginInfo WithAssemblyPath(string assemblyPath) =>
        this with { AssemblyPath = assemblyPath };

    public PluginInfo WithInstallInfo(string installPath, string assemblyPath, PluginState state, DateTime? installTime) =>
        this with { InstallPath = installPath, AssemblyPath = assemblyPath, State = state, InstallTime = installTime };

    public PluginInfo WithMetadata(bool hasMetadata) =>
        this with { HasMetadata = hasMetadata };

    public PluginInfo WithMinPluginSdkVersion(string? minPluginSdkVersion) =>
        this with { MinPluginSdkVersion = minPluginSdkVersion };

    public PluginInfo WithPendingUpgrade(string? newVersion, string? errorMessage = null) =>
        this with { PendingUpgradeVersion = newVersion, ErrorMessage = errorMessage };

    public PluginInfo WithDataDirectory(string dataDirectory) =>
        this with { DataDirectory = dataDirectory };

    public PluginInfo WithUpgradeAwareness(
        string? previousSchemaVersion,
        string? currentSchemaVersion,
        bool requiresDataMigration) =>
        this with
        {
            PreviousSchemaVersion = previousSchemaVersion,
            CurrentSchemaVersion = currentSchemaVersion,
            RequiresDataMigration = requiresDataMigration
        };
}
