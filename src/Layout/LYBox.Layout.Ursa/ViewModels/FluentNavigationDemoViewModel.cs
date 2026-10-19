using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.ViewModels;
using LYBox.Layout.Ursa.Controls;

namespace LYBox.Layout.Ursa.ViewModels;

/// <summary>
/// 「Fluent 组件」示例页：命令栏 / 顶部菜单栏 / 顶部导航 / 侧边导航视图。
/// 数据仅用于演示交互，不影响宿主真实导航。
/// </summary>
public partial class FluentNavigationDemoViewModel : ViewModelBase
{
    public FluentNavigationDemoViewModel()
    {
        TopNavigationItems =
        [
            new FluentNavigationItem("主页", "Home", "FluentHome20Regular"),
            new FluentNavigationItem("GitHub", "GitHub", "FluentCode20Regular"),
            new FluentNavigationItem("订阅", "Subscriptions", "FluentStar20Regular"),
            new FluentNavigationItem("历史", "History", "FluentHistory20Regular"),
            new FluentNavigationItem("下载", "Downloads", "FluentArrowDownload20Regular"),
            new FluentNavigationItem("未播放的剧集", "Unplayed", "FluentVideo20Regular"),
            new FluentNavigationItem("正在播放", "NowPlaying", "FluentPlay20Regular"),
        ];

        SelectedTopNavigationItem = TopNavigationItems[0];

        BuildSideNavigation();
        SelectedSideNavigationItem = SideNavigationItems.FirstOrDefault(i => !i.IsSeparator);

        BuildCommandBar();
    }

    // ===== 命令栏 =====

    public ObservableCollection<FluentCommandBarItem> CommandBarItems { get; } = [];

    [ObservableProperty] private string _lastCommandText = "命令栏：等待执行命令…";
    [ObservableProperty] private int _zoomPercent = 100;

    [RelayCommand]
    private void ExecuteCommand(string? name)
    {
        LastCommandText = $"命令栏：已执行「{name}」";
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomPercent = Math.Min(400, ZoomPercent + 25);
        LastCommandText = $"命令栏：缩放到 {ZoomPercent}%";
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomPercent = Math.Max(25, ZoomPercent - 25);
        LastCommandText = $"命令栏：缩放到 {ZoomPercent}%";
    }

    private void BuildCommandBar()
    {
        var editToggle = new FluentCommandBarItem("编辑模式", "FluentEdit20Regular")
        {
            IsToggle = true,
            IsChecked = true,
        };
        editToggle.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FluentCommandBarItem.IsChecked))
                LastCommandText = $"命令栏：编辑模式 = {(editToggle.IsChecked ? "开" : "关")}";
        };

        CommandBarItems.Add(new FluentCommandBarItem("新建", "FluentAdd20Regular", ExecuteCommandCommand, "新建"));
        CommandBarItems.Add(new FluentCommandBarItem("保存", "FluentSave20Regular", ExecuteCommandCommand, "保存"));
        CommandBarItems.Add(new FluentCommandBarItem { IsSeparator = true });
        CommandBarItems.Add(editToggle);
        CommandBarItems.Add(new FluentCommandBarItem("撤销", "FluentArrowUndo20Regular", ExecuteCommandCommand, "撤销"));
        CommandBarItems.Add(new FluentCommandBarItem("重做", "FluentArrowRedo20Regular", ExecuteCommandCommand, "重做"));
        CommandBarItems.Add(new FluentCommandBarItem { IsSeparator = true });
        CommandBarItems.Add(new FluentCommandBarItem("放大", "FluentZoomIn20Regular", ZoomInCommand));
        CommandBarItems.Add(new FluentCommandBarItem("缩小", "FluentZoomOut20Regular", ZoomOutCommand));
        CommandBarItems.Add(new FluentCommandBarItem("共享", "FluentShare20Regular", ExecuteCommandCommand, "共享"));
        CommandBarItems.Add(new FluentCommandBarItem("删除", "FluentDelete20Regular", ExecuteCommandCommand, "删除"));
    }

    // ===== 顶部导航 =====

    public ObservableCollection<FluentNavigationItem> TopNavigationItems { get; }

    [ObservableProperty] private FluentNavigationItem? _selectedTopNavigationItem;
    [ObservableProperty] private string _topNavigationStatus = string.Empty;

    partial void OnSelectedTopNavigationItemChanged(FluentNavigationItem? value)
    {
        TopNavigationStatus = value is null ? "未选择" : $"当前页面：{value.Header}（Key = {value.Key}）";
    }

    // ===== 侧边导航视图 =====

    public ObservableCollection<MenuItemViewModel> SideNavigationItems { get; } = [];

    [ObservableProperty] private MenuItemViewModel? _selectedSideNavigationItem;
    [ObservableProperty] private bool _isSidePaneCollapsed;
    [ObservableProperty] private string _sideNavigationStatus = string.Empty;

    partial void OnSelectedSideNavigationItemChanged(MenuItemViewModel? value)
    {
        SideNavigationStatus = value is null || value.IsSeparator
            ? "未选择"
            : $"当前页面：{value.MenuHeader}（Key = {value.Key}）";
    }

    [RelayCommand]
    private void ToggleSidePane()
    {
        IsSidePaneCollapsed = !IsSidePaneCollapsed;
    }

    private void BuildSideNavigation()
    {
        SideNavigationItems.Add(NewItem("主页", "Demo.Home", "FluentHome20Regular"));
        SideNavigationItems.Add(NewItem("GitHub", "Demo.GitHub", "FluentCode20Regular"));
        SideNavigationItems.Add(NewItem("订阅", "Demo.Subscriptions", "FluentStar20Regular"));
        SideNavigationItems.Add(new MenuItemViewModel { IsSeparator = true });

        var history = NewItem("历史", "Demo.History", "FluentHistory20Regular");
        history.Children.Add(NewItem("视频历史", "Demo.History.Video", null));
        history.Children.Add(NewItem("音频历史", "Demo.History.Audio", null));
        SideNavigationItems.Add(history);

        var downloads = NewItem("下载", "Demo.Downloads", "FluentArrowDownload20Regular");
        downloads.Children.Add(NewItem("文件", "Demo.Downloads.Files", null));
        downloads.Children.Add(NewItem("文件夹", "Demo.Downloads.Folders", null));
        downloads.Children.Add(NewItem("应用", "Demo.Downloads.Apps", null));
        SideNavigationItems.Add(downloads);

        SideNavigationItems.Add(new MenuItemViewModel { IsSeparator = true });
        SideNavigationItems.Add(NewItem("未播放的剧集", "Demo.Unplayed", "FluentVideo20Regular"));
        SideNavigationItems.Add(NewItem("正在播放", "Demo.NowPlaying", "FluentPlay20Regular"));
    }

    private static MenuItemViewModel NewItem(string header, string key, string? icon) => new()
    {
        MenuHeader = header,
        Key = key,
        MenuIconName = icon,
    };
}
