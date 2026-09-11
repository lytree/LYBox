namespace LYBox.Plugin.Shared.Models;

public class PluginManifest
{
    public string? PluginId { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Assembly { get; set; }
    public List<string>? Dependencies { get; set; }

    /// <summary>
    /// Additional assembly name patterns that this plugin declares as shared
    /// (forwarded to the host's default AssemblyLoadContext). Each entry is either
    /// an exact assembly name or a prefix pattern ending with '*'.
    /// These are merged with the default shared-assemblies.txt list at runtime.
    /// </summary>
    public List<string>? SharedAssemblies { get; set; }

    public string? State { get; set; }
    public DateTime? InstallTime { get; set; }
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 该插件所需的最低 Plugin SDK 契约版本。
    /// 缺省时视为 "0.0.0"（向后兼容未声明版本要求的旧插件）。
    /// 主体程序加载时与 PluginSdkContract.CurrentVersion 比对，不满足则拒绝加载。
    /// </summary>
    public string? MinPluginSdkVersion { get; set; }

    /// <summary>
    /// 插件自身数据 schema 版本号（仅由插件作者在 plugin.json 中维护）。
    /// 当插件的本地数据库/数据结构发生不兼容变更时应递增。
    /// 宿主不参与校验，仅在升级时透传给插件 <see cref="PluginInfo.PreviousSchemaVersion"/>，
    /// 由插件 <c>RegisterAsync</c> 自行判断是否需要迁移本地数据。
    /// 缺省时视为 "0"，表示从未记录过版本。
    /// </summary>
    public string? SchemaVersion { get; set; }

    /// <summary>
    /// 该插件是否需要数据迁移。
    /// 仅在升级场景下有意义：true 表示从低版本升级到当前版本时，
    /// 插件需要执行数据迁移（宿主在 <c>RegisterAsync</c> 完成后调用其迁移钩子）。
    /// </summary>
    public bool RequiresDataMigration { get; set; }

    /// <summary>
    /// S1 清单 v2：插件类别。缺省解析为 "Avalonia"（向后兼容）。
    /// 可能值："Avalonia" | "Web"。
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// S1 清单 v2：仅当 <see cref="Kind"/> 为 "Web" 时存在，描述 Web 插件前端资源位置。
    /// </summary>
    public WebDescriptor? Web { get; set; }
}

/// <summary>
/// Web 插件前端资源描述。仅当清单 <see cref="PluginManifest.Kind"/> 为 "Web" 时有效。
/// </summary>
public class WebDescriptor
{
    /// <summary>前端资源根目录名称（相对 <see cref="PluginInfo.InstallPath"/>），默认 "wwwroot"。</summary>
    public string? Wwwroot { get; set; }

    /// <summary>入口页面文件名，默认 "index.html"。</summary>
    public string? EntryPage { get; set; }
}
