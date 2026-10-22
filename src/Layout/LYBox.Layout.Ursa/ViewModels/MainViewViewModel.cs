using System.Collections.ObjectModel;
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
    private readonly ISettingsService? _settingsService;
    private ILocalizationService? _localizationService;

    public WindowNotificationManager? NotificationManager { get; set; }
    public MenuViewModel Menus { get; private set; }
    [ObservableProperty] private string? _settingText;
    [ObservableProperty] private string? _pluginText;
    [ObservableProperty] private object? _content;
    [ObservableProperty] private bool _isCollapsed;

    /// <summary>
    /// 侧边栏是否完全隐藏（参考 DevToys NavBarState.Hidden）。
    /// 窄窗（宽度 &lt; 640）且未打开浮层时置为 true，内容区占满整个窗口；
    /// 此时顶栏出现汉堡按钮用于唤出侧边栏。
    /// </summary>
    [ObservableProperty] private bool _isSidebarHidden;

    /// <summary>
    /// 窄窗浮层导航是否打开（参考 DevToys NavBarState.Overlay）：
    /// 窗口宽度 &lt; 1008 时侧边栏以浮层覆盖内容，并显示遮罩。
    /// </summary>
    [ObservableProperty] private bool _isNavigationOverlay;

    /// <summary>侧边栏为内容区预留的宽度：浮层/隐藏时 0，折叠 49，展开 320。</summary>
    public double NavigationReservedWidth => IsSidebarHidden || IsNavigationOverlay
        ? 0
        : IsCollapsed ? NavigationCollapsedWidth : NavigationExpandedWidth;

    private const double NavigationHiddenThreshold = 640;
    private const double NavigationOverlayThreshold = 1008;
    private const double NavigationExpandedWidth = 320;
    private const double NavigationCollapsedWidth = 49;

    /// <summary>最近一次已知的窗口宽度（默认按展开态初始化）。</summary>
    private double _navigationWidth = 1180;

    /// <summary>用户手动折叠的意图，仅在非浮层（宽度 &gt; 1008）时生效。</summary>
    private bool _prefersCollapsed;

    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private MenuItemViewModel? _selectedMenuItem;

    /// <summary>搜索建议全集（导航项拍平后的可打开页面）。</summary>
    public ObservableCollection<SearchSuggestion> SearchSuggestions { get; } = [];

    /// <summary>当前关键字命中的建议项（搜索框下拉列表的数据源）。</summary>
    public ObservableCollection<SearchSuggestion> FilteredSuggestions { get; } = [];

    /// <summary>搜索建议下拉是否展开。</summary>
    [ObservableProperty] private bool _isSearchSuggestionOpen;

    /// <summary>面包屑导航路径（首项为首页，末项为当前页）。</summary>
    public ObservableCollection<NavigationBreadcrumb> Breadcrumbs { get; } = [];

    /// <summary>仅当存在层级路径（非首页）时显示面包屑。</summary>
    public bool ShowBreadcrumbs => Breadcrumbs.Count > 1;

    /// <summary>设置页是否为当前页（页脚入口选中态）。</summary>
    public bool IsSettingsSelected => string.Equals(_currentKey, "Settings", StringComparison.Ordinal);

    /// <summary>插件管理页是否为当前页（页脚入口选中态）。</summary>
    public bool IsPluginsSelected => string.Equals(_currentKey, "PluginManagement", StringComparison.Ordinal);

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
        ExecuteSearch();
    }

    /// <summary>
    /// 搜索按钮 / 回车：跳过防抖，立即执行一次搜索。
    /// 下拉建议展开时回车等价于「选中第一条建议」（参考 DevToys AutoSuggestBox 的回车行为）。
    /// </summary>
    [RelayCommand]
    public void SubmitSearch()
    {
        _searchDebounceTimer?.Stop();
        if (IsSearchSuggestionOpen && FilteredSuggestions.Count > 0)
        {
            SelectSuggestion(FilteredSuggestions[0]);
            return;
        }

        ExecuteSearch();
    }

    /// <summary>
    /// 清除按钮 / Esc：清空关键字并立即恢复完整列表。
    /// 先赋值再停表，避免 OnSearchTextChanged 重新启动防抖导致重复派发。
    /// </summary>
    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
        _searchDebounceTimer?.Stop();
        ExecuteSearch();
    }

    /// <summary>
    /// 执行搜索：非空关键字时先跳到首页（确保首页已创建并注册消息接收器），
    /// 再把关键字广播给首页做卡片过滤；同时刷新侧边栏的搜索建议下拉。
    /// </summary>
    private void ExecuteSearch()
    {
        var value = SearchText;
        if (!string.IsNullOrWhiteSpace(value) && Content is not IntroductionDemoViewModel)
        {
            OnNavigation(this, "Introduction");
        }
        WeakReferenceMessenger.Default.Send(value ?? string.Empty, "SearchChanged");
        ApplySearchSuggestions(value);
    }

    /// <summary>
    /// 刷新搜索建议下拉：按名称 / Key / 分组过滤建议项，无命中时收起下拉。
    /// </summary>
    private void ApplySearchSuggestions(string? query)
    {
        FilteredSuggestions.Clear();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var keyword = query.Trim();
            foreach (var suggestion in SearchSuggestions)
            {
                if (suggestion.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || suggestion.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (suggestion.Group?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    FilteredSuggestions.Add(suggestion);
                }
            }
        }

        IsSearchSuggestionOpen = FilteredSuggestions.Count > 0;
    }

    /// <summary>
    /// 选中一条搜索建议：关闭下拉、清空关键字并直达对应页面。
    /// </summary>
    private void SelectSuggestion(SearchSuggestion suggestion)
    {
        IsSearchSuggestionOpen = false;
        SearchText = string.Empty;
        _searchDebounceTimer?.Stop();
        WeakReferenceMessenger.Default.Send(suggestion.Key, "JumpTo");
    }

    /// <summary>
    /// 按窗口宽度应用导航布局（参考 DevToys NavBarThresholds）：
    /// &lt; 640 隐藏（Hidden）；&lt; 1008 折叠为图标栏，点开时以浮层覆盖内容（Overlay）；
    /// 其余展开（Expanded）。
    /// </summary>
    public void UpdateNavigationLayout(double width)
    {
        if (width <= 0 || _navigationWidth == width)
            return;

        _navigationWidth = width;
        IsNavigationOverlay = false;
        ApplyNavigationLayout();
    }

    private void ApplyNavigationLayout()
    {
        IsSidebarHidden = _navigationWidth <= NavigationHiddenThreshold && !IsNavigationOverlay;
        IsCollapsed = !IsNavigationOverlay
                      && (_navigationWidth <= NavigationOverlayThreshold || _prefersCollapsed);
        OnPropertyChanged(nameof(NavigationReservedWidth));
    }

    /// <summary>
    /// 展开侧边栏：窄窗下打开浮层（Overlay），宽窗下退出折叠态。
    /// </summary>
    public void ExpandNavigation()
    {
        if (_navigationWidth <= NavigationOverlayThreshold)
            IsNavigationOverlay = true;
        else
            _prefersCollapsed = false;

        ApplyNavigationLayout();
    }

    /// <summary>
    /// 标题栏汉堡 / 侧栏开关：窄窗切换浮层显隐，宽窗切换折叠态并持久化用户意图。
    /// </summary>
    [RelayCommand]
    public void ToggleCollapse()
    {
        if (_navigationWidth <= NavigationOverlayThreshold)
        {
            IsNavigationOverlay = !IsNavigationOverlay;
        }
        else
        {
            _prefersCollapsed = !IsCollapsed;
            // 参考 DevToys 记住侧边栏状态：写入设置项，下次启动沿用
            _settingsService?.SetValue("App.SidebarCollapsed", _prefersCollapsed);
        }

        ApplyNavigationLayout();
    }

    /// <summary>点击遮罩关闭浮层导航。</summary>
    [RelayCommand]
    public void CloseNavigationOverlay()
    {
        IsNavigationOverlay = false;
        ApplyNavigationLayout();
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
        BuildSearchSuggestions();

        // 恢复上次的侧边栏状态（设置页「折叠侧边栏」开关写的就是这个键）。
        // 有持久化值时视为用户已作选择，避免被窗口宽度自适应逻辑改回去。
        _settingsService = ServiceLocator.GetService<ISettingsService>();
        if (_settingsService?.GetValue<bool>("App.SidebarCollapsed") is true)
        {
            _prefersCollapsed = true;
            IsCollapsed = true;
        }

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

    /// <summary>
    /// 构建搜索建议集：把菜单树拍平成可打开的页面列表（分组节点本身不作为建议项，
    /// 与首页工具卡片的口径一致）。
    /// </summary>
    private void BuildSearchSuggestions()
    {
        SearchSuggestions.Clear();
        foreach (var parent in Menus.MenuItems)
        {
            if (parent.IsSeparator || string.IsNullOrEmpty(parent.Key)) continue;

            if (parent.Children.Count == 0)
            {
                // 首页自身不作为建议项，避免自引用
                if (parent.Key != "Introduction")
                    AddSuggestion(parent, null);
                continue;
            }

            foreach (var child in parent.Children)
            {
                if (child.IsSeparator || string.IsNullOrEmpty(child.Key)) continue;
                AddSuggestion(child, parent.MenuHeader);
            }
        }
    }

    private void AddSuggestion(MenuItemViewModel item, string? group)
    {
        var suggestion = new SearchSuggestion
        {
            Name = item.MenuHeader ?? item.Key!,
            Key = item.Key!,
            IconName = string.IsNullOrWhiteSpace(item.MenuIconName)
                ? FallbackIcons.For(item.Key)
                : item.MenuIconName,
            Group = group
        };
        suggestion.ActivateCommand = new RelayCommand(() => SelectSuggestion(suggestion));
        SearchSuggestions.Add(suggestion);
    }

    private void OnCultureChanged(object? sender, System.Globalization.CultureInfo culture)
    {
        UpdateLocalizedStrings();
        Menus.RefreshHeaders();
        BuildSearchSuggestions();
        RefreshBreadcrumbs();
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
        // 浮层导航选中页面后自动收起（参考 DevToys NavigateAsync → CloseNavigationOverlay）
        CloseNavigationOverlay();
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsPluginsSelected));
        RefreshBreadcrumbs();
    }

    /// <summary>
    /// 重建面包屑：首项固定为首页（NAV_Introduction），随后按菜单层级追加到当前页。
    /// 末项标记为当前节点（不可点击）。
    /// </summary>
    private void RefreshBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var homeTitle = _localizationService?.GetString("NAV_Introduction", "All tools") ?? "All tools";
        var isHome = string.Equals(_currentKey, "Introduction", StringComparison.Ordinal);
        Breadcrumbs.Add(new NavigationBreadcrumb(homeTitle, "Introduction", Activate) { IsCurrent = isHome });

        if (!isHome && !string.IsNullOrEmpty(_currentKey))
        {
            var path = new List<MenuItemViewModel>();
            if (FindMenuPath(Menus.MenuItems, _currentKey!, path))
            {
                foreach (var item in path)
                    Breadcrumbs.Add(new NavigationBreadcrumb(item.MenuHeader ?? item.Key ?? string.Empty, item.Key, Activate));
            }
            else
            {
                Breadcrumbs.Add(new NavigationBreadcrumb(_currentKey!, null, Activate));
            }

            Breadcrumbs[^1].IsCurrent = true;
        }

        OnPropertyChanged(nameof(ShowBreadcrumbs));
    }

    private static bool FindMenuPath(IEnumerable<MenuItemViewModel> items, string key, List<MenuItemViewModel> path)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator) continue;
            path.Add(item);
            if (item.Key == key || FindMenuPath(item.Children, key, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
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

/// <summary>
/// 面包屑节点（参考 DevToys NavigationBreadcrumb）：标题 + 导航 Key，末项为当前页。
/// </summary>
public sealed class NavigationBreadcrumb
{
    public NavigationBreadcrumb(string title, string? key, Action<string>? navigate)
    {
        Title = title;
        Key = key;
        if (key is not null && navigate is not null)
            ActivateCommand = new RelayCommand(() => navigate(key));
    }

    public string Title { get; }

    public string? Key { get; }

    /// <summary>是否为当前页（当前节点不可点击）。</summary>
    public bool IsCurrent { get; set; }

    public IRelayCommand? ActivateCommand { get; }
}
