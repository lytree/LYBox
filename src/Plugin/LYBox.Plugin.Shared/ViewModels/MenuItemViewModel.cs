using System.Collections.ObjectModel;
using System.Windows.Input;
using LYBox.Plugin.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace LYBox.Plugin.Shared.ViewModels;

public enum ControlStatus
{
    New,
    Beta,
    Stable,
}

public partial class MenuItemViewModel : ViewModelBase
{
    private string? _menuHeader;
    private string? _displayHeader;

    public string? MenuHeader
    {
        get => _displayHeader ?? _menuHeader;
        set
        {
            _menuHeader = value;
            _displayHeader = ResolveHeader(value);
            OnPropertyChanged(nameof(MenuHeader));
        }
    }

    public string? RawHeader => _menuHeader;

    [ObservableProperty]
    private string? _menuIconName;
    public string? Key { get; set; }
    public string? Status { get; set; }
    public string? Group { get; set; }
    public int Order { get; set; }

    public bool IsSeparator { get; set; }
    public ObservableCollection<MenuItemViewModel> Children { get; set; } = [];

    public ICommand ActivateCommand { get; set; }

    public MenuItemViewModel()
    {
        ActivateCommand = new RelayCommand(OnActivate);
    }

    private void OnActivate()
    {
        if (IsSeparator || Key is null) return;
        WeakReferenceMessenger.Default.Send(Key, "JumpTo");
    }

    public void RefreshHeader()
    {
        _displayHeader = ResolveHeader(_menuHeader);
        OnPropertyChanged(nameof(MenuHeader));
        foreach (var child in Children)
            child.RefreshHeader();
    }

    private static readonly Lazy<ILocalizationService?> LocalizationServiceLazy = new(
        () => ServiceLocator.GetService<ILocalizationService>());

    private static string? ResolveHeader(string? header)
    {
        if (string.IsNullOrEmpty(header)) return header;
        try
        {
            var localizationService = LocalizationServiceLazy.Value;
            if (localizationService is not null)
            {
                var resolved = localizationService.GetString(header);
                return resolved == header ? header : resolved;
            }
        }
        catch { }
        return header;
    }
}
