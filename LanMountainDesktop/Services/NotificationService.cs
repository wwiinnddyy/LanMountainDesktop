using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;

namespace LanMountainDesktop.Services;

public enum NotificationPosition
{
    TopLeft = 0,
    TopRight = 1,
    TopCenter = 2,
    BottomLeft = 3,
    BottomRight = 4,
    BottomCenter = 5,
    Center = 6
}

public enum NotificationSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public readonly record struct NotificationContent(
    string Title,
    string? Message = null,
    Stream? IconStream = null,
    string? IconPath = null,
    Bitmap? IconBitmap = null,
    NotificationSeverity Severity = NotificationSeverity.Info,
    NotificationPosition Position = NotificationPosition.TopRight,
    TimeSpan? Duration = null,
    Action? OnClick = null,
    string? PrimaryButtonText = null,
    string? SecondaryButtonText = null,
    string? CloseButtonText = null,
    Action? OnPrimaryButtonClick = null,
    Action? OnSecondaryButtonClick = null)
{
    public TimeSpan EffectiveDuration => Duration ?? TimeSpan.FromSeconds(4);

    /// <summary>
    /// Indicates whether this notification should be shown as a dialog (center position)
    /// or as a toast notification (other positions)
    /// </summary>
    public bool IsDialogNotification => Position == NotificationPosition.Center;
}

public interface INotificationService
{
    void Show(NotificationContent content);

    Task<FAContentDialogResult> ShowDialogAsync(NotificationContent content);

    void ShowInfo(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight);

    void ShowSuccess(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight);

    void ShowWarning(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight);

    void ShowError(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight);

    Task<FAContentDialogResult> ShowDialogInfoAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel");

    Task<FAContentDialogResult> ShowDialogSuccessAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel");

    Task<FAContentDialogResult> ShowDialogWarningAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel");

    Task<FAContentDialogResult> ShowDialogErrorAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel");
}

internal sealed class NotificationService : INotificationService
{
    private readonly IAppearanceThemeService? _appearanceThemeService;
    private readonly NotificationWindowManager _windowManager;

    public NotificationService(IAppearanceThemeService? appearanceThemeService = null)
    {
        _appearanceThemeService = appearanceThemeService;
        _windowManager = NotificationWindowManager.Instance;
    }

    public void Show(NotificationContent content)
    {
        if (!IsNotificationEnabled())
        {
            return;
        }

        if (content.IsDialogNotification)
        {
            Dispatcher.UIThread.Post(() => ShowDialogWindow(content), DispatcherPriority.Normal);
            return;
        }

        Dispatcher.UIThread.Post(() => ShowCore(content), DispatcherPriority.Normal);
    }

    private void ShowDialogWindow(NotificationContent content)
    {
        var window = new NotificationDialogWindow();
        window.Initialize(content, _appearanceThemeService);

        Screen? screen = null;
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            screen = desktop.MainWindow?.Screens?.Primary;
        }
        var workingArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        window.Measure(Size.Infinity);
        var windowWidth = window.DesiredSize.Width > 0 ? window.DesiredSize.Width : 400;
        var windowHeight = window.DesiredSize.Height > 0 ? window.DesiredSize.Height : 200;

        var centerX = workingArea.X + (workingArea.Width - (int)Math.Round(windowWidth)) / 2;
        var centerY = workingArea.Y + (workingArea.Height - (int)Math.Round(windowHeight)) / 2;
        window.Position = new PixelPoint(centerX, centerY);

        window.Show();

        _ = Task.Run(async () =>
        {
            if (window.CompletionSource is not null)
            {
                await window.CompletionSource.Task;
            }
        });
    }

    public async Task<FAContentDialogResult> ShowDialogAsync(NotificationContent content)
    {
        if (!IsNotificationEnabled())
        {
            return FAContentDialogResult.None;
        }

        return await Dispatcher.UIThread.InvokeAsync(() => ShowDialogCoreAsync(content));
    }

    private async Task<FAContentDialogResult> ShowDialogCoreAsync(NotificationContent content)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            AppLogger.Warn("Notification", "Cannot show dialog notification: main window not found");
            return FAContentDialogResult.None;
        }

        var dialog = new FAContentDialog
        {
            Title = content.Title,
            Content = content.Message ?? string.Empty,
            PrimaryButtonText = content.PrimaryButtonText,
            SecondaryButtonText = content.SecondaryButtonText,
            CloseButtonText = content.CloseButtonText,
            DefaultButton = !string.IsNullOrEmpty(content.PrimaryButtonText) ? FAContentDialogButton.Primary :
                           !string.IsNullOrEmpty(content.SecondaryButtonText) ? FAContentDialogButton.Secondary :
                           FAContentDialogButton.Close
        };

        var result = await dialog.ShowAsync(mainWindow);

        // Execute callbacks based on result
        switch (result)
        {
            case FAContentDialogResult.Primary:
                content.OnPrimaryButtonClick?.Invoke();
                break;
            case FAContentDialogResult.Secondary:
                content.OnSecondaryButtonClick?.Invoke();
                break;
        }

        return result;
    }

    private static bool IsNotificationEnabled()
    {
        try
        {
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            var snapshot = settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(PluginSdk.SettingsScope.App);
            return snapshot.NotificationEnabled;
        }
        catch
        {
            // 濠电姷顣介埀顒€鍟块埀顒€缍婇幃妯诲緞鐏炴儳鐝伴柣鐘叉处瑜板啰寰婇崹顕呯唵闁诡垱澹嗙花鍧楁偡濞嗘瑧鐣甸柡浣哥Т閻ｆ繈宕熼鐐殿偧闂佽崵濮抽梽宥夊磹閺囥垹绠氶幖娣妽閸嬨劑鏌曟繛鐐澒闁稿鎸搁～婵囨綇閳轰礁缁?
            return true;
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private void ShowCore(NotificationContent content)
    {
        var viewModel = new NotificationViewModel
        {
            Title = content.Title,
            Message = content.Message,
            Severity = content.Severity,
            Position = content.Position,
            Duration = content.EffectiveDuration,
            OnClick = content.OnClick
        };

        if (content.IconBitmap is not null)
        {
            viewModel.Icon = content.IconBitmap;
        }
        else if (content.IconStream is not null)
        {
            viewModel.Icon = new Bitmap(content.IconStream);
        }
        else if (!string.IsNullOrEmpty(content.IconPath))
        {
            try
            {
                viewModel.Icon = new Bitmap(content.IconPath);
            }
            catch
            {
                AppLogger.Warn("Notification", $"Failed to load icon from path: {content.IconPath}");
            }
        }

        _windowManager.ShowNotification(viewModel, _appearanceThemeService);
    }

    public void ShowInfo(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight)
    {
        Show(new NotificationContent(title, message, Severity: NotificationSeverity.Info, Position: position));
    }

    public void ShowSuccess(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight)
    {
        Show(new NotificationContent(title, message, Severity: NotificationSeverity.Success, Position: position));
    }

    public void ShowWarning(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight)
    {
        Show(new NotificationContent(title, message, Severity: NotificationSeverity.Warning, Position: position));
    }

    public void ShowError(string title, string? message = null,
        NotificationPosition position = NotificationPosition.TopRight)
    {
        Show(new NotificationContent(title, message, Severity: NotificationSeverity.Error, Position: position));
    }

    public Task<FAContentDialogResult> ShowDialogInfoAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel")
    {
        return ShowDialogAsync(new NotificationContent(
            title,
            message,
            Severity: NotificationSeverity.Info,
            Position: NotificationPosition.Center,
            PrimaryButtonText: primaryButtonText,
            CloseButtonText: closeButtonText));
    }

    public Task<FAContentDialogResult> ShowDialogSuccessAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel")
    {
        return ShowDialogAsync(new NotificationContent(
            title,
            message,
            Severity: NotificationSeverity.Success,
            Position: NotificationPosition.Center,
            PrimaryButtonText: primaryButtonText,
            CloseButtonText: closeButtonText));
    }

    public Task<FAContentDialogResult> ShowDialogWarningAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel")
    {
        return ShowDialogAsync(new NotificationContent(
            title,
            message,
            Severity: NotificationSeverity.Warning,
            Position: NotificationPosition.Center,
            PrimaryButtonText: primaryButtonText,
            CloseButtonText: closeButtonText));
    }

    public Task<FAContentDialogResult> ShowDialogErrorAsync(string title, string? message = null,
        string? primaryButtonText = "OK", string? closeButtonText = "Cancel")
    {
        return ShowDialogAsync(new NotificationContent(
            title,
            message,
            Severity: NotificationSeverity.Error,
            Position: NotificationPosition.Center,
            PrimaryButtonText: primaryButtonText,
            CloseButtonText: closeButtonText));
    }
}

internal sealed class NotificationWindowManager
{
    private static NotificationWindowManager? _instance;
    public static NotificationWindowManager Instance => _instance ??= new NotificationWindowManager();

    private readonly Dictionary<NotificationPosition, List<NotificationWindow>> _windowsByPosition = new();
    private const double Margin = 12;
    private const double Spacing = 6;

    private NotificationWindowManager()
    {
        foreach (var position in Enum.GetValues<NotificationPosition>())
        {
            _windowsByPosition[position] = new List<NotificationWindow>();
        }
    }

    public void ShowNotification(NotificationViewModel viewModel, IAppearanceThemeService? themeService)
    {
        var position = viewModel.Position;
        var windows = _windowsByPosition[position];

        // 濠电偛顕慨鏉戭潩閿曞偆鏁婇柡鍥╁Х绾剧偓銇勯弮鈧Σ鎺楀储椤掑嫭鍋ｉ柛銉憾閸ゆ瑧鎲搁弶鎸庡枠鐎殿喚鏁婚崺鈧い鎺嶇缁剁偟鎲搁弮鍫濈劦妞ゆ帊鐒﹂惃鎴︽煟閹垮嫮绡€鐎殿噮鍋呯€靛ジ寮堕幋鐑嗕画
        var maxNotifications = GetMaxNotificationsPerPosition();

        if (windows.Count >= maxNotifications)
        {
            var oldestWindow = windows[0];
            windows.RemoveAt(0);
            oldestWindow.Close();
        }

        var window = new NotificationWindow();
        window.Initialize(viewModel, themeService);
        window.Closed += OnWindowClosed;

        windows.Add(window);
        UpdateWindowPositions(position);

        window.ShowWithAnimationAsync();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not NotificationWindow window) return;

        var position = window.NotificationPositionValue;
        var windows = _windowsByPosition.GetValueOrDefault(position);
        if (windows is null) return;

        windows.Remove(window);
        window.Closed -= OnWindowClosed;

        UpdateWindowPositions(position);
    }

    private static int GetMaxNotificationsPerPosition()
    {
        try
        {
            // 濠电偛顕慨瀛橆殽閹间礁鐭楅煫鍥ㄦ磻濞岊亪鏌嶈閸撴盯骞忕€ｎ喖绀堢憸蹇涘几閸岀偞鐓涢柛顐ｇ箘瀛濇繝娈垮枤閸犳劗绮欐径鎰垫晣闁宠棄妫楀▓娲⒑閸涘﹦鎳勯柣妤侇殔閵嗘帡鎳滈棃娑氱獮閻熸粍妫冮崺鈧い鎺嶇劍閻ㄦ垿鏌ｉ幙鍕瘈鐎殿噮鍋呯€靛ジ寮堕幋鐑嗕画
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            var snapshot = settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(PluginSdk.SettingsScope.App);
            return snapshot.NotificationMaxPerPosition > 0 ? snapshot.NotificationMaxPerPosition : 5;
        }
        catch
        {
            return 5;
        }
    }

    private void UpdateWindowPositions(NotificationPosition position)
    {
        var windows = _windowsByPosition.GetValueOrDefault(position);
        if (windows is null || windows.Count == 0) return;

        var screen = GetPrimaryScreen();
        var workingArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1d;

        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var targetPosition = CalculateWindowPosition(window, position, workingArea, scale, i);
            window.Position = targetPosition;
        }
    }

    private PixelPoint CalculateWindowPosition(
        NotificationWindow window,
        NotificationPosition position,
        PixelRect workingArea,
        double scale,
        int stackIndex)
    {
        window.Measure(Size.Infinity);
        var windowWidthDip = window.Bounds.Width > 0
            ? window.Bounds.Width
            : window.DesiredSize.Width > 0 ? window.DesiredSize.Width : 320;
        var windowHeightDip = window.Bounds.Height > 0
            ? window.Bounds.Height
            : window.DesiredSize.Height > 0 ? window.DesiredSize.Height : 80;

        var windowWidth = (int)Math.Round(windowWidthDip * scale);
        var windowHeight = (int)Math.Round(windowHeightDip * scale);

        var margin = (int)Math.Round(Margin * scale);
        var spacing = (int)Math.Round(Spacing * scale);
        var stackedOffset = stackIndex * (windowHeight + spacing);

        return position switch
        {
            NotificationPosition.TopLeft => new PixelPoint(
                workingArea.X + margin,
                workingArea.Y + margin + stackedOffset),

            NotificationPosition.TopRight => new PixelPoint(
                workingArea.Right - windowWidth - margin,
                workingArea.Y + margin + stackedOffset),

            NotificationPosition.TopCenter => new PixelPoint(
                workingArea.X + (workingArea.Width - windowWidth) / 2,
                workingArea.Y + margin + stackedOffset),

            NotificationPosition.BottomLeft => new PixelPoint(
                workingArea.X + margin,
                workingArea.Bottom - windowHeight - margin - stackedOffset),

            NotificationPosition.BottomRight => new PixelPoint(
                workingArea.Right - windowWidth - margin,
                workingArea.Bottom - windowHeight - margin - stackedOffset),

            NotificationPosition.BottomCenter => new PixelPoint(
                workingArea.X + (workingArea.Width - windowWidth) / 2,
                workingArea.Bottom - windowHeight - margin - stackedOffset),

            NotificationPosition.Center => new PixelPoint(
                workingArea.X + (workingArea.Width - windowWidth) / 2,
                workingArea.Y + (workingArea.Height - windowHeight) / 2),

            _ => new PixelPoint(
                workingArea.Right - windowWidth - margin,
                workingArea.Y + margin + stackedOffset)
        };
    }

    private static Screen? GetPrimaryScreen()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Screens?.Primary;
        }
        return null;
    }

    public void ApplyThemeToAllWindows(AppearanceThemeSnapshot snapshot)
    {
        foreach (var windows in _windowsByPosition.Values)
        {
            foreach (var window in windows.ToList())
            {
                try
                {
                    window.RequestedThemeVariant = snapshot.IsNightMode ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
                }
                catch
                {
                }
            }
        }
    }
}
