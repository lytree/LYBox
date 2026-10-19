using System;

namespace LYBox.Plugin.Shared;

/// <summary>
/// 图标回退表：插件未声明 <c>IconName</c> 时，按菜单 Key 的稳定散列挑选一个通用 Fluent 图标，
/// 保证侧边栏分类与首页工具卡片仍有可辨识的图形占位。
/// </summary>
/// <remarks>
/// 仅影响显示，不写回插件元数据；显式声明的 IconName 始终优先。
/// 散列自行实现（而非 string.GetHashCode），确保跨进程/跨次启动结果稳定。
/// </remarks>
public static class FallbackIcons
{
    private static readonly string[] s_palette =
    [
        "FluentApps20Regular",
        "FluentWindow20Regular",
        "FluentGrid20Regular",
        "FluentCode20Regular",
        "FluentTextFont20Regular",
        "FluentTextBulletList20Regular",
        "FluentTable20Regular",
        "FluentDatabase20Regular",
        "FluentDocumentText20Regular",
        "FluentImage20Regular",
        "FluentColor20Regular",
        "FluentPaintBrush20Regular",
        "FluentCalculator20Regular",
        "FluentMathFormula20Regular",
        "FluentCalendarLtr20Regular",
        "FluentClock20Regular",
        "FluentTimer20Regular",
        "FluentChartMultiple20Regular",
        "FluentBeaker20Regular",
        "FluentWrench20Regular",
        "FluentCube20Regular",
        "FluentDeveloperBoard20Regular",
        "FluentArrowSwap20Regular",
        "FluentTextGrammarWand20Regular",
        "FluentMusicNote220Regular",
        "FluentBookOpen20Regular",
        "FluentGames20Regular",
        "FluentGlobe20Regular",
        "FluentShield20Regular",
        "FluentMail20Regular",
    ];

    /// <summary>按 Key 取稳定图标名；Key 为空时返回首个通用图标。</summary>
    public static string For(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return s_palette[0];

        unchecked
        {
            var hash = 17;
            foreach (var ch in key)
                hash = hash * 31 + ch;

            return s_palette[(hash & 0x7FFFFFFF) % s_palette.Length];
        }
    }
}
