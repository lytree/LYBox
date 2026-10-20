using LYBox.DemoPlugin.Views;
using LYBox.Plugin.Shared.Attributes;

namespace LYBox.DemoPlugin.ViewModels;

/// <summary>
/// 演示工具集合。用于目检：
/// 1. 侧边栏分组（父项 + 子项）与折叠行为；
/// 2. 首页三列工具卡片网格（分组子项 + 顶级叶子项）；
/// 3. 卡片图标、状态徽标与描述文案。
/// </summary>
/// <remarks>
/// 注意：<c>[Menu]</c> 的 <c>parentKey</c> 关联的是父项的 <b>Header 文案</b>（原始字符串），
/// 不是父项的 Key —— 宿主以 <c>MenuItemViewModel.RawHeader</c> 建立父子索引。
/// </remarks>
[NavigationItem("demo-encoders")]
[Menu("编码器", "demo-encoders", IconName = "FluentCode20Regular", Order = 10)]
[ViewMap(typeof(DemoToolView))]
public partial class EncoderGroupViewModel : DemoToolViewModel
{
    public EncoderGroupViewModel()
    {
        Title = "编码器";
        Description = "文本与数据的编码 / 解码工具集";
    }
}

[NavigationItem("demo-base64")]
[Menu("Base64 编解码", "demo-base64", parentKey: "编码器", IconName = "FluentCode20Regular", Order = 11, Description = "文本与 Base64 互转")]
[ViewMap(typeof(DemoToolView))]
public partial class Base64ToolViewModel : DemoToolViewModel
{
    public Base64ToolViewModel()
    {
        Title = "Base64 编解码";
        Description = "文本与 Base64 互转";
    }
}

[NavigationItem("demo-url")]
[Menu("URL 编解码", "demo-url", parentKey: "编码器", IconName = "FluentLink20Regular", Order = 12, Description = "URL 转义与还原")]
[ViewMap(typeof(DemoToolView))]
public partial class UrlToolViewModel : DemoToolViewModel
{
    public UrlToolViewModel()
    {
        Title = "URL 编解码";
        Description = "URL 转义与还原";
    }
}

[NavigationItem("demo-json")]
[Menu("JSON 格式化", "demo-json", parentKey: "编码器", IconName = "FluentDocumentText20Regular", Order = 13, Status = "Beta", Description = "JSON 美化与压缩")]
[ViewMap(typeof(DemoToolView))]
public partial class JsonToolViewModel : DemoToolViewModel
{
    public JsonToolViewModel()
    {
        Title = "JSON 格式化";
        Description = "JSON 美化与压缩";
    }
}

[NavigationItem("demo-timestamp")]
[Menu("时间戳转换", "demo-timestamp", IconName = "FluentClock20Regular", Order = 20, Description = "Unix 时间戳与日期互转")]
[ViewMap(typeof(DemoToolView))]
public partial class TimestampToolViewModel : DemoToolViewModel
{
    public TimestampToolViewModel()
    {
        Title = "时间戳转换";
        Description = "Unix 时间戳与日期互转";
    }
}

[NavigationItem("demo-color")]
[Menu("颜色转换", "demo-color", IconName = "FluentColor20Regular", Order = 30, Description = "HEX / RGB / HSL 互转")]
[ViewMap(typeof(DemoToolView))]
public partial class ColorToolViewModel : DemoToolViewModel
{
    public ColorToolViewModel()
    {
        Title = "颜色转换";
        Description = "HEX / RGB / HSL 互转";
    }
}

[NavigationItem("demo-regex")]
[Menu("正则测试", "demo-regex", IconName = "FluentTextGrammarWand20Regular", Order = 40, Status = "New", Description = "实时匹配与分组高亮")]
[ViewMap(typeof(DemoToolView))]
public partial class RegexToolViewModel : DemoToolViewModel
{
    public RegexToolViewModel()
    {
        Title = "正则测试";
        Description = "实时匹配与分组高亮";
    }
}
