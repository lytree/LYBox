using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace LYBox.Layout.Core.ViewModels;

public partial class IntroductionDemoViewModel : ObservableObject
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
    private List<ToolItem> _allItems = [];

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
    [ObservableProperty] private bool _hasNoResults;

    public IntroductionDemoViewModel()
    {
        _localizationService = ServiceLocator.TryGetService<ILocalizationService>(out var loc) ? loc : null;
        WelcomeTitle = _localizationService?.GetString("HOME_WELCOME_TITLE", "Welcome to LYBox") ?? "Welcome to LYBox";
        VersionText = ResolveVersionText();
        LoadTools();
        WeakReferenceMessenger.Default.Register<IntroductionDemoViewModel, string, string>(this, "SearchChanged", OnSearchChanged);
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

        ApplyFilter(SearchText);
    }

    private string? ResolveHeader(string? header)
    {
        if (string.IsNullOrEmpty(header)) return header;
        return _localizationService?.GetString(header) ?? header;
    }

    private void ApplyFilter(string? search)
    {
        List<ToolItem> result;
        if (string.IsNullOrWhiteSpace(search))
        {
            result = _allItems;
        }
        else
        {
            var keyword = search.Trim();
            result = [];
            foreach (var item in _allItems)
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
        HasNoResults = result.Count == 0;
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

    public string? Description { get; set; }

    /// <summary>卡片图标资源键（Fluent 图标名）。</summary>
    public string? IconName { get; set; }

    public string? Status { get; set; }

    /// <summary>
    /// 导航命令（由 ViewModel 注入）
    /// </summary>
    public System.Windows.Input.ICommand? NavigateCommand { get; set; }
}
