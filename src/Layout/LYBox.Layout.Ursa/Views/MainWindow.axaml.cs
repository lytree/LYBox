using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.Services;
using LYBox.Plugin.Shared.Web;
using LYBox.Layout.Core.Services;
using LYBox.Layout.Ursa.ViewModels;
using Ursa.Controls;

namespace LYBox.Layout.Ursa.Views;

public partial class MainWindow : UrsaWindow
{
    public WindowNotificationManager? NotificationManager { get; set; }

    /// <summary>
    /// 退出旁路标志：托盘菜单「退出」命令在调用 desktop.Shutdown() 前置为 true，
    /// 使关闭流程跳过「最小化到托盘」与退出确认对话框，允许窗口真正关闭。
    /// </summary>
    public static bool ForceExit { get; set; }

    /// <summary>
    /// WinUI 3 Mica 材质需换为半透明 brush 的 shell 层资源键。
    /// 同时覆盖 Fluent* 语义键（MainView/NavPane）与 Ursa 模板静态别名键
    /// （TitleBarBackground/WindowBackground，经 StaticResource 解析，无法级联）。
    /// </summary>
    private static readonly string[] s_backdropBrushKeys =
    [
        "FluentShellBackgroundBrush",
        "FluentNavPaneBackgroundBrush",
        "FluentTitleBarBackgroundBrush",
        "TitleBarBackground",
        "WindowBackground",
    ];

    public MainWindow()
    {
        InitializeComponent();

        NotificationManager = new WindowNotificationManager(this) { MaxItems = 3 };

        // Mica 可用性与主题切换都可能发生在窗口生命周期内，两者变化时重算 shell brush
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty || e.Property == ActualThemeVariantProperty)
                ApplyBackdropBrushes();
        };
    }

    /// <summary>
    /// 窗口级快捷键（在冒泡阶段统一处理，控件自身未消费时生效）：
    /// Ctrl+K / Ctrl+F 聚焦搜索；Esc 关闭浮层导航或清空搜索；
    /// Alt+Left 与鼠标侧键后退触发页面返回。
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (MainViewControl.DataContext is not MainViewViewModel viewModel)
        {
            base.OnKeyDown(e);
            return;
        }

        var modifiers = e.KeyModifiers;
        var hasControl = (modifiers & KeyModifiers.Control) != 0;
        var hasAlt = (modifiers & KeyModifiers.Alt) != 0;

        if (hasControl && (e.Key == Key.K || e.Key == Key.F))
        {
            MainViewControl.OpenSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (viewModel.IsNavigationOverlay)
            {
                viewModel.CloseNavigationOverlay();
                e.Handled = true;
                return;
            }

            if (!string.IsNullOrEmpty(viewModel.SearchText))
            {
                viewModel.ClearSearch();
                e.Handled = true;
                return;
            }
        }

        if ((hasAlt && e.Key == Key.Left) || e.Key == Key.BrowserBack)
        {
            if (viewModel.CanGoBack)
            {
                viewModel.Back();
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // DWM 在窗口显示后才确定最终透明等级（Win11 → Mica；其余回退 None/Blur）
        ApplyBackdropBrushes();
    }

    /// <summary>
    /// WinUI 3 Mica 适配：ActualTransparencyLevel 达到 Mica 时将 shell 层 brush 换为
    /// 半透明 FluentShellMicaBrush（透出桌面壁纸采样）；否则移除覆盖，
    /// 回退主题字典中的不透明 WinUI 3 配色（Linux/macOS/低版本 Windows 路径）。
    /// </summary>
    private void ApplyBackdropBrushes()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.Mica)
        {
            foreach (var key in s_backdropBrushKeys)
                Resources.Remove(key);
            return;
        }

        if (this.TryGetResource("FluentShellMicaBrush", ActualThemeVariant, out var mica))
        {
            var micaBrush = mica as IBrush ?? Brushes.Transparent;
            foreach (var key in s_backdropBrushKeys)
                Resources[key] = micaBrush;
        }
    }

    protected override async Task<bool> CanClose()
    {
        // 强制退出（托盘菜单退出路径）：直接放行
        if (ForceExit)
            return true;

        // 设置驱动：开启「关闭时最小化到托盘」时隐藏窗口而非退出
        if (ServiceLocator.TryGetService<ISettingsService>(out var settings) && settings is not null)
        {
            var minimizeToTray = settings.GetValue<bool>("App.MinimizeToTray");
            if (minimizeToTray)
            {
                this.Hide();
                return false;
            }
        }

        // 本地化文案
        var loc = ServiceLocator.TryGetService<ILocalizationService>(out var service) ? service : null;
        var message = loc?.GetString("EXIT_CONFIRM_MESSAGE", "Are you sure you want to exit?") ?? "Are you sure you want to exit?";
        var title = loc?.GetString("EXIT_CONFIRM_TITLE", "Exit") ?? "Exit";

        // Ursa 标准 Overlay Dialog 确认（参照 Ursa.Demo MainWindow 实现）
        // 弹窗期间抑制原生层 WebView，避免其 HWND 盖住 Overlay 内容
        using var webViewLayerSuppression = PluginWebViewLayerGuard.Suppress();
        var result = await OverlayMessageBox.ShowAsync(message, title, button: MessageBoxButton.YesNo);
        if (result == MessageBoxResult.Yes)
        {
            // 用户确认退出：标记 ForceExit 并触发 Shutdown，绕过下次 CanClose 拦截
            ForceExit = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        return false;
    }
}
