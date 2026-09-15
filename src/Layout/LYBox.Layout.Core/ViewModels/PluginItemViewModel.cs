using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LYBox.Layout.Core.ViewModels;

/// <summary>
/// 插件列表项 ViewModel，承载单个插件的展示状态与操作可用性。
/// 提取到 Core 供 Ursa/Fluent 两个布局共享。
/// </summary>
public partial class PluginItemViewModel : ViewModelBase
{
    [ObservableProperty] private string _pluginId = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private PluginState _state;
    [ObservableProperty] private bool _isBuiltIn;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private string _stateColor = "#808080";
    [ObservableProperty] private bool _canEnable;
    [ObservableProperty] private bool _canDisable;
    [ObservableProperty] private bool _canUninstall;
    [ObservableProperty] private bool _canCancelUpgrade;
    [ObservableProperty] private string? _pendingUpgradeVersion;
    [ObservableProperty] private string _supportedPlatformsText = string.Empty;

    private ILocalizationService? _localizationService;

    public PluginItemViewModel(
        PluginInfo info,
        ILocalizationService? localizationService = null,
        bool isReadOnly = false)
    {
        _localizationService = localizationService;
        UpdateFrom(info, localizationService, isReadOnly);
    }

    public void UpdateFrom(
        PluginInfo info,
        ILocalizationService? localizationService = null,
        bool isReadOnly = false)
    {
        if (localizationService is not null)
            _localizationService = localizationService;

        PluginId = info.PluginId;
        Name = info.Name;
        Version = info.Version;
        Author = info.Author;
        Description = info.Description;
        State = info.State;
        IsBuiltIn = info.IsBuiltIn;
        IsReadOnly = isReadOnly;
        ErrorMessage = info.ErrorMessage;

        (StateText, StateColor) = info.State switch
        {
            PluginState.Loaded => (_localizationService?.GetString("STATE_LOADED", "Loaded") ?? "Loaded", "#4CAF50"),
            PluginState.Installed => (_localizationService?.GetString("STATE_INSTALLED", "Installed (restart to load)") ?? "Installed (restart to load)", "#2196F3"),
            PluginState.Disabled => (_localizationService?.GetString("STATE_DISABLED", "Disabled") ?? "Disabled", "#FF9800"),
            PluginState.PendingUninstall => (_localizationService?.GetString("STATE_PENDING_UNINSTALL", "Pending Uninstall") ?? "Pending Uninstall", "#9C27B0"),
            PluginState.PendingUpgrade => (_localizationService?.GetString("STATE_PENDING_UPGRADE", "Pending Upgrade") ?? "Pending Upgrade", "#00BCD4"),
            PluginState.Error => (_localizationService?.GetString("STATE_ERROR", "Error") ?? "Error", "#F44336"),
            _ => (_localizationService?.GetString("STATE_NOT_INSTALLED", "Not Installed") ?? "Not Installed", "#808080")
        };

        CanEnable = !isReadOnly && info.State == PluginState.Disabled;
        CanDisable = !isReadOnly && (info.State == PluginState.Loaded || info.State == PluginState.Installed);
        CanUninstall = !isReadOnly && !info.IsBuiltIn &&
                        info.State != PluginState.PendingUninstall &&
                        info.State != PluginState.PendingUpgrade;
        CanCancelUpgrade = !isReadOnly && info.State == PluginState.PendingUpgrade;
        PendingUpgradeVersion = info.State == PluginState.PendingUpgrade
            ? info.PendingUpgradeVersion
            : null;

        // 平台支持：把 ["windows","linux"] 列表渲染成"Windows / Linux"；
        // null / 空 / 仅 "*" 视为"All Platforms"（无约束）。
        SupportedPlatformsText = FormatSupportedPlatforms(info.SupportedPlatforms, _localizationService);
    }

    /// <summary>
    /// 把 <see cref="PluginInfo.SupportedPlatforms"/> 渲染成 UI 友好字符串。
    /// 空 / null / 仅 "*" → "All Platforms"（本地化优先）。
    /// </summary>
    private static string FormatSupportedPlatforms(
        IReadOnlyList<string>? platforms,
        ILocalizationService? loc)
    {
        var fallbackAll = loc?.GetString("PLATFORM_ALL", "All Platforms") ?? "All Platforms";
        if (platforms is null || platforms.Count == 0) return fallbackAll;

        var concrete = new List<string>(platforms.Count);
        var hasWildcard = false;
        foreach (var p in platforms)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var token = p.Trim();
            if (token == "*") { hasWildcard = true; continue; }
            concrete.Add(LocalizePlatformToken(token, loc));
        }

        if (concrete.Count == 0) return fallbackAll;
        if (hasWildcard)
        {
            var localizedAll = loc?.GetString("PLATFORM_ALL", "All Platforms") ?? "All Platforms";
            return $"{string.Join(" / ", concrete)} / {localizedAll}";
        }
        return string.Join(" / ", concrete);
    }

    private static string LocalizePlatformToken(string token, ILocalizationService? loc) =>
        token.ToLowerInvariant() switch
        {
            "windows" => loc?.GetString("PLATFORM_WINDOWS", "Windows") ?? "Windows",
            "linux" => loc?.GetString("PLATFORM_LINUX", "Linux") ?? "Linux",
            "osx" or "macos" or "mac" => loc?.GetString("PLATFORM_MACOS", "macOS") ?? "macOS",
            _ => token
        };
}
