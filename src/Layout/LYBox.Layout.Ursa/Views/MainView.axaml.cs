using Avalonia;
using Avalonia.Controls;
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
        _viewModel = DataContext as MainViewViewModel;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _viewModel is null)
            return;
        _viewModel.NotificationManager = WindowNotificationManager.TryGetNotificationManager(topLevel, out var manager)
            ? manager
            : new WindowNotificationManager(topLevel);
    }
}
