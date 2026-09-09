using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace LYBox.Plugin.Shared.Converters;

public class IconNameToPathConverter: IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i)
        {
            // 保留 int 分支的兜底逻辑（暂未发现调用方；保留以兼容旧用法）。
            return AvaloniaProperty.UnsetValue;
        }
        if (value is string iconName && !string.IsNullOrEmpty(iconName))
        {
            // 键名容错：原键无 FluentIcon 前缀则补上，避免插件写 "Home20Regular" 时找不到 "FluentIconHome20Regular"。
            var key = iconName.StartsWith("FluentIcon", StringComparison.Ordinal) || iconName.StartsWith("Fluent", StringComparison.Ordinal)
                ? iconName
                : "FluentIcon" + iconName;

            var app = Application.Current;
            if (app is not null)
            {
                // 1. 直接查 Application.Resources（含 MergedDictionaries 递归）。
                if (TryFindResource(app.Resources, key, out var geometry) && geometry is not null)
                {
                    return geometry;
                }

                // 2. 遍历 Styles 中的 Resources（UrsaFluentTheme、UrsaSemiTheme 等主题样式把图标资源挂在这里）。
                foreach (var style in app.Styles)
                {
                    if (style is Styles styles && TryFindResource(styles.Resources, key, out geometry) && geometry is not null)
                    {
                        return geometry;
                    }
                    if (style is IResourceProvider rp && TryFindResourceInProvider(rp, key, out geometry) && geometry is not null)
                    {
                        return geometry;
                    }
                }
            }
        }
        return AvaloniaProperty.UnsetValue;
    }

    private static bool TryFindResource(IResourceDictionary dict, object key, out StreamGeometry? geometry)
    {
        geometry = null;
        if (dict.TryGetValue(key, out var resource) && resource is StreamGeometry geo)
        {
            geometry = geo;
            return true;
        }
        foreach (var merged in dict.MergedDictionaries)
        {
            if (TryFindResourceInProvider(merged, key, out geometry))
                return true;
        }
        return false;
    }

    private static bool TryFindResourceInProvider(IResourceProvider provider, object key, out StreamGeometry? geometry)
    {
        geometry = null;
        return provider is IResourceDictionary rd && TryFindResource(rd, key, out geometry);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
