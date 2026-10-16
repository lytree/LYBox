using System.Text.Encodings.Web;
using System.Text.Json;

namespace LYBox.Layout.Core.Services;

/// <summary>
/// 插件系统共享工具：统一 JsonSerializerOptions 与文件系统操作，消除 PluginLoader 与 PluginInstallationManager 间的重复代码。
/// </summary>
internal static class PluginUtilities
{
    /// <summary>
    /// 统一的 JSON 序列化选项：camelCase 命名 + 缩进输出，用于 plugin.json 和 .upgrade.json。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 回写 <c>plugin.json</c> 专用的写入选项。
    /// <para>
    /// 相比 <see cref="JsonOptions"/> 仅多一项宽松转义：默认编码器会把非 ASCII 字符写成
    /// <c>\uXXXX</c> 转义序列，导致中文插件名 / 描述在每次宿主回写后变成乱码般的转义串，
    /// 既破坏人工可读性又污染版本 diff。清单是本地文件、不参与 HTML 上下文，故可安全放宽。
    /// </para>
    /// </summary>
    public static readonly JsonSerializerOptions ManifestWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 递归复制目录及其所有子目录和文件。
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.GetFullPath(Path.Combine(destDir, relative));
            var dir = Path.GetDirectoryName(dest);
            if (dir != null) Directory.CreateDirectory(dir);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
