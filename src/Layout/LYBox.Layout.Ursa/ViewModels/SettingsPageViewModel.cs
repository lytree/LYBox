using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Messages;
using LYBox.Plugin.Shared.Models;
using LYBox.Plugin.Shared.Services;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace LYBox.Layout.Ursa.ViewModels;

public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsService? _settingsService;
    private readonly ILocalizationService? _localizationService;

    /// <summary>
    /// 通过 Key 索引 SettingEntry 引用，便于插件更新特定条目的副标题。
    /// </summary>
    private readonly Dictionary<string, SettingEntryViewModel> _entriesByKey = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<SettingsGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatusText = string.Empty;

    public SettingsPageViewModel()
    {
    }

    public SettingsPageViewModel(ISettingsService settingsService, ILocalizationService? localizationService = null)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        WeakReferenceMessenger.Default.Register<SettingsPageViewModel, SettingStatusChangedMessage>(this, OnSettingStatusChanged);
        LoadSettings();
    }

    [RelayCommand]
    public void Refresh()
    {
        LoadSettings();
    }

    [RelayCommand]
    public void Save()
    {
        if (_settingsService == null) return;

        try
        {
            foreach (var group in Groups)
            {
                foreach (var entry in group.Items.Where(e => e.IsDirty))
                {
                    _settingsService.SetValue(entry.Key, entry.GetCurrentValue());
                    entry.MarkSaved();
                }
            }

            IsDirty = false;
            SaveStatusText = _localizationService?.GetString("SAVED", "已保存") ?? "已保存";

            ApplyRuntimeSettings();
        }
        catch (Exception)
        {
            SaveStatusText = _localizationService?.GetString("SAVE_FAILED", "保存失败") ?? "保存失败";
        }
    }

    [RelayCommand]
    public void Reset()
    {
        if (_settingsService == null) return;

        foreach (var group in Groups)
        {
            foreach (var entry in group.Items)
            {
                entry.ResetToSaved();
            }
        }

        IsDirty = false;
        SaveStatusText = string.Empty;
    }

    private void ApplyRuntimeSettings()
    {
        if (_settingsService == null) return;

        var theme = _settingsService.GetValue("App.Theme");
        if (theme != null)
        {
            var app = Application.Current;
            if (app is not null)
            {
                app.RequestedThemeVariant = theme switch
                {
                    "Light" => ThemeVariant.Light,
                    "Dark" => ThemeVariant.Dark,
                    _ => ThemeVariant.Default
                };
            }
        }

        var locale = _settingsService.GetValue("App.Locale");
        if (!string.IsNullOrEmpty(locale))
        {
            try
            {
                var culture = locale == "Default"
                    ? CultureInfo.CurrentUICulture
                    : new CultureInfo(locale);
                if (_localizationService is not null)
                {
                    _localizationService.SetCulture(culture);
                }
            }
            catch (CultureNotFoundException) { }
        }
    }

    internal void MarkDirty()
    {
        IsDirty = true;
        SaveStatusText = string.Empty;
    }

    private void LoadSettings()
    {
        if (_settingsService == null) return;

        Groups.Clear();
        _entriesByKey.Clear();
        IsDirty = false;
        SaveStatusText = string.Empty;

        var allSettings = _settingsService.GetAllSettings();
        var grouped = allSettings.GroupBy(s => s.GroupName);

        foreach (var group in grouped.OrderBy(g => g.Min(s => s.GroupOrder)))
        {
            var localizedName = ResolveGroupName(group.Key);
            var groupVm = new SettingsGroupViewModel(localizedName, _settingsService, this);
            foreach (var setting in group.OrderBy(s => s.ItemOrder))
            {
                var localizedSetting = LocalizeSettingItem(setting);
                var entry = CreateEntry(localizedSetting, _settingsService, this);
                _entriesByKey[setting.Key] = entry;
                groupVm.Items.Add(entry);
            }
            Groups.Add(groupVm);
        }
    }

    private void OnSettingStatusChanged(SettingsPageViewModel recipient, SettingStatusChangedMessage message)
    {
        if (_entriesByKey.TryGetValue(message.Key, out var entry))
        {
            switch (entry)
            {
                case ActionSettingEntryViewModel action:
                    action.UpdateSubtitle(message.Text);
                    break;
                case ReadOnlySettingEntryViewModel readOnly:
                    readOnly.UpdateText(message.Text);
                    break;
            }
        }
    }

    private string ResolveGroupName(string groupName)
    {
        var key = $"GROUP_{groupName.ToUpperInvariant()}";
        var resolved = _localizationService?.GetString(key);
        return resolved == key ? groupName : resolved ?? groupName;
    }

    private SettingItem LocalizeSettingItem(SettingItem setting)
    {
        var displayNameKey = $"SETTING_{setting.Key.Replace(".", "_").ToUpperInvariant()}";
        var descKey = $"SETTING_{setting.Key.Replace(".", "_").ToUpperInvariant()}_DESC";

        var displayName = _localizationService?.GetString(displayNameKey);
        if (displayName != displayNameKey)
            setting.DisplayName = displayName!;

        var desc = _localizationService?.GetString(descKey);
        if (desc != descKey)
            setting.Description = desc;

        return setting;
    }

    private SettingEntryViewModel CreateEntry(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent)
    {
        return setting.SettingType switch
        {
            SettingType.Text => new TextSettingEntryViewModel(setting, settingsService, parent),
            SettingType.Switch => new SwitchSettingEntryViewModel(setting, settingsService, parent),
            SettingType.Dropdown => new DropdownSettingEntryViewModel(setting, settingsService, parent),
            SettingType.Path => new PathSettingEntryViewModel(setting, settingsService, parent, _localizationService),
            SettingType.ReadOnly => new ReadOnlySettingEntryViewModel(setting, settingsService, parent, _localizationService),
            SettingType.Action => new ActionSettingEntryViewModel(setting, settingsService, parent),
            _ => new TextSettingEntryViewModel(setting, settingsService, parent)
        };
    }
}

public class SettingsGroupViewModel : ViewModelBase
{
    public string GroupName { get; }
    public ObservableCollection<SettingEntryViewModel> Items { get; } = [];
    private readonly ISettingsService _settingsService;

    public SettingsGroupViewModel(string groupName, ISettingsService settingsService, SettingsPageViewModel parent)
    {
        GroupName = groupName;
        _settingsService = settingsService;
    }
}

public abstract partial class SettingEntryViewModel : ViewModelBase
{
    protected readonly ISettingsService SettingsService;
    protected readonly SettingItem Setting;
    protected readonly SettingsPageViewModel Parent;

    public string Key => Setting.Key;
    public string DisplayName => Setting.DisplayName;
    public string? Description => Setting.Description;

    [ObservableProperty] private bool _isDirty;

    public SettingEntryViewModel(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent)
    {
        Setting = setting;
        SettingsService = settingsService;
        Parent = parent;
    }

    public abstract object? GetCurrentValue();
    public abstract void ResetToSaved();
    public abstract void MarkSaved();

    protected void OnValueChanged()
    {
        IsDirty = true;
        Parent.MarkDirty();
    }
}

public partial class TextSettingEntryViewModel : SettingEntryViewModel
{
    public string PlaceholderText => Setting.PlaceholderText ?? "";

    [ObservableProperty] private string _textValue;
    private string _savedValue;

    public TextSettingEntryViewModel(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent)
        : base(setting, settingsService, parent)
    {
        _textValue = setting.RawValue;
        _savedValue = setting.RawValue;
    }

    public override object? GetCurrentValue() => TextValue;

    public override void ResetToSaved()
    {
        TextValue = _savedValue;
        IsDirty = false;
    }

    public override void MarkSaved()
    {
        _savedValue = TextValue;
        IsDirty = false;
    }

    partial void OnTextValueChanged(string value) => OnValueChanged();
}

public partial class SwitchSettingEntryViewModel : SettingEntryViewModel
{
    [ObservableProperty] private bool _switchValue;
    private bool _savedValue;

    public SwitchSettingEntryViewModel(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent)
        : base(setting, settingsService, parent)
    {
        _switchValue = setting.GetValue<bool>();
        _savedValue = _switchValue;
    }

    public override object? GetCurrentValue() => SwitchValue;

    public override void ResetToSaved()
    {
        SwitchValue = _savedValue;
        IsDirty = false;
    }

    public override void MarkSaved()
    {
        _savedValue = SwitchValue;
        IsDirty = false;
    }

    partial void OnSwitchValueChanged(bool value) => OnValueChanged();
}

public partial class DropdownSettingEntryViewModel : SettingEntryViewModel
{
    [ObservableProperty] private string? _dropdownValue;
    public ObservableCollection<string> DropdownOptions { get; }
    private string? _savedValue;

    public DropdownSettingEntryViewModel(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent)
        : base(setting, settingsService, parent)
    {
        _dropdownValue = setting.RawValue;
        _savedValue = setting.RawValue;
        DropdownOptions = new ObservableCollection<string>(setting.GetOptions());
    }

    public override object? GetCurrentValue() => DropdownValue;

    public override void ResetToSaved()
    {
        DropdownValue = _savedValue;
        IsDirty = false;
    }

    public override void MarkSaved()
    {
        _savedValue = DropdownValue;
        IsDirty = false;
    }

    partial void OnDropdownValueChanged(string? value) => OnValueChanged();
}

public partial class PathSettingEntryViewModel : SettingEntryViewModel
{
    private readonly ILocalizationService? _localizationService;

    public string PlaceholderText => Setting.IsFolder
        ? (_localizationService?.GetString("SELECT_FOLDER_PATH", "选择文件夹路径...") ?? "选择文件夹路径...")
        : (Setting.PlaceholderText ?? _localizationService?.GetString("SELECT_FILE_PATH", "选择文件路径...") ?? "选择文件路径...");

    [ObservableProperty] private string _pathValue;
    private string _savedValue;

    public PathSettingEntryViewModel(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent, ILocalizationService? localizationService = null)
        : base(setting, settingsService, parent)
    {
        _localizationService = localizationService;
        _pathValue = setting.RawValue;
        _savedValue = setting.RawValue;
    }

    public override object? GetCurrentValue() => PathValue;

    public override void ResetToSaved()
    {
        PathValue = _savedValue;
        IsDirty = false;
    }

    public override void MarkSaved()
    {
        _savedValue = PathValue;
        IsDirty = false;
    }

    partial void OnPathValueChanged(string value) => OnValueChanged();

    [RelayCommand]
    private async Task Browse()
    {
        var topLevel = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel == null) return;

        var storageProvider = topLevel.StorageProvider;

        var title = _localizationService?.GetString("BROWSE", "浏览") ?? "浏览";

        if (Setting.IsFolder)
        {
            var folderResult = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"{title}{DisplayName}",
                AllowMultiple = false
            });

            if (folderResult.Count > 0)
            {
                PathValue = folderResult[0].TryGetLocalPath() ?? folderResult[0].Path.ToString();
            }
        }
        else
        {
            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"{title}{DisplayName}",
                AllowMultiple = false
            });

            if (result.Count > 0)
            {
                PathValue = result[0].TryGetLocalPath() ?? result[0].Path.ToString();
            }
        }
    }
}

/// <summary>
/// 只读条目（用于显示状态/提示文本，不参与保存）。
/// </summary>
public partial class ReadOnlySettingEntryViewModel : SettingEntryViewModel
{
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty] private string _textValue;

    public ReadOnlySettingEntryViewModel(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent, ILocalizationService? localizationService = null)
        : base(setting, settingsService, parent)
    {
        _localizationService = localizationService;
        _textValue = setting.RawValue;
    }

    public override object? GetCurrentValue() => TextValue;

    public override void ResetToSaved() { /* read-only */ }

    public override void MarkSaved() { /* read-only */ }

    /// <summary>
    /// 由 <see cref="SettingStatusChangedMessage"/> 触发：插件可借此更新文本（例如刷新运行期状态）。
    /// </summary>
    public void UpdateText(string value) => TextValue = value ?? string.Empty;
}

/// <summary>
/// 动作条目：渲染一个按钮 + 可选的状态/说明文本。点击按钮时通过
/// <see cref="SettingActionInvokedMessage"/> 通知插件执行对应动作。
/// 插件在自身 <c>RegisterAsync</c> 时通过 <c>WeakReferenceMessenger</c> 订阅。
/// </summary>
public partial class ActionSettingEntryViewModel : SettingEntryViewModel
{
    /// <summary>
    /// 可由插件通过 <c>WeakReferenceMessenger.Default.Send(new SettingActionInvokedMessage(actionId))</c>
    /// 或外部调用更新（例如刷新状态文本）。
    /// </summary>
    [ObservableProperty] private string _subtitle;

    public ActionSettingEntryViewModel(SettingItem setting, ISettingsService settingsService, SettingsPageViewModel parent)
        : base(setting, settingsService, parent)
    {
        _subtitle = setting.RawValue ?? string.Empty;
    }

    public override object? GetCurrentValue() => Subtitle;

    public override void ResetToSaved() { /* action has no persisted value */ }

    public override void MarkSaved() { /* action has no persisted value */ }

    public void UpdateSubtitle(string? value) => Subtitle = value ?? string.Empty;

    [RelayCommand]
    private void Invoke()
    {
        WeakReferenceMessenger.Default.Send(new SettingActionInvokedMessage(Setting.Key));
    }
}
