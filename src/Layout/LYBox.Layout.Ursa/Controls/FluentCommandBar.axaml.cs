using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using LYBox.Plugin.Shared.Converters;

namespace LYBox.Layout.Ursa.Controls;

/// <summary>
/// WinUI 风格命令栏：图标+文字主命令、分隔线、切换按钮；超出 <see cref="MaxVisibleItems"/>
/// 的命令自动收进右侧「…」溢出浮出菜单。
/// 视觉元素全部由现有主题的 Button / ToggleButton / PathIcon 组合而成。
/// </summary>
public partial class FluentCommandBar : UserControl
{
    private static readonly IconNameToPathConverter IconConverter = new();

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<FluentCommandBar, IEnumerable?>(nameof(ItemsSource));

    /// <summary>标签位置，默认图标在左、文字在右。</summary>
    public static readonly StyledProperty<FluentCommandLabelPosition> LabelPositionProperty =
        AvaloniaProperty.Register<FluentCommandBar, FluentCommandLabelPosition>(
            nameof(LabelPosition), FluentCommandLabelPosition.Right);

    /// <summary>主区域最多显示的命令数，其余进入溢出菜单。</summary>
    public static readonly StyledProperty<int> MaxVisibleItemsProperty =
        AvaloniaProperty.Register<FluentCommandBar, int>(nameof(MaxVisibleItems), 8);

    public static readonly StyledProperty<bool> IsOverflowEnabledProperty =
        AvaloniaProperty.Register<FluentCommandBar, bool>(nameof(IsOverflowEnabled), true);

    public static readonly StyledProperty<bool> HasOverflowProperty =
        AvaloniaProperty.Register<FluentCommandBar, bool>(nameof(HasOverflow));

    public static readonly StyledProperty<bool> IsOverflowOpenProperty =
        AvaloniaProperty.Register<FluentCommandBar, bool>(nameof(IsOverflowOpen));

    public FluentCommandBar()
    {
        InitializeComponent();

        ItemsSourceProperty.Changed.AddClassHandler<FluentCommandBar>((x, _) => x.Rebuild());
        LabelPositionProperty.Changed.AddClassHandler<FluentCommandBar>((x, _) => x.Rebuild());
        MaxVisibleItemsProperty.Changed.AddClassHandler<FluentCommandBar>((x, _) => x.Rebuild());
        IsOverflowEnabledProperty.Changed.AddClassHandler<FluentCommandBar>((x, _) => x.Rebuild());
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public FluentCommandLabelPosition LabelPosition
    {
        get => GetValue(LabelPositionProperty);
        set => SetValue(LabelPositionProperty, value);
    }

    public int MaxVisibleItems
    {
        get => GetValue(MaxVisibleItemsProperty);
        set => SetValue(MaxVisibleItemsProperty, value);
    }

    public bool IsOverflowEnabled
    {
        get => GetValue(IsOverflowEnabledProperty);
        set => SetValue(IsOverflowEnabledProperty, value);
    }

    public bool HasOverflow
    {
        get => GetValue(HasOverflowProperty);
        private set => SetValue(HasOverflowProperty, value);
    }

    public bool IsOverflowOpen
    {
        get => GetValue(IsOverflowOpenProperty);
        set => SetValue(IsOverflowOpenProperty, value);
    }

    /// <summary>主区域实际显示的命令。</summary>
    public ObservableCollection<FluentCommandBarItem> VisibleItems { get; } = [];

    /// <summary>收进溢出菜单的命令。</summary>
    public ObservableCollection<FluentCommandBarItem> OverflowItems { get; } = [];

    private void OnMoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsOverflowOpen = !IsOverflowOpen;
    }

    private void Rebuild()
    {
        var all = ItemsSource?.OfType<FluentCommandBarItem>().ToList() ?? [];

        VisibleItems.Clear();
        OverflowItems.Clear();

        var max = Math.Max(1, MaxVisibleItems);
        var overflow = IsOverflowEnabled && all.Count > max;

        if (!overflow)
        {
            foreach (var item in all)
                VisibleItems.Add(item);
        }
        else
        {
            for (var i = 0; i < max; i++)
                VisibleItems.Add(all[i]);
            for (var i = max; i < all.Count; i++)
                OverflowItems.Add(all[i]);
        }

        HasOverflow = OverflowItems.Count > 0;
        BuildControls();
    }

    private void BuildControls()
    {
        PrimaryPanel.Children.Clear();
        OverflowPanel.Children.Clear();

        foreach (var item in VisibleItems)
            PrimaryPanel.Children.Add(CreateControl(item, inOverflow: false));

        foreach (var item in OverflowItems)
            OverflowPanel.Children.Add(CreateControl(item, inOverflow: true));
    }

    private Control CreateControl(FluentCommandBarItem item, bool inOverflow)
    {
        if (item.IsSeparator)
        {
            return new Border
            {
                Classes = { inOverflow ? "FluentCommandSeparatorOverflow" : "FluentCommandSeparator" },
            };
        }

        var isCompact = LabelPosition == FluentCommandLabelPosition.Collapsed && !inOverflow;

        var content = new StackPanel
        {
            Orientation = isCompact ? Orientation.Horizontal : LabelPosition == FluentCommandLabelPosition.Bottom && !inOverflow
                ? Orientation.Vertical
                : Orientation.Horizontal,
            Spacing = isCompact ? 0 : 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (!string.IsNullOrEmpty(item.IconName) && IconConverter.Convert(item.IconName, typeof(StreamGeometry), null, CultureInfo.InvariantCulture) is StreamGeometry geometry)
        {
            content.Children.Add(new PathIcon
            {
                Width = 16,
                Height = 16,
                Data = geometry,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (!isCompact && !string.IsNullOrEmpty(item.Header))
        {
            content.Children.Add(new TextBlock
            {
                Text = item.Header,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        Button button = item.IsToggle ? new ToggleButton() : new Button();
        button.Classes.Add("FluentCommandItem");
        if (inOverflow)
            button.Classes.Add("over");
        if (isCompact)
            button.Classes.Add("compact");

        button.Content = content;
        button.Command = item.Command;
        button.CommandParameter = item.CommandParameter;

        if (button is ToggleButton toggle)
        {
            toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(FluentCommandBarItem.IsChecked))
            {
                Source = item,
                Mode = BindingMode.TwoWay,
            });
        }

        return button;
    }
}
