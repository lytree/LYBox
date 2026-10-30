using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Reflection;
using LYBox.Layout.Core.Services;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace LYBox.Layout.Core.ViewModels;

public partial class IntroductionDemoViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private double _ratingValue = 3.5;
    [ObservableProperty] private int _sliderValue = 50;
    [ObservableProperty] private IPAddress? _ipAddress = new IPAddress(new byte[] { 192, 168, 1, 1 });
    [ObservableProperty] private double _lowerValue = 20;
    [ObservableProperty] private double _upperValue = 80;
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private DateTime _endDate = DateTime.Today.AddDays(7);
    [ObservableProperty] private DateTime _dateTime = DateTime.Now;
    [ObservableProperty] private TimeSpan _startTime = new TimeSpan(9, 0, 0);
    [ObservableProperty] private TimeSpan _endTime = new TimeSpan(17, 0, 0);

    private readonly ILocalizationService? _localizationService;
    private readonly INavigationService? _navigationService;
    private readonly string? _groupKey;
    private List<ToolItem> _allItems = [];
    private bool _updatingCategories;
    private bool _disposed;

    /// <summary>
    /// 分类目录页的父级菜单 Key；为 null 时表示首页（全部工具）。
    /// </summary>
    public string? GroupKey => _groupKey;

    /// <summary>首页显示欢迎横幅；分类目录页只显示分类标题。</summary>
    public bool ShowWelcome => _groupKey is null;

    /// <summary>分类筛选标签（首项为「全部」，数量不足时不展示）。</summary>
    public ObservableCollection<ToolCategoryItem> Categories { get; } = [];

    /// <summary>当前选中的分类；GroupKey 为 null 表示「全部」。</summary>
    [ObservableProperty] private ToolCategoryItem? _selectedCategory;

    /// <summary>分类标签是否展示（首页且分类数足够多时）。</summary>
    [ObservableProperty] private bool _showCategories;

    /// <summary>「最近使用」工具卡片（仅首页、未筛选时展示）。</summary>
    [ObservableProperty] private IReadOnlyList<ToolItem> _recentItems = [];

    /// <summary>是否存在最近使用记录。</summary>
    [ObservableProperty] private bool _hasRecentTools;

    /// <summary>工具计数文案，形如「3 / 12 个工具」。</summary>
    [ObservableProperty] private string _toolCountText = string.Empty;

    /// <summary>是否存在搜索关键字或分类筛选。</summary>
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText)
                                    || SelectedCategory?.GroupKey is not null;

    /// <summary>
    /// 首页工具卡片数据源（已过滤）。整体替换引用只触发一次 PropertyChanged，
    /// 避免逐项增删导致网格反复重排。
    /// </summary>
    [ObservableProperty] private IReadOnlyList<ToolItem> _filteredItems = [];

    /// <summary>头图主标题（欢迎语）。</summary>
    [ObservableProperty] private string? _welcomeTitle;

    /// <summary>头图版本后缀，形如 v2.3.0-preview.11。</summary>
    [ObservableProperty] private string? _versionText;

    [ObservableProperty] private string? _searchText;

    /// <summary>搜索无结果（有搜索词但没有匹配项）。</summary>
    [ObservableProperty] private bool _hasNoResults;

    /// <summary>未安装任何工具（没有搜索词且工具列表为空）。</summary>
    [ObservableProperty] private bool _hasNoTools;

    /// <summary>
    /// 工具卡片网格的列数。参考 DevToys GridView 的 ItemMinWidth=330：
    /// 由页面按可用宽度推算后回写，宽度不足时自动降列。
    /// </summary>
    [ObservableProperty] private int _gridColumns = 3;

    public IntroductionDemoViewModel()
        : this(null)
    {
    }

    public IntroductionDemoViewModel(string? groupKey)
    {
        _groupKey = groupKey;
        _localizationService = ServiceLocator.TryGetService<ILocalizationService>(out var loc) ? loc : null;
        _navigationService = ServiceLocator.TryGetService<INavigationService>(out var nav) ? nav : null;
        if (_navigationService is not null)
            _navigationService.RecentNavigationsChanged += OnRecentNavigationsChanged;

        if (_groupKey is null)
        {
            WelcomeTitle = _localizationService?.GetString("HOME_WELCOME_TITLE", "Welcome to LYBox") ?? "Welcome to LYBox";
            VersionText = ResolveVersionText();
        }
        else
        {
            // 分类目录页把分组名作为横幅标题，不再重复展示应用版本
            WelcomeTitle = ResolveGroupTitle(_groupKey);
            VersionText = null;
        }

        LoadTools();
        WeakReferenceMessenger.Default.Register<IntroductionDemoViewModel, string, string>(this, "SearchChanged", OnSearchChanged);
        WeakReferenceMessenger.Default.Register<IntroductionDemoViewModel, string, string>(this, "MenuChanged", OnMenuChanged);
    }

    private void OnMenuChanged(IntroductionDemoViewModel recipient, string message)
    {
        // 菜单结构/文案发生变化：重载工具并保留可用的筛选状态
        LoadTools();
        RefreshGroupTitle();
    }

    private void OnRecentNavigationsChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
            RefreshRecentItems();
    }

    public void Dispose()
    {
        _disposed = true;
        if (_navigationService is not null)
            _navigationService.RecentNavigationsChanged -= OnRecentNavigationsChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    /// <summary>分类菜单的本地化标题（找不到时回退 Key）。</summary>
    private string ResolveGroupTitle(string key)
    {
        if (ServiceLocator.TryGetService<IMenuConfigurationService>(out var menuConfig) && menuConfig is not null)
        {
            var item = menuConfig.GetMenuItemByKey(key);
            if (item is not null)
                return ResolveHeader(item.MenuHeader) ?? item.Key ?? key;
        }
        return key;
    }

    private static string ResolveVersionText()
    {
        var informational = (Assembly.GetEntryAssembly() ?? typeof(IntroductionDemoViewModel).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return string.Empty;

        // 去掉 SourceLink 追加的 +commit 后缀，仅展示语义版本
        var plusIndex = informational.IndexOf('+');
        if (plusIndex > 0)
            informational = informational[..plusIndex];

        return informational.StartsWith('v') ? informational : "v" + informational;
    }

    private void OnSearchChanged(IntroductionDemoViewModel recipient, string message)
    {
        SearchText = message;
    }

    partial void OnSearchTextChanged(string? value)
    {
        ApplyFilter(value);
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    partial void OnSelectedCategoryChanged(ToolCategoryItem? value)
    {
        if (_updatingCategories)
            return;

        OnPropertyChanged(nameof(HasActiveFilters));
        RefreshCategories();
        ApplyFilter(SearchText);
    }

    /// <summary>
    /// 从菜单配置服务加载所有工具并拍平为单列表：首页网格不再按分组插入标题，
    /// 分组名作为卡片的次级描述保留（插件未提供描述时的兜底）。
    /// </summary>
    private void LoadTools()
    {
        _allItems = [];
        if (!ServiceLocator.TryGetService<IMenuConfigurationService>(out var menuConfig) || menuConfig is null)
        {
            ApplyFilter(SearchText);
            return;
        }

        var navCmd = NavigateCommand;
        foreach (var parent in menuConfig.GetMenuStructure())
        {
            if (parent.IsSeparator || string.IsNullOrEmpty(parent.Key)) continue;
            // 分类目录页只收录该父级菜单下属的工具
            if (_groupKey is not null && parent.Key != _groupKey) continue;

            var category = ResolveHeader(parent.MenuHeader) ?? parent.Key ?? string.Empty;

            // 一级条目若无子项（如「控件示例」），本身即一个可打开的页面，直接作为工具卡片展示；
            // 首页自身（Introduction）排除，避免自引用。
            if (parent.Children.Count == 0)
            {
                if (parent.Key != "Introduction")
                {
                    _allItems.Add(new ToolItem
                    {
                        Name = category,
                        Key = parent.Key,
                        GroupKey = parent.Key,
                        Category = category,
                        // 叶子页面没有独立描述，标题已表达含义，不再重复同名文案
                        Description = string.IsNullOrWhiteSpace(parent.Description) ? null : parent.Description,
                        IconName = string.IsNullOrWhiteSpace(parent.MenuIconName)
                            ? FallbackIcons.For(parent.Key)
                            : parent.MenuIconName,
                        Status = parent.Status,
                        NavigateCommand = navCmd
                    });
                }
                continue;
            }

            foreach (var child in parent.Children)
            {
                if (child.IsSeparator || string.IsNullOrEmpty(child.Key)) continue;
                var name = ResolveHeader(child.MenuHeader) ?? child.Key ?? string.Empty;
                _allItems.Add(new ToolItem
                {
                    Name = name,
                    Key = child.Key!,
                    GroupKey = parent.Key,
                    Category = category,
                    Description = !string.IsNullOrWhiteSpace(child.Description)
                        ? child.Description
                        : string.Equals(category, name, StringComparison.Ordinal) ? null : category,
                    IconName = string.IsNullOrWhiteSpace(child.MenuIconName)
                        ? FallbackIcons.For(child.Key)
                        : child.MenuIconName,
                    Status = child.Status,
                    NavigateCommand = navCmd
                });
            }
        }

        RefreshCategories();
        ApplyFilter(SearchText);
    }

    private string? ResolveHeader(string? header)
    {
        if (string.IsNullOrEmpty(header)) return header;
        return _localizationService?.GetString(header) ?? header;
    }

    private void ApplyFilter(string? search)
    {
        // 分类筛选先于关键字：选中「全部」时 GroupKey 为 null，不额外过滤
        IEnumerable<ToolItem> source = _allItems;
        if (SelectedCategory?.GroupKey is { } groupKey)
            source = source.Where(item => item.GroupKey == groupKey);

        List<ToolItem> result;
        if (string.IsNullOrWhiteSpace(search))
        {
            result = source as List<ToolItem> ?? source.ToList();
        }
        else
        {
            var keyword = search.Trim();
            result = [];
            foreach (var item in source)
            {
                if (item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(item);
                }
            }
        }
        FilteredItems = result;

        // 空状态区分两种语义：未装任何工具 vs. 筛选/搜索无匹配
        var filtering = !string.IsNullOrWhiteSpace(search) || SelectedCategory?.GroupKey is not null;
        HasNoResults = filtering && result.Count == 0;
        HasNoTools = !filtering && result.Count == 0;

        ToolCountText = string.Format(
            _localizationService?.GetString("HOME_TOOL_COUNT", "{0} / {1} 个工具") ?? "{0} / {1} 个工具",
            result.Count,
            _allItems.Count);

        RefreshRecentItems();
    }

    /// <summary>
    /// 按工具所属分组重建分类筛选标签。首项固定为「全部」，
    /// 分类数不足三个时不展示筛选行（参考 DevToys 首页分类过滤器）。
    /// </summary>
    private void RefreshCategories()
    {
        // 分类目录页本身已限定在一个分组内，不再提供分类筛选
        if (!ShowWelcome)
        {
            _updatingCategories = true;
            try
            {
                Categories.Clear();
                ShowCategories = false;
                SelectedCategory = null;
            }
            finally
            {
                _updatingCategories = false;
            }
            return;
        }

        var previousGroupKey = SelectedCategory?.GroupKey;
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        var titles = new Dictionary<string, string>();
        foreach (var item in _allItems)
        {
            var key = item.GroupKey ?? string.Empty;
            if (counts.TryAdd(key, 1))
            {
                order.Add(key);
                titles[key] = item.Category;
            }
            else
            {
                counts[key]++;
            }
        }

        _updatingCategories = true;
        try
        {
            Categories.Clear();
            if (ShowWelcome)
            {
                Categories.Add(new ToolCategoryItem
                {
                    GroupKey = null,
                    Title = _localizationService?.GetString("HOME_ALL_CATEGORIES", "All tools") ?? "All tools",
                    Count = _allItems.Count
                });
            }

            foreach (var key in order)
            {
                Categories.Add(new ToolCategoryItem
                {
                    GroupKey = key,
                    Title = titles[key],
                    Count = counts[key]
                });
            }

            ShowCategories = ShowWelcome && Categories.Count > 2;

            // 重建后恢复原选中分类；原分类已消失时回退到「全部」
            SelectedCategory = previousGroupKey is null
                ? Categories.FirstOrDefault()
                : Categories.FirstOrDefault(category => category.GroupKey == previousGroupKey)
                  ?? Categories.FirstOrDefault();
        }
        finally
        {
            _updatingCategories = false;
        }
    }

    /// <summary>刷新「最近使用」：仅首页且未筛选时展示，最多六项。</summary>
    private void RefreshRecentItems()
    {
        if (!ShowWelcome || HasActiveFilters || _allItems.Count == 0)
        {
            RecentItems = [];
            HasRecentTools = false;
            return;
        }

        var items = new List<ToolItem>();
        if (_navigationService is not null)
        {
            foreach (var key in _navigationService.GetRecentNavigationKeys())
            {
                var match = _allItems.FirstOrDefault(item => item.Key == key);
                if (match is not null)
                    items.Add(match);
                if (items.Count >= 6)
                    break;
            }
        }

        RecentItems = items;
        HasRecentTools = items.Count > 0;
    }

    /// <summary>语言或菜单文案变化后刷新横幅标题。</summary>
    private void RefreshGroupTitle()
    {
        WelcomeTitle = _groupKey is null
            ? _localizationService?.GetString("HOME_WELCOME_TITLE", "Welcome to LYBox") ?? "Welcome to LYBox"
            : ResolveGroupTitle(_groupKey);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedCategory = Categories.FirstOrDefault();
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        WeakReferenceMessenger.Default.Send(key, "JumpTo");
    }

    public void RefreshGroups()
    {
        LoadTools();
    }
}

/// <summary>
/// 单个工具项（首页卡片数据源）
/// </summary>
public sealed class ToolItem
{
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    /// <summary>所属分组（分类）名称，作为卡片次级描述的兜底。</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>所属父级菜单 Key，用于分类筛选匹配（不随语言变化）。</summary>
    public string? GroupKey { get; set; }

    public string? Description { get; set; }

    /// <summary>卡片图标资源键（Fluent 图标名）。</summary>
    public string? IconName { get; set; }

    public string? Status { get; set; }

    /// <summary>
    /// 导航命令（由 ViewModel 注入）
    /// </summary>
    public System.Windows.Input.ICommand? NavigateCommand { get; set; }
}

/// <summary>
/// 首页分类筛选标签（「全部」或某个工具分组）。
/// </summary>
public sealed class ToolCategoryItem
{
    /// <summary>父级菜单 Key；null 表示「全部」。</summary>
    public string? GroupKey { get; init; }

    public string Title { get; init; } = string.Empty;

    public int Count { get; init; }

    /// <summary>标签上的计数文案。</summary>
    public string CountText => Count.ToString();
}
