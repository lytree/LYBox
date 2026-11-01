using System.Reflection;

namespace LYBox.Layout.Core.Services;

/// <summary>
/// 宿主版本读取：取 <see cref="AssemblyInformationalVersionAttribute"/> 并去掉 SourceLink 追加的 +commit 后缀。
/// </summary>
public static class HostVersion
{
    /// <summary>形如 <c>2.4.0-beta.2</c> 的语义版本；不可用时返回 <c>null</c>。</summary>
    public static string? Current { get; } = Resolve();

    /// <summary>形如 <c>v2.4.0-beta.2</c> 的展示文本；不可用时返回 <c>null</c>。</summary>
    public static string? DisplayText { get; } = Current is null ? null : "v" + Current;

    private static string? Resolve()
    {
        var informational = (Assembly.GetEntryAssembly() ?? typeof(HostVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return null;

        var plusIndex = informational.IndexOf('+');
        return plusIndex > 0 ? informational[..plusIndex] : informational;
    }
}
