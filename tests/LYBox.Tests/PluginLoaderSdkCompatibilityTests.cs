using LYBox.Plugin.Shared;
using LYBox.Layout.Core.Services;
using TUnit.Core;
using TUnit.Assertions;

namespace LYBox.Tests;

/// <summary>
/// PluginLoader.IsPluginSdkCompatible 的单元测试。
/// 宿主 SDK 版本由 PluginSdkContract.CurrentVersion 编译时注入（见 LYBox.Plugin.Shared.csproj 的
/// GeneratePluginSdkContract target），值取自仓库根 version.props 的 HostVersion。
///
/// 重要：本测试中所有「更高 / 更低 / 精确」用例的版本号均基于 <see cref="Host"/> 动态派生，
/// 因此 version.props 的版本 bump 后测试语义仍然成立，无需人工同步硬编码的宿主版本。
/// </summary>
public class PluginLoaderSdkCompatibilityTests
{
    /// <summary>宿主 SDK 稳定版本三段（已剥离预发布标签）。</summary>
    private static readonly (int Major, int Minor, int Build) Host =
        ParseStable(PluginSdkContract.CurrentVersion);

    private static (int Major, int Minor, int Build) ParseStable(string version)
    {
        var core = version;
        var dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0) core = core[..dash];

        var parts = core.Split('.');
        return (
            int.Parse(parts[0]),
            parts.Length > 1 ? int.Parse(parts[1]) : 0,
            parts.Length > 2 ? int.Parse(parts[2]) : 0);
    }

    private static string Ver(int major, int minor, int build) => $"{major}.{minor}.{build}";

    #region null / 空字符串 → 通过（无约束）

    [Test]
    [MethodDataSource(nameof(NullOrWhitespaceCases))]
    public async Task IsPluginSdkCompatible_NullOrWhitespace_ReturnsTrue(string? required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsTrue();
    }

    public static IEnumerable<object?[]> NullOrWhitespaceCases() => new List<object?[]>
    {
        new object?[] { null },
        new object?[] { "" },
        new object?[] { "   " },
        new object?[] { "\t" },
    };

    #endregion

    #region 解析失败 → 拒绝（fail-closed）

    [Test]
    [MethodDataSource(nameof(InvalidVersionCases))]
    public async Task IsPluginSdkCompatible_InvalidVersion_ReturnsFalse(string? required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsFalse();
    }

    public static IEnumerable<object[]> InvalidVersionCases() => new List<object[]>
    {
        new object[] { "abc" },       // 非数字
        new object[] { "1.x.0" },     // 次版本号非数字
        new object[] { "1.2.x" },     // 修订号非数字
        new object[] { "v2.1.0" },    // 带前缀
        new object[] { "2.1.0." },    // 尾部多余点
    };

    #endregion

    #region 主版本号不匹配 → 不兼容

    [Test]
    [MethodDataSource(nameof(MajorMismatchCases))]
    public async Task IsPluginSdkCompatible_MajorMismatch_ReturnsFalse(string required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsFalse();
    }

    public static IEnumerable<object[]> MajorMismatchCases() => new List<object[]>
    {
        new object[] { Ver(Host.Major - 1, 1, 0) },   // 低于主版本
        new object[] { Ver(Host.Major + 1, 1, 0) },   // 高于主版本
        new object[] { Ver(Host.Major - 1, 0, 0) },   // 主版本不同且最低版本
        new object[] { Ver(Host.Major + 8, 0, 0) },   // 远高主版本
    };

    #endregion

    #region 主版本匹配，次版本号 > 要求 → 兼容

    [Test]
    [MethodDataSource(nameof(MinorHigherCases))]
    public async Task IsPluginSdkCompatible_MinorHigherThanRequired_ReturnsTrue(string required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsTrue();
    }

    public static IEnumerable<object[]> MinorHigherCases() => new List<object[]>
    {
        new object[] { Ver(Host.Major, LowerMinor, 0) },    // 宿主次版本更高
        new object[] { Ver(Host.Major, LowerMinor, 5) },    // 宿主次版本更高，修订号也更
        new object[] { Ver(Host.Major, LowerMinor, 999) },  // 任意高修订号
    };

    #endregion

    #region 主版本匹配，次版本号 < 要求 → 不兼容

    [Test]
    [MethodDataSource(nameof(MinorLowerCases))]
    public async Task IsPluginSdkCompatible_MinorLowerThanRequired_ReturnsFalse(string required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsFalse();
    }

    public static IEnumerable<object[]> MinorLowerCases() => new List<object[]>
    {
        new object[] { Ver(Host.Major, Host.Minor + 1, 0) },   // 宿主次版本 < 要求 → 不兼容
        new object[] { Ver(Host.Major, Host.Minor + 3, 0) },   // 远高次版本
        new object[] { Ver(Host.Major, Host.Minor + 2, 0) },   // 次版本更高 + 修订号更高
    };

    #endregion

    #region 主版本+次版本匹配，修订号 >= 要求 → 兼容

    [Test]
    [MethodDataSource(nameof(ExactMatchCases))]
    public async Task IsPluginSdkCompatible_ExactMatch_ReturnsTrue(string required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsTrue();
    }

    public static IEnumerable<object[]> ExactMatchCases() => new List<object[]>
    {
        new object[] { Ver(Host.Major, Host.Minor, Host.Build) },  // 精确匹配
        new object[] { $"{Host.Major}.{Host.Minor}" },             // 缺省修订号 → 0
    };

    #endregion

    #region 主版本+次版本匹配，修订号 < 要求 → 不兼容

    [Test]
    [MethodDataSource(nameof(BuildHigherCases))]
    public async Task IsPluginSdkCompatible_BuildHigherThanRequired_ReturnsFalse(string required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsFalse();
    }

    public static IEnumerable<object[]> BuildHigherCases() => new List<object[]>
    {
        new object[] { Ver(Host.Major, Host.Minor, Host.Build + 1) },    // 同主+次版本，修订号更高 → 不兼容
        new object[] { Ver(Host.Major, Host.Minor, Host.Build + 999) },  // 远高修订号
    };

    #endregion

    #region 预发布标签 → 忽略，取稳定版本部分

    [Test]
    [MethodDataSource(nameof(PreReleaseStrippedAndMatchesCases))]
    public async Task IsPluginSdkCompatible_PreReleaseTag_StrippedAndMatches(string required)
    {
        // 预发布标签应被忽略，取核心版本比较 → 兼容
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsTrue();
    }

    public static IEnumerable<object[]> PreReleaseStrippedAndMatchesCases() => new List<object[]>
    {
        new object[] { Ver(Host.Major, Host.Minor, Host.Build) + "-preview" },
        new object[] { Ver(Host.Major, Host.Minor, Host.Build) + "-alpha.1" },
        new object[] { Ver(Host.Major, Host.Minor, Host.Build) + "-rc.2+build.456" },
    };

    [Test]
    [MethodDataSource(nameof(PreReleaseStrippedAndRejectsWhenHigherCases))]
    public async Task IsPluginSdkCompatible_PreReleaseTag_StrippedAndRejectsWhenHigher(string required)
    {
        // 预发布标签被忽略后，核心版本高于宿主 → 不兼容
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsFalse();
    }

    public static IEnumerable<object[]> PreReleaseStrippedAndRejectsWhenHigherCases() => new List<object[]>
    {
        new object[] { Ver(Host.Major, Host.Minor, Host.Build + 1) + "-preview" },  // 剥离后修订号更高 → 不兼容
        new object[] { Ver(Host.Major, Host.Minor + 1, 0) + "-beta" },              // 剥离后次版本更高 → 不兼容
    };

    #endregion

    #region 缺省版本段

    [Test]
    [MethodDataSource(nameof(PartialVersionCases))]
    public async Task IsPluginSdkCompatible_PartialVersion_DefaultsMissingSegments(string required)
    {
        await Assert.That(PluginLoader.IsPluginSdkCompatible(required)).IsTrue();
    }

    public static IEnumerable<object[]> PartialVersionCases() => new List<object[]>
    {
        new object[] { Host.Major.ToString() },                          // 仅主版本 → major.0.0，宿主更高 → 兼容
        new object[] { $"{Host.Major}.{LowerMinor}" },                    // 主+次，低于宿主 → 兼容
    };

    #endregion

    /// <summary>比宿主次版本低一档（不低于 0），用于构造「宿主更高」的用例。</summary>
    private static int LowerMinor => Math.Max(0, Host.Minor - 1);
}
