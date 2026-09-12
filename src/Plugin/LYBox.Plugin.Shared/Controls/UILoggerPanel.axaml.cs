using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LYBox.Plugin.Shared.Models;

namespace LYBox.Plugin.Shared.Controls;

/// <summary>
/// 共享 UILogger 日志面板。
/// <list type="bullet">
/// <item>绑定 <see cref="LogEntries"/>（元素通常为 <see cref="LogEntry"/>）与 <see cref="ClearLogCommand"/> 即可使用；</item>
/// <item>支持多行多选（单击切换 / Ctrl、Shift 连选）；</item>
/// <item>右键弹出“复制”：复制所有选中行（无选中时复制右键所在行），多行以换行拼接写入剪贴板，复制行为由面板内部完成，不依赖 ViewModel 命令。</item>
/// </list>
/// </summary>
public partial class UILoggerPanel : UserControl
{
    public static readonly StyledProperty<IEnumerable?> LogEntriesProperty =
        AvaloniaProperty.Register<UILoggerPanel, IEnumerable?>(nameof(LogEntries));

    public static readonly StyledProperty<ICommand?> ClearLogCommandProperty =
        AvaloniaProperty.Register<UILoggerPanel, ICommand?>(nameof(ClearLogCommand));

    /// <summary>右键按下时所在行的数据项（用于无选中时退化为复制该行）。</summary>
    private object? _contextEntry;

    public IEnumerable? LogEntries
    {
        get => GetValue(LogEntriesProperty);
        set => SetValue(LogEntriesProperty, value);
    }

    public ICommand? ClearLogCommand
    {
        get => GetValue(ClearLogCommandProperty);
        set => SetValue(ClearLogCommandProperty, value);
    }

    public UILoggerPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 右键按下：记录所在行；若该行未选中，则将其设为唯一选中项（避免右键打开菜单却复制不到目标行）。
    /// 已选中的行（含多选）保持不变，便于批量复制。
    /// </summary>
    private void OnLogListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (LogList is null) return;
        var point = e.GetCurrentPoint(LogList);
        if (!point.Properties.IsRightButtonPressed) return;
        if (e.Source is not Visual source) return;

        var container = source.FindAncestorOfType<ListBoxItem>();
        if (container?.DataContext is null) return;

        _contextEntry = container.DataContext;
        if (!container.IsSelected)
        {
            var index = LogList.Items.IndexOf(_contextEntry);
            if (index >= 0)
            {
                LogList.SelectedIndex = index;
            }
        }
    }

    /// <summary>复制选中行（多行以换行拼接）；无选中时复制右键所在行。</summary>
    private async void OnCopySelectedClick(object? sender, RoutedEventArgs e)
    {
        var items = new List<object>();

        if (LogList?.SelectedItems is IEnumerable selected)
        {
            foreach (var item in selected)
            {
                if (item is not null) items.Add(item);
            }
        }

        if (items.Count == 0 && _contextEntry is not null)
        {
            items.Add(_contextEntry);
        }

        if (items.Count == 0) return;

        var text = string.Join(Environment.NewLine, items.Select(GetCopyText));
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static string GetCopyText(object item) => item switch
    {
        LogEntry entry => entry.CopyText,
        _ => item.ToString() ?? string.Empty
    };
}
