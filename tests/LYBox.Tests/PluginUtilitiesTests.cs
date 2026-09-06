using System.Text.Json;
using LYBox.Plugin.Shared.Models;
using LYBox.Layout.Core.Services;
using TUnit.Core;
using TUnit.Assertions;

namespace LYBox.Tests;

/// <summary>
/// PluginUtilities 共享工具类的单元测试：验证 JsonSerializerOptions 一致性和 CopyDirectory 功能。
/// </summary>
public class PluginUtilitiesTests
{
    #region JsonOptions 一致性

    [Test]
    public async Task JsonOptions_WriteIndented_IsTrue()
    {
        // 确保统一使用缩进输出（原 PluginLoader 的配置，现在作为标准）
        await Assert.That(PluginUtilities.JsonOptions.WriteIndented).IsTrue();
    }

    [Test]
    public async Task JsonOptions_PropertyNamingPolicy_IsCamelCase()
    {
        // 确保命名策略为 camelCase
        await Assert.That(PluginUtilities.JsonOptions.PropertyNamingPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task JsonOptions_SerializeManifest_ProducesCamelCaseJson()
    {
        // 验证序列化 PluginManifest 时正确使用 camelCase
        var manifest = new PluginManifest
        {
            PluginId = "test-id",
            Name = "TestPlugin",
            Version = "1.0.0",
            Author = "TestAuthor",
            Description = "Test description",
            MinPluginSdkVersion = "2.1.0"
        };

        var json = JsonSerializer.Serialize(manifest, PluginUtilities.JsonOptions);

        await Assert.That(json).Contains("\"pluginId\": \"test-id\"");
        await Assert.That(json).Contains("\"name\": \"TestPlugin\"");
        await Assert.That(json).Contains("\"version\": \"1.0.0\"");
        await Assert.That(json).Contains("\"minPluginSdkVersion\": \"2.1.0\"");
    }

    [Test]
    public async Task JsonOptions_SerializeAndDeserialize_RoundTrip()
    {
        // 验证序列化和反序列化的往返一致性
        var original = new PluginManifest
        {
            PluginId = "round-trip-id",
            Name = "RoundTripPlugin",
            Version = "3.2.1",
            Author = "RTAuthor",
            Description = "Round trip test",
            MinPluginSdkVersion = "2.0.0"
        };

        var json = JsonSerializer.Serialize(original, PluginUtilities.JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PluginManifest>(json, PluginUtilities.JsonOptions);

        await Assert.That(deserialized).IsNotNull();
        await Assert.That(deserialized!.PluginId).IsEqualTo(original.PluginId);
        await Assert.That(deserialized.Name).IsEqualTo(original.Name);
        await Assert.That(deserialized.Version).IsEqualTo(original.Version);
        await Assert.That(deserialized.MinPluginSdkVersion).IsEqualTo(original.MinPluginSdkVersion);
    }

    [Test]
    public async Task JsonOptions_SerializePendingUpgradeInfo_ProducesCamelCaseJson()
    {
        // 验证 PendingUpgradeInfo 也使用相同的 camelCase 命名（原 PluginInstallationManager 的用途）
        var info = new PendingUpgradeInfo
        {
            PluginId = "upgrade-target",
            NewVersion = "2.0.0",
            ScheduledAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PreserveState = true
        };

        var json = JsonSerializer.Serialize(info, PluginUtilities.JsonOptions);

        await Assert.That(json).Contains("\"pluginId\": \"upgrade-target\"");
        await Assert.That(json).Contains("\"newVersion\": \"2.0.0\"");
        await Assert.That(json).Contains("\"preserveState\": true");
    }

    #endregion

    #region CopyDirectory

    [Test]
    public async Task CopyDirectory_CopiesAllFilesAndSubdirectories()
    {
        // 准备源目录结构
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lybox-test-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(tempRoot, "source");
        var destDir = Path.Combine(tempRoot, "dest");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDir, "sub1", "sub2"));
            File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(sourceDir, "sub1", "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(sourceDir, "sub1", "sub2", "file3.txt"), "content3");

            // 执行
            PluginUtilities.CopyDirectory(sourceDir, destDir);

            // 验证
            await Assert.That(File.Exists(Path.Combine(destDir, "file1.txt"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(destDir, "sub1", "file2.txt"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(destDir, "sub1", "sub2", "file3.txt"))).IsTrue();
            await Assert.That(File.ReadAllText(Path.Combine(destDir, "file1.txt"))).IsEqualTo("content1");
            await Assert.That(File.ReadAllText(Path.Combine(destDir, "sub1", "sub2", "file3.txt"))).IsEqualTo("content3");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public async Task CopyDirectory_OverwritesExistingFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lybox-test-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(tempRoot, "source");
        var destDir = Path.Combine(tempRoot, "dest");

        try
        {
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);

            File.WriteAllText(Path.Combine(sourceDir, "shared.txt"), "new_content");
            File.WriteAllText(Path.Combine(destDir, "shared.txt"), "old_content");

            PluginUtilities.CopyDirectory(sourceDir, destDir);

            await Assert.That(File.ReadAllText(Path.Combine(destDir, "shared.txt"))).IsEqualTo("new_content");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public async Task CopyDirectory_CreatesDestinationIfNotExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lybox-test-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(tempRoot, "source");
        var destDir = Path.Combine(tempRoot, "dest");

        try
        {
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "file.txt"), "data");

            // 目标目录不存在
            await Assert.That(Directory.Exists(destDir)).IsFalse();

            PluginUtilities.CopyDirectory(sourceDir, destDir);

            await Assert.That(Directory.Exists(destDir)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(destDir, "file.txt"))).IsTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    #endregion
}
