using System.Text.Json.Nodes;
using LYBox.Layout.Core.Services;
using LYBox.Plugin.Shared.Models;
using TUnit.Assertions;
using TUnit.Core;

namespace LYBox.Tests;

/// <summary>
/// <c>plugin.json</c> 回写语义的回归测试。
/// <para>
/// 宿主只允许更新自己拥有的运行期字段（state / installTime / isBuiltIn）；
/// 插件作者声明并由构建期生成的字段（pluginId / kind / web / assembly / sharedAssemblies /
/// schemaVersion / requiresDataMigration / 未知扩展字段）必须原样保留。
/// </para>
/// <para>
/// 历史缺陷：宿主曾按内存 <see cref="PluginInfo"/> 整份重建清单。当加载路径退化
/// （例如开发期 <c>AVALONIA_EXTRA_PLUGINS_PATH</c> 只能按程序集名推断）时，
/// 降级快照会被回写成"权威清单"，把 <c>kind</c> 抹成 Avalonia、<c>web</c> 抹成 null、
/// <c>pluginId</c> 改成程序集名，直接摧毁插件的 Web 注册声明。
/// </para>
/// </summary>
public class PluginManifestPersistenceTests
{
    private const string PluginId = "manifest.persistence.test";
    private const string AuthorDeclaredChineseName = "字帖生成器";

    /// <summary>模拟构建期生成的完整清单：含中文、Web 描述与多个作者声明字段。</summary>
    private const string AuthorManifest = """
    {
      "pluginId": "manifest.persistence.test",
      "name": "字帖生成器",
      "version": "1.0.0-preview.1",
      "author": "lytree",
      "description": "离线字帖生成器",
      "assembly": "LYBox.Plugin.Zitie.dll",
      "dependencies": [],
      "sharedAssemblies": ["Some.Vendor.*"],
      "minPluginSdkVersion": "2.0.0",
      "kind": "Web",
      "web": {
        "wwwroot": "wwwroot",
        "entryPage": "index.html"
      },
      "supportedPlatforms": ["*"],
      "schemaVersion": "3",
      "requiresDataMigration": true,
      "authorExtension": { "custom": [1, 2, 3] },
      "state": "Installed"
    }
    """;

    [Test]
    public async Task SaveManifest_OnlyUpdatesRuntimeFields_AndPreservesAuthorDeclarations()
    {
        using var workspace = new ManifestWorkspace();
        var manifestPath = await workspace.WriteManifestAsync(PluginId, AuthorManifest);

        using var loader = new PluginLoader(workspace.PluginsDirectory);
        loader.MarkPluginError(PluginId, "boom");

        var text = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonNode.Parse(text)!.AsObject();

        // 作者声明：必须逐项保持
        await Assert.That(manifest["pluginId"]!.GetValue<string>()).IsEqualTo(PluginId);
        await Assert.That(manifest["name"]!.GetValue<string>()).IsEqualTo(AuthorDeclaredChineseName);
        await Assert.That(manifest["kind"]!.GetValue<string>()).IsEqualTo("Web");
        await Assert.That(manifest["web"]!["wwwroot"]!.GetValue<string>()).IsEqualTo("wwwroot");
        await Assert.That(manifest["web"]!["entryPage"]!.GetValue<string>()).IsEqualTo("index.html");
        await Assert.That(manifest["assembly"]!.GetValue<string>()).IsEqualTo("LYBox.Plugin.Zitie.dll");
        await Assert.That(manifest["sharedAssemblies"]![0]!.GetValue<string>()).IsEqualTo("Some.Vendor.*");
        await Assert.That(manifest["minPluginSdkVersion"]!.GetValue<string>()).IsEqualTo("2.0.0");
        await Assert.That(manifest["schemaVersion"]!.GetValue<string>()).IsEqualTo("3");
        await Assert.That(manifest["requiresDataMigration"]!.GetValue<bool>()).IsTrue();
        await Assert.That(manifest["supportedPlatforms"]![0]!.GetValue<string>()).IsEqualTo("*");
        // 未知扩展字段不应被序列化管道丢弃
        await Assert.That(manifest["authorExtension"]!["custom"]![2]!.GetValue<int>()).IsEqualTo(3);

        // 运行期字段：应被更新
        await Assert.That(manifest["state"]!.GetValue<string>()).IsEqualTo(nameof(PluginState.Error));

        // 中文不得被转义为 \uXXXX（清单需保持人工可读、可 diff）
        await Assert.That(text.Contains(AuthorDeclaredChineseName, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SaveManifest_RebuildsWhenFileMissing_WithoutGuidanceFromDisk()
    {
        using var workspace = new ManifestWorkspace();
        var manifestPath = await workspace.WriteManifestAsync(PluginId, AuthorManifest);

        using var loader = new PluginLoader(workspace.PluginsDirectory);
        // 清单在加载之后消失：此时内存快照是唯一信息源，走兜底重建
        File.Delete(manifestPath);

        loader.MarkPluginError(PluginId, "boom");

        await Assert.That(File.Exists(manifestPath)).IsTrue();
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();

        await Assert.That(manifest["pluginId"]!.GetValue<string>()).IsEqualTo(PluginId);
        await Assert.That(manifest["kind"]!.GetValue<string>()).IsEqualTo("Web");
        await Assert.That(manifest["web"]!["entryPage"]!.GetValue<string>()).IsEqualTo("index.html");
        await Assert.That(manifest["state"]!.GetValue<string>()).IsEqualTo(nameof(PluginState.Error));
    }

    /// <summary>临时插件目录夹具，与 <c>PluginManagementServiceTests.PluginWorkspace</c> 保持一致的布局。</summary>
    private sealed class ManifestWorkspace : IDisposable
    {
        public ManifestWorkspace()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "LYBox.Tests", Guid.NewGuid().ToString("N"));
            PluginsDirectory = Path.Combine(RootDirectory, "plugins");
            Directory.CreateDirectory(PluginsDirectory);
        }

        public string RootDirectory { get; }

        public string PluginsDirectory { get; }

        public async Task<string> WriteManifestAsync(string pluginId, string manifestJson)
        {
            var directory = Path.Combine(PluginsDirectory, pluginId);
            Directory.CreateDirectory(directory);
            var manifestPath = Path.Combine(directory, "plugin.json");
            await File.WriteAllTextAsync(manifestPath, manifestJson);
            return manifestPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
                Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
