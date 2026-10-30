using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using Ursa.Controls;
using LYBox.Layout.Ursa.ViewModels;
using LYBox.Plugin.Shared.ViewModels;

namespace LYBox.Layout.Ursa.Views;

public partial class MainView : UserControl
{
    private MainViewViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();

        // Ursa NavMenu 只通过 Header/Icon/SubMenu/Command 四个 Binding 做数据映射，
        // IsSeparator 需要在这里手动从数据项同步到容器，否则分隔项会渲染成空白行。
        NavMenu.ContainerPrepared += OnNavContainerPrepared;
    }

    /// <summary>
    /// 折叠态侧边栏的放大镜入口：展开侧边栏并聚焦搜索框，与 Ctrl+F / Ctrl+K 行为一致。
    /// </summary>
    private void OnSearchRailButtonClick(object? sender, RoutedEventArgs e) => OpenSearch();

    /// <summary>
    /// 展开侧边栏并把焦点交给搜索框。展开时 NavMenu 有约 250ms 的宽度过渡，
    /// 等动画结束再聚焦，避免焦点落进仍在塌缩中的控件。
    /// </summary>
    public void OpenSearch()
    {
        if (DataContext is not MainViewViewModel viewModel)
            return;

        // 浮层态下侧边栏已经展开，直接聚焦；否则等宽度过渡结束再聚焦
        var alreadyExpanded = !viewModel.IsCollapsed && !viewModel.IsNavigationOverlay;
        viewModel.ExpandNavigation();

        if (alreadyExpanded)
            FocusSearchBox();
        else
            DispatcherTimer.RunOnce(FocusSearchBox, TimeSpan.FromMilliseconds(220));
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _viewModel?.UpdateNavigationLayout(e.NewSize.Width);
    }

    /// <summary>
    /// 初始化/布局完成后按窗口宽度应用导航三态（Hidden / Overlay / Expanded），
    /// 阈值与状态机由 ViewModel 的 UpdateNavigationLayout 统一维护。
    /// </summary>
    private void ApplySidebarWidthPolicy(double width)
    {
        _viewModel?.UpdateNavigationLayout(width);
    }

    /// <summary>
    /// 把 <see cref="MenuItemViewModel.IsSeparator"/> 同步到 <see cref="NavMenuItem"/> 容器。
    /// 二级菜单的容器由 NavMenuItem 自身生成，事件 sender 即对应 ItemsControl，
    /// 因此同一处理器可覆盖根菜单与子菜单。
    /// </summary>
    private static void OnNavContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not NavMenuItem menuItem)
            return;

        var item = menuItem.DataContext as MenuItemViewModel;
        if (item is null && sender is ItemsControl owner && e.Index >= 0 && e.Index < owner.ItemCount)
            item = owner.Items[e.Index] as MenuItemViewModel;

        // 双向同步：容器可能被回收复用，普通项必须显式复位，避免上一次的分隔状态残留。
        if (item is not null)
            menuItem.IsSeparator = item.IsSeparator;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachViewModel();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachViewModel();
    }

    /// <summary>
    /// 绑定 ViewModel 依赖的宿主服务（通知管理器）并按窗口宽度摆正侧边栏。
    /// DataContext 可能在挂在可视化树之前/之后才赋值，两条路径都要走一遍。
    /// </summary>
    private void AttachViewModel()
    {
        _viewModel = DataContext as MainViewViewModel;
        if (_viewModel is null)
            return;

        if (TopLevel.GetTopLevel(this) is { } topLevel && _viewModel.NotificationManager is null)
        {
            _viewModel.NotificationManager = WindowNotificationManager.TryGetNotificationManager(topLevel, out var manager)
                ? manager
                : new WindowNotificationManager(topLevel);
        }

        // 首次挂载时 Bounds 可能还是 0（窗口尺寸在附加之后才定下来），
        // 因此再延后到布局完成时补一次。
        ApplySidebarWidthPolicy(Bounds.Width);
        Dispatcher.UIThread.Post(() => ApplySidebarWidthPolicy(Bounds.Width), DispatcherPriority.Loaded);
    }
}
