using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace LYBox.Layout.Ursa.Controls;

/// <summary>
/// WinUI 风格顶部导航条：水平排列的图标+文字导航项，选中项底部 accent 指示条；
/// 超出 <see cref="MaxVisibleItems"/> 的项自动收进右侧「更多」浮出菜单。
/// 数据层复用 <see cref="FluentNavigationItem"/>，与 Ursa NavMenu 的侧边导航形成互补。
/// </summary>
public partial class FluentTopNavigation : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<FluentTopNavigation, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<FluentNavigationItem?> SelectedItemProperty =
        AvaloniaProperty.Register<FluentTopNavigation, FluentNavigationItem?>(
            nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>直接显示的导航项数量，其余进入溢出菜单。</summary>
    public static readonly StyledProperty<int> MaxVisibleItemsProperty =
        AvaloniaProperty.Register<FluentTopNavigation, int>(nameof(MaxVisibleItems), 6);

    /// <summary>是否启用溢出收纳（关闭后使用横向滚动）。</summary>
    public static readonly StyledProperty<bool> IsOverflowEnabledProperty =
        AvaloniaProperty.Register<FluentTopNavigation, bool>(nameof(IsOverflowEnabled), true);

    public static readonly StyledProperty<bool> HasOverflowProperty =
        AvaloniaProperty.Register<FluentTopNavigation, bool>(nameof(HasOverflow));

    /// <summary>「更多」浮出菜单是否打开。</summary>
    public static readonly StyledProperty<bool> IsOverflowOpenProperty =
        AvaloniaProperty.Register<FluentTopNavigation, bool>(nameof(IsOverflowOpen));

    public FluentTopNavigation()
    {
        InitializeComponent();

        SelectCommand = new RelayCommand<FluentNavigationItem>(item =>
        {
            if (item is not null)
                SelectedItem = item;
        });

        ItemsSourceProperty.Changed.AddClassHandler<FluentTopNavigation>((x, _) => x.Rebuild());
        MaxVisibleItemsProperty.Changed.AddClassHandler<FluentTopNavigation>((x, _) => x.Rebuild());
        IsOverflowEnabledProperty.Changed.AddClassHandler<FluentTopNavigation>((x, _) => x.Rebuild());
        SelectedItemProperty.Changed.AddClassHandler<FluentTopNavigation>((x, e) =>
            x.ApplySelection(e.NewValue as FluentNavigationItem));
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public FluentNavigationItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
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

    /// <summary>主区域实际显示的项。</summary>
    public ObservableCollection<FluentNavigationItem> VisibleItems { get; } = [];

    /// <summary>收进「更多」浮出菜单的项。</summary>
    public ObservableCollection<FluentNavigationItem> OverflowItems { get; } = [];

    public ICommand SelectCommand { get; }

    /// <summary>「更多」按钮：切换浮出菜单。</summary>
    private void OnMoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsOverflowOpen = !IsOverflowOpen;
    }

    private void Rebuild()
    {
        var all = ItemsSource?.OfType<FluentNavigationItem>().ToList() ?? [];

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
        ApplySelection(SelectedItem);
    }

    private void ApplySelection(FluentNavigationItem? selected)
    {
        foreach (var item in VisibleItems.Concat(OverflowItems))
            item.IsSelected = ReferenceEquals(item, selected);
    }
}
