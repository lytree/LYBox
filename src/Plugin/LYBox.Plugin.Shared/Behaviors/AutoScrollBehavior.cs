using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace LYBox.Plugin.Shared.Behaviors;

/// <summary>
/// 附加属性：使绑定了 <see cref="INotifyCollectionChanged"/> 集合的 <see cref="ListBox"/>
/// 在新增项时自动滚动到最后一行。
/// 用法（XAML）：
/// <code>
/// &lt;ListBox ItemsSource="{Binding LogEntries}"
///          behaviors:AutoScrollBehavior.AutoScrollToEnd="True" /&gt;
/// </code>
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly AttachedProperty<bool> AutoScrollToEndProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>(
            "AutoScrollToEnd", typeof(AutoScrollBehavior));

    private static readonly AttachedProperty<INotifyCollectionChanged?> TrackedSourceProperty =
        AvaloniaProperty.RegisterAttached<ListBox, INotifyCollectionChanged?>(
            "TrackedSource", typeof(AutoScrollBehavior));

    static AutoScrollBehavior()
    {
        AutoScrollToEndProperty.Changed.AddClassHandler<ListBox>(OnAutoScrollChanged);
        ListBox.ItemsSourceProperty.Changed.AddClassHandler<ListBox>(OnItemsSourceChanged);
    }

    public static void SetAutoScrollToEnd(ListBox element, bool value) => element.SetValue(AutoScrollToEndProperty, value);
    public static bool GetAutoScrollToEnd(ListBox element) => element.GetValue(AutoScrollToEndProperty);

    static void OnAutoScrollChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            TrySubscribe(listBox);
        }
        else
        {
            Unsubscribe(listBox);
        }
    }

    static void OnItemsSourceChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (!GetAutoScrollToEnd(listBox)) return;
        Unsubscribe(listBox);
        TrySubscribe(listBox);
    }

    static void TrySubscribe(ListBox listBox)
    {
        if (listBox.ItemsSource is INotifyCollectionChanged source)
        {
            source.CollectionChanged += (s, e) => OnCollectionChanged(listBox, e);
            listBox.SetValue(TrackedSourceProperty, source);
        }
    }

    static void Unsubscribe(ListBox listBox)
    {
        var prev = listBox.GetValue(TrackedSourceProperty);
        if (prev is null) return;
        prev.CollectionChanged -= (s, e) => OnCollectionChanged(listBox, e);
        listBox.SetValue(TrackedSourceProperty, null);
    }

    static void OnCollectionChanged(ListBox listBox, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count == 0) return;
        var item = e.NewItems.Count == 1 ? e.NewItems[0]! : e.NewItems[e.NewItems.Count - 1]!;
        listBox.ScrollIntoView(item);
    }
}
