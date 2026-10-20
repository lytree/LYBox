using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Services;
using LYBox.Plugin.Shared.ViewModels;
using LYBox.Layout.Core.Services;
using LYBox.Layout.Core.ViewModels;
using LYBox.Layout.Ursa.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Threading;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LYBox.Layout.Ursa.ViewModels;

public partial class MainViewViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IMenuConfigurationService _menuConfigurationService;
    private ILocalizationService? _localizationService;

    public WindowNotificationManager? NotificationManager { get; set; }
    public MenuViewModel Menus { get; private set; }
    [ObservableProperty] private string? _settingText;
    [ObservableProperty] private string? _pluginText;
    [ObservableProperty] private object? _content;
    [ObservableProperty] private bool _isCollapsed;
    [ObservableProperty] private bool _isSidebarHidden;
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private MenuItemViewModel? _selectedMenuItem;

    /// <summary>是否可以回退到上一页面（控制标题栏返回按钮的可用态）。</summary>
    [ObservableProperty] private bool _canGoBack;

    /// <summary>回退历史栈：仅记录真实发生的页面切换，Back 出栈时不回写。</summary>
    private readonly Stack<string> _backStack = new();

    /// <summary>当前已显示的页面 Key，用于判断是否需要记录历史。</summary>
    private string? _currentKey;

    // 搜索防抖：避免每次击键都触发导航和过滤
    private DispatcherTimer? _searchDebounceTimer;

    partial void OnSearchTextChanged(string? value)
    {
        // 重启防抖计时器（300ms），仅当计时器到期后才执行搜索
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300),
            DispatcherPriority.Background, OnSearchDebounce);
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounce(object? sender, EventArgs e)
    {
        _searchDebounceTimer!.Stop();
        var value = SearchText;
        // 搜索时自动跳转到首页，确保首页已创建并注册消息接收器
        if (!string.IsNullOrWhiteSpace(value) && Content is not IntroductionDemoViewModel)
        {
            OnNavigation(this, "Introduction");
        }
        // 将搜索文本发送到首页（IntroductionDemoViewModel），用于过滤工具卡片
        WeakReferenceMessenger.Default.Send(value ?? string.Empty, "SearchChanged");
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarHidden = !IsSidebarHidden;
    }

    [RelayCommand]
    public void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }

    [RelayCommand]
    public void Activate(string key)
    {
        WeakReferenceMessenger.Default.Send(key, "JumpTo");
    }

    public MainViewViewModel(INavigationService navigationService, IMenuConfigurationService menuConfigurationService)
    {
        _navigationService = navigationService;
        _menuConfigurationService = menuConfigurationService;
        Menus = new MenuViewModel();
        BuildMenuItems();

        _localizationService = ServiceLocator.GetService<ILocalizationService>();
        if (_localizationService is not null)
            _localizationService.CultureChanged += OnCultureChanged;

        UpdateLocalizedStrings();
        Menus.RefreshHeaders();
        WeakReferenceMessenger.Default.Register<MainViewViewModel, string, string>(this, "JumpTo", OnNavigation);
        OnNavigation(this, "Introduction");
    }

    /// <summary>
    /// 构建导航菜单：按 <see cref="MenuItemViewModel.Group"/> 变化自动插入分隔线。
    /// 未设置 Group 的菜单项（含当前默认菜单）不会产生任何分隔线，行为与旧版一致。
    /// </summary>
    private void BuildMenuItems()
    {
        Menus.MenuItems.Clear();
        MenuItemViewModel? previous = null;
        foreach (var item in _menuConfigurationService.GetMenuStructure())
        {
            var startsNewGroup = previous is not null
                                 && !string.IsNullOrEmpty(item.Group)
                                 && !string.IsNullOrEmpty(previous.Group)
                                 && item.Group != previous.Group;
            if (startsNewGroup)
                Menus.MenuItems.Add(new MenuItemViewModel { IsSeparator = true });

            Menus.MenuItems.Add(item);
            previous = item;
        }
    }

    private void OnCultureChanged(object? sender, System.Globalization.CultureInfo culture)
    {
        UpdateLocalizedStrings();
        Menus.RefreshHeaders();
    }

    private void UpdateLocalizedStrings()
    {
        SettingText = _localizationService?.GetString("NAV_Settings", "Settings");
        PluginText = _localizationService?.GetString("NAV_Plugins", "Plugins");
    }

    partial void OnContentChanged(object? value)
    {
        if (value is IDisposable disposable && !ReferenceEquals(disposable, this))
        {
            _disposableContent = disposable;
        }
    }

    private IDisposable? _disposableContent;

    private void OnNavigation(MainViewViewModel vm, string s)
    {
        NavigateTo(s, recordHistory: true);
    }

    /// <summary>
    /// 执行导航。recordHistory=true 时把当前页压入回退栈（Back 触发的导航传 false，
    /// 避免「返回」又把来路压回去导致来回跳转）。
    /// </summary>
    private void NavigateTo(string key, bool recordHistory)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (recordHistory && !string.IsNullOrEmpty(_currentKey) && _currentKey != key)
        {
            _backStack.Push(_currentKey!);
            CanGoBack = true;
        }

        if (_disposableContent is not null && !ReferenceEquals(_disposableContent, this))
        {
            _disposableContent.Dispose();
            _disposableContent = null;
        }

        // 先同步 _lastNavigatedKey：SelectedMenuItem 赋值会触发 OnSelectedMenuItemChanged，
        // 若 Key 不一致会再次派发 JumpTo，造成重复创建 ViewModel。
        _lastNavigatedKey = key;
        Content = _navigationService.CreateViewModel(key);
        // 使用扁平索引 O(1) 查找菜单项，替代递归遍历
        SelectedMenuItem = _menuConfigurationService.GetMenuItemByKey(key);
        _currentKey = key;
    }

    /// <summary>标题栏返回按钮：回到上一页面；无历史时回到首页。</summary>
    [RelayCommand]
    public void Back()
    {
        if (_backStack.Count == 0)
        {
            // 标题栏返回入口常显，无历史时回到首页
            if (!string.Equals(_currentKey, "Introduction", StringComparison.Ordinal))
                NavigateTo("Introduction", recordHistory: false);
            return;
        }

        var key = _backStack.Pop();
        CanGoBack = _backStack.Count > 0;
        NavigateTo(key, recordHistory: false);
    }

    /// <summary>
    /// 当用户在 NavigationView 中点击菜单项时触发导航。
    /// 程序化设置 SelectedMenuItem 时（如 OnNavigation）也会触发，需避免递归：
    /// 若新选中项的 Key 与当前 Content 对应的 Key 一致则跳过。
    /// </summary>
    partial void OnSelectedMenuItemChanged(MenuItemViewModel? value)
    {
        if (value is null || string.IsNullOrEmpty(value.Key))
            return;
        // 仅当 Key 变化时才发送导航消息，避免循环
        if (_lastNavigatedKey != value.Key)
        {
            _lastNavigatedKey = value.Key;
            WeakReferenceMessenger.Default.Send(value.Key, "JumpTo");
        }
    }

    private string? _lastNavigatedKey;

    partial void OnIsCollapsedChanged(bool value)
    {
        SettingText = value ? null : _localizationService?.GetString("NAV_Settings", "Settings");
        PluginText = value ? null : _localizationService?.GetString("NAV_Plugins", "Plugins");
    }

    public override void Dispose()
    {
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = null;
        WeakReferenceMessenger.Default.Unregister<string, string>(this, "JumpTo");
        if (_localizationService is not null)
            _localizationService.CultureChanged -= OnCultureChanged;
        base.Dispose();
    }
}
