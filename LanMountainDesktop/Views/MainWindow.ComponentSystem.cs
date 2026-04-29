using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;
using PathShape = Avalonia.Controls.Shapes.Path;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIcon = FluentIcons.Avalonia.SymbolIcon;

namespace LanMountainDesktop.Views;

public partial class MainWindow : Window
{
    private readonly List<DesktopComponentPlacementSnapshot> _desktopComponentPlacements = [];
    private readonly Dictionary<int, Grid> _desktopPageComponentGrids = new();

    private const string DesktopComponentClass = "desktop-component";
    private const string DesktopComponentHostClass = "desktop-component-host";
    private const string DesktopComponentContentHostTag = "desktop-component-content-host";
    private const string DesktopComponentResizeHandleTag = "desktop-component-resize-handle";

    private string? _componentLibraryActiveCategoryId;
    private int _componentLibraryCategoryIndex;
    private int _componentLibraryComponentIndex;
    private double _componentLibraryCategoryPageWidth;
    private double _componentLibraryComponentPageWidth;
    private TranslateTransform? _componentLibraryCategoryHostTransform;
    private TranslateTransform? _componentLibraryComponentHostTransform;
    private IReadOnlyList<ComponentLibraryCategory> _componentLibraryCategories = Array.Empty<ComponentLibraryCategory>();
    private IReadOnlyList<ComponentLibraryComponentEntry> _componentLibraryActiveComponents = Array.Empty<ComponentLibraryComponentEntry>();
    private bool _isComponentLibraryCategoryGestureActive;
    private bool _isComponentLibraryComponentGestureActive;
    private Point _componentLibraryCategoryGestureStartPoint;
    private Point _componentLibraryCategoryGestureCurrentPoint;
    private double _componentLibraryCategoryGestureBaseOffset;
    private Point _componentLibraryComponentGestureStartPoint;
    private Point _componentLibraryComponentGestureCurrentPoint;
    private double _componentLibraryComponentGestureBaseOffset;

    private sealed record ComponentLibraryCategory(
        string Id,
        Symbol Icon,
        string Title,
        IReadOnlyList<ComponentLibraryComponentEntry> Components);

    private readonly record struct ComponentScaleRule(int WidthUnit, int HeightUnit, int MinScale);
    private readonly record struct TaskbarProfilePopupMaterialPalette(
        Color SurfaceColor,
        Color OutlineColor,
        Color AvatarSurfaceColor,
        Color PrimaryTextColor,
        Color AccentColor,
        Color HoverColor,
        Color PressedColor,
        Color DividerColor);

    private readonly IPowerManagementService _powerService = PowerManagementServiceFactory.GetOrCreate();
    private bool _isPowerMenuOpen;
    private bool _isPowerMenuAnimating;

    private void InitializeTaskbarProfileFlyout()
    {
        if (TaskbarProfileButton is null || TaskbarProfilePopup is null)
        {
            return;
        }

        TaskbarProfilePopup.PlacementTarget = TaskbarProfileButton;
        RefreshTaskbarProfilePresentation();
    }

    private void RefreshTaskbarProfilePresentation()
    {
        if (TaskbarProfileButton is null)
        {
            return;
        }

        var profile = _currentUserProfileService.GetCurrentProfile();
        ApplyProfileAvatarVisual(TaskbarProfileAvatarImage, TaskbarProfileAvatarFallbackText, profile);
        ApplyProfileAvatarVisual(TaskbarProfileHeaderAvatarImage, TaskbarProfileHeaderAvatarFallbackText, profile);
        TaskbarProfileDisplayNameTextBlock.Text = profile.DisplayName;
        TaskbarProfileSettingsActionTextBlock.Text = L("tooltip.open_settings", "Settings");
        TaskbarProfileDesktopEditActionTextBlock.Text = L("button.component_library", "Edit Desktop");
        TaskbarProfilePowerActionTextBlock.Text = L("power.menu", "Power");
        TaskbarPowerTitleTextBlock.Text = L("power.title", "Power");
        TaskbarPowerBackTextBlock.Text = L("power.back", "Back");
        PowerShutdownTextBlock.Text = L("power.shutdown", "Shutdown");
        PowerRestartTextBlock.Text = L("power.restart", "Restart");
        PowerLogoutTextBlock.Text = L("power.logout", "Log Out");
        PowerSleepTextBlock.Text = L("power.sleep", "Sleep");
        PowerLockTextBlock.Text = L("power.lock_screen", "Lock Screen");

        UpdatePowerMenuVisibility();
        ApplyTaskbarProfilePopupTheme(_appearanceThemeService.GetCurrent());

        ToolTip.SetTip(TaskbarProfileButton, profile.DisplayName);
    }

    private static void ApplyProfileAvatarVisual(Image? image, TextBlock? fallbackText, CurrentUserProfileSnapshot profile)
    {
        if (image is not null)
        {
            image.Source = profile.AvatarBitmap;
            image.IsVisible = profile.AvatarBitmap is not null;
        }

        if (fallbackText is not null)
        {
            fallbackText.Text = profile.FallbackMonogram;
            fallbackText.IsVisible = profile.AvatarBitmap is null;
        }
    }

    private void ApplyTaskbarProfilePopupTheme(AppearanceThemeSnapshot snapshot)
    {
        if (TaskbarProfilePopupPanel is null)
        {
            return;
        }

        var palette = BuildTaskbarProfilePopupMaterialPalette(snapshot);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupSurfaceBrush", palette.SurfaceColor);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupOutlineBrush", palette.OutlineColor);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupAvatarSurfaceBrush", palette.AvatarSurfaceColor);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupTextBrush", palette.PrimaryTextColor);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupAccentBrush", palette.AccentColor);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupActionHoverBrush", palette.HoverColor);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupActionPressedBrush", palette.PressedColor);
        SetTaskbarProfilePopupBrush("TaskbarProfilePopupDividerBrush", palette.DividerColor);
    }

    private void SetTaskbarProfilePopupBrush(string resourceKey, Color color)
    {
        TaskbarProfilePopupPanel.Resources[resourceKey] = new SolidColorBrush(color);
    }

    private static TaskbarProfilePopupMaterialPalette BuildTaskbarProfilePopupMaterialPalette(AppearanceThemeSnapshot snapshot)
    {
        var primary = snapshot.MonetPalette.Primary.A > 0
            ? snapshot.MonetPalette.Primary
            : snapshot.AccentColor;
        if (primary == default)
        {
            primary = Color.Parse("#FF6750A4");
        }

        var neutral = snapshot.MonetPalette.Neutral.A > 0
            ? snapshot.MonetPalette.Neutral
            : snapshot.IsNightMode
                ? Color.Parse("#FF1A1F27")
                : Color.Parse("#FFF7F9FD");
        var neutralVariant = snapshot.MonetPalette.NeutralVariant.A > 0
            ? snapshot.MonetPalette.NeutralVariant
            : ColorMath.Blend(neutral, primary, snapshot.IsNightMode ? 0.20 : 0.10);

        var surfaceBase = snapshot.IsNightMode
            ? Color.Parse("#FF141A22")
            : Color.Parse("#FFFCFCFF");
        var surface = ColorMath.Blend(surfaceBase, neutral, snapshot.IsNightMode ? 0.52 : 0.46);
        surface = ColorMath.Blend(surface, primary, snapshot.IsNightMode ? 0.12 : 0.05);

        var outlineSeed = snapshot.IsNightMode
            ? ColorMath.Blend(neutralVariant, Color.Parse("#FFFFFFFF"), 0.28)
            : ColorMath.Blend(neutralVariant, Color.Parse("#FF111827"), 0.12);
        var outline = Color.FromArgb(
            snapshot.IsNightMode ? (byte)0x82 : (byte)0x38,
            outlineSeed.R,
            outlineSeed.G,
            outlineSeed.B);

        var primaryTextPreferred = snapshot.IsNightMode
            ? Color.Parse("#FFF4F7FB")
            : Color.Parse("#FF14171B");
        var primaryText = ColorMath.EnsureContrast(primaryTextPreferred, surface, 7.0);
        var accent = ColorMath.EnsureContrast(primary, surface, 3.0);
        var avatarSurface = ColorMath.Blend(surface, primary, snapshot.IsNightMode ? 0.26 : 0.16);
        var hover = ColorMath.Blend(surface, primary, snapshot.IsNightMode ? 0.20 : 0.10);
        var pressed = ColorMath.Blend(surface, primary, snapshot.IsNightMode ? 0.30 : 0.18);
        var divider = Color.FromArgb(
            snapshot.IsNightMode ? (byte)0x44 : (byte)0x20,
            outlineSeed.R,
            outlineSeed.G,
            outlineSeed.B);

        return new TaskbarProfilePopupMaterialPalette(
            surface,
            outline,
            avatarSurface,
            primaryText,
            accent,
            hover,
            pressed,
            divider);
    }

    private void OnTaskbarProfileButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (TaskbarProfileButton is null || TaskbarProfilePopup is null)
        {
            return;
        }

        if (TaskbarProfilePopup.IsOpen)
        {
            TaskbarProfilePopup.IsOpen = false;
            return;
        }

        ResetPowerMenuState();
        RefreshTaskbarProfilePresentation();
        TaskbarProfilePopup.IsOpen = true;
    }

    private void OnOpenComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (TaskbarProfilePopup is not null)
        {
            TaskbarProfilePopup.IsOpen = false;
        }
        ExecuteTaskbarDesktopEditAction();
    }

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (TaskbarProfilePopup is not null)
        {
            TaskbarProfilePopup.IsOpen = false;
        }
        ExecuteTaskbarSettingsAction();
    }

    private void ExecuteTaskbarDesktopEditAction()
    {
        if (_isComponentLibraryOpen)
        {
            CloseComponentLibraryWindow(reopenSettings: false);
            return;
        }

        var settingsWindowService = (Application.Current as App)?.SettingsWindowService;
        _reopenSettingsAfterComponentLibraryClose = settingsWindowService?.IsOpen == true;
        if (_reopenSettingsAfterComponentLibraryClose)
        {
            settingsWindowService?.Close();
        }

        OpenComponentLibraryWindow();
    }

    private void ExecuteTaskbarSettingsAction()
    {
        if (_isComponentLibraryOpen)
        {
            CloseComponentLibraryWindow(reopenSettings: false);
        }

        (Application.Current as App)?.OpenIndependentSettingsModule("MainWindowTaskbar");
    }

    private void OnPowerMenuEnterClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        EnterPowerMenu();
    }

    private void OnPowerMenuBackClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ExitPowerMenu();
    }

    private void ResetPowerMenuState()
    {
        _isPowerMenuOpen = false;
        _isPowerMenuAnimating = false;

        if (TaskbarProfileMainPanel is not null)
        {
            TaskbarProfileMainPanel.IsVisible = true;
            TaskbarProfileMainPanel.Opacity = 1d;
        }

        if (TaskbarProfilePowerPanel is not null)
        {
            TaskbarProfilePowerPanel.IsVisible = false;
            TaskbarProfilePowerPanel.Opacity = 0d;
            var transform = TaskbarProfilePowerPanel.RenderTransform as TranslateTransform;
            if (transform is not null) transform.X = 340d;
        }
    }

    private void UpdatePowerMenuVisibility()
    {
        var supported = _powerService.IsShutdownSupported ||
                        _powerService.IsRestartSupported ||
                        _powerService.IsLogoutSupported ||
                        _powerService.IsSleepSupported ||
                        _powerService.IsLockSupported;

        if (TaskbarProfilePowerActionButton is not null)
        {
            TaskbarProfilePowerActionButton.IsVisible = supported;
        }
    }

    private async void EnterPowerMenu()
    {
        if (_isPowerMenuAnimating || _isPowerMenuOpen || TaskbarProfileMainPanel is null || TaskbarProfilePowerPanel is null)
            return;

        _isPowerMenuAnimating = true;

        TaskbarProfilePowerPanel.IsVisible = true;
        TaskbarProfilePowerPanel.Opacity = 0d;
        var powerTransform = TaskbarProfilePowerPanel.RenderTransform as TranslateTransform;
        if (powerTransform is not null) powerTransform.X = 340d;

        await Task.Delay(16);

        TaskbarProfileMainPanel.Opacity = 0d;
        TaskbarProfilePowerPanel.Opacity = 1d;
        if (powerTransform is not null) powerTransform.X = 0d;

        await Task.Delay(280);

        TaskbarProfileMainPanel.IsVisible = false;
        _isPowerMenuOpen = true;
        _isPowerMenuAnimating = false;
    }

    private async void ExitPowerMenu()
    {
        if (_isPowerMenuAnimating || !_isPowerMenuOpen || TaskbarProfileMainPanel is null || TaskbarProfilePowerPanel is null)
            return;

        _isPowerMenuAnimating = true;

        TaskbarProfileMainPanel.IsVisible = true;
        TaskbarProfileMainPanel.Opacity = 0d;
        var powerTransform = TaskbarProfilePowerPanel.RenderTransform as TranslateTransform;
        if (powerTransform is not null) powerTransform.X = 0d;

        await Task.Delay(16);

        TaskbarProfileMainPanel.Opacity = 1d;
        TaskbarProfilePowerPanel.Opacity = 0d;
        if (powerTransform is not null) powerTransform.X = 340d;

        await Task.Delay(280);

        TaskbarProfilePowerPanel.IsVisible = false;
        _isPowerMenuOpen = false;
        _isPowerMenuAnimating = false;
    }

    private async void OnPowerShutdownClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ClosePopupIfOpen();

        if (OperatingSystem.IsWindows())
        {
            _powerService.ShowNativePowerUI(PowerAction.Shutdown);
        }
        else
        {
            await ShowPowerConfirmDialogAsync(L("power.shutdown_confirm_title", "Shutdown"),
                L("power.shutdown_confirm_message", "Are you sure you want to shut down this computer?"),
                () => _powerService.ShutdownAsync());
        }
    }

    private async void OnPowerRestartClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ClosePopupIfOpen();

        await ShowPowerConfirmDialogAsync(L("power.restart_confirm_title", "Restart"),
            L("power.restart_confirm_message", "Are you sure you want to restart this computer?"),
            () => _powerService.RestartAsync());
    }

    private async void OnPowerLogoutClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ClosePopupIfOpen();

        await ShowPowerConfirmDialogAsync(L("power.logout_confirm_title", "Log Out"),
            L("power.logout_confirm_message", "Are you sure you want to log out?"),
            () => _powerService.LogoutAsync());
    }

    private async void OnPowerSleepClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ClosePopupIfOpen();

        await ShowPowerConfirmDialogAsync(L("power.sleep_confirm_title", "Sleep"),
            L("power.sleep_confirm_message", "Are you sure you want to put the computer to sleep?"),
            () => _powerService.SleepAsync());
    }

    private async void OnPowerLockClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ClosePopupIfOpen();
        await _powerService.LockAsync();
    }

    private async Task ShowPowerConfirmDialogAsync(string title, string message, Func<Task> action)
    {
        try
        {
            var dialog = new FAContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = L("power.confirm_yes", "Yes"),
                SecondaryButtonText = L("power.confirm_cancel", "Cancel")
            };

            var result = await dialog.ShowAsync(this);
            if (result == FAContentDialogResult.Primary)
            {
                await action();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("PowerMenu", $"Dialog error: {ex.Message}");
        }
    }

    private void ClosePopupIfOpen()
    {
        if (TaskbarProfilePopup is not null && TaskbarProfilePopup.IsOpen)
        {
            TaskbarProfilePopup.IsOpen = false;
        }
    }

    private void OnCloseComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        _componentLibraryWindowService.Close(this);
    }

    private void OnCloseComponentSettingsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
    }

    private void OnStatusBarClockChecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressStatusBarToggleEvents)
        {
            return;
        }

        _topStatusComponentIds.Add(BuiltInComponentIds.Clock);
        ApplyTopStatusComponentVisibility();
        PersistSettings();
    }

    private void OnStatusBarClockUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressStatusBarToggleEvents)
        {
            return;
        }

        _topStatusComponentIds.Remove(BuiltInComponentIds.Clock);
        ApplyTopStatusComponentVisibility();
        PersistSettings();
    }

    private void ApplyTaskbarSettings(AppSettingsSnapshot snapshot)
    {
        _topStatusComponentIds.Clear();
        if (snapshot.TopStatusComponentIds is not null)
        {
            foreach (var componentId in snapshot.TopStatusComponentIds)
            {
                if (string.IsNullOrWhiteSpace(componentId))
                {
                    continue;
                }

                var normalizedId = componentId.Trim();
                if (_componentRegistry.IsKnownComponent(normalizedId) &&
                    _componentRegistry.AllowsStatusBarPlacement(normalizedId))
                {
                    _topStatusComponentIds.Add(normalizedId);
                }
            }
        }

        _pinnedTaskbarActions.Clear();
        if (snapshot.PinnedTaskbarActions is not null)
        {
            foreach (var actionText in snapshot.PinnedTaskbarActions)
            {
                if (Enum.TryParse<TaskbarActionId>(actionText, ignoreCase: true, out var action))
                {
                    _pinnedTaskbarActions.Add(action);
                }
            }
        }

        if (_pinnedTaskbarActions.Count == 0)
        {
            foreach (var action in DefaultPinnedTaskbarActions)
            {
                _pinnedTaskbarActions.Add(action);
            }
        }

        _enableDynamicTaskbarActions = snapshot.EnableDynamicTaskbarActions;
        _taskbarLayoutMode = string.IsNullOrWhiteSpace(snapshot.TaskbarLayoutMode)
            ? TaskbarLayoutBottomFullRowMacStyle
            : snapshot.TaskbarLayoutMode;

        _clockDisplayFormat = snapshot.ClockDisplayFormat == "HourMinute"
            ? ClockDisplayFormat.HourMinute
            : ClockDisplayFormat.HourMinuteSecond;
        _statusBarClockTransparentBackground = snapshot.StatusBarClockTransparentBackground;
        _clockPosition = NormalizeClockPosition(snapshot.ClockPosition);
        _clockFontSize = NormalizeFontSize(snapshot.ClockFontSize);

        _showTextCapsule = snapshot.ShowTextCapsule;
        _textCapsuleContent = snapshot.TextCapsuleContent ?? "**Hello** World!";
        _textCapsulePosition = NormalizeTextCapsulePosition(snapshot.TextCapsulePosition);
        _textCapsuleTransparentBackground = snapshot.TextCapsuleTransparentBackground;
        _textCapsuleFontSize = NormalizeFontSize(snapshot.TextCapsuleFontSize);

        _showNetworkSpeed = snapshot.ShowNetworkSpeed;
        _networkSpeedPosition = NormalizeNetworkSpeedPosition(snapshot.NetworkSpeedPosition);
        _networkSpeedDisplayMode = NormalizeNetworkSpeedDisplayMode(snapshot.NetworkSpeedDisplayMode);
        _networkSpeedTransparentBackground = snapshot.NetworkSpeedTransparentBackground;
        _showNetworkTypeIcon = snapshot.ShowNetworkTypeIcon;
        _networkSpeedFontSize = NormalizeFontSize(snapshot.NetworkSpeedFontSize);

        _statusBarShadowEnabled = snapshot.StatusBarShadowEnabled;
        _statusBarShadowColor = snapshot.StatusBarShadowColor ?? "#000000";
        _statusBarShadowOpacity = snapshot.StatusBarShadowOpacity;

        ApplyClockSettingsToAllWidgets();
        ApplyTextCapsuleSettingsToAllWidgets();
        ApplyNetworkSpeedSettingsToAllWidgets();
        ApplyStatusBarShadow();
    }

    private void ApplyClockSettingsToAllWidgets()
    {
        if (ClockWidgetLeft is not null)
        {
            ClockWidgetLeft.SetDisplayFormat(_clockDisplayFormat);
            ClockWidgetLeft.SetTransparentBackground(_statusBarClockTransparentBackground);
            ClockWidgetLeft.SetFontSize(_clockFontSize);
        }
        if (ClockWidgetCenter is not null)
        {
            ClockWidgetCenter.SetDisplayFormat(_clockDisplayFormat);
            ClockWidgetCenter.SetTransparentBackground(_statusBarClockTransparentBackground);
            ClockWidgetCenter.SetFontSize(_clockFontSize);
        }
        if (ClockWidgetRight is not null)
        {
            ClockWidgetRight.SetDisplayFormat(_clockDisplayFormat);
            ClockWidgetRight.SetTransparentBackground(_statusBarClockTransparentBackground);
            ClockWidgetRight.SetFontSize(_clockFontSize);
        }
    }

    private static string NormalizeClockPosition(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase) => "Center",
            _ when string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase) => "Right",
            _ => "Left"
        };
    }

    private static string NormalizeFontSize(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Small", StringComparison.OrdinalIgnoreCase) => "Small",
            _ when string.Equals(value, "Large", StringComparison.OrdinalIgnoreCase) => "Large",
            _ => "Medium"
        };
    }

    private void ApplyTextCapsuleSettingsToAllWidgets()
    {
        if (TextCapsuleWidgetLeft is not null)
        {
            TextCapsuleWidgetLeft.SetText(_textCapsuleContent);
            TextCapsuleWidgetLeft.SetTransparentBackground(_textCapsuleTransparentBackground);
        }
        if (TextCapsuleWidgetCenter is not null)
        {
            TextCapsuleWidgetCenter.SetText(_textCapsuleContent);
            TextCapsuleWidgetCenter.SetTransparentBackground(_textCapsuleTransparentBackground);
        }
        if (TextCapsuleWidgetRight is not null)
        {
            TextCapsuleWidgetRight.SetText(_textCapsuleContent);
            TextCapsuleWidgetRight.SetTransparentBackground(_textCapsuleTransparentBackground);
        }
    }

    private static string NormalizeTextCapsulePosition(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase) => "Center",
            _ when string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase) => "Left",
            _ => "Right"
        };
    }

    private void ApplyNetworkSpeedSettingsToAllWidgets()
    {
        if (NetworkSpeedWidgetLeft is not null)
        {
            NetworkSpeedWidgetLeft.SetDisplayMode(_networkSpeedDisplayMode);
            NetworkSpeedWidgetLeft.SetTransparentBackground(_networkSpeedTransparentBackground);
            NetworkSpeedWidgetLeft.SetShowNetworkTypeIcon(_showNetworkTypeIcon);
            NetworkSpeedWidgetLeft.SetFontSize(_networkSpeedFontSize);
        }
        if (NetworkSpeedWidgetCenter is not null)
        {
            NetworkSpeedWidgetCenter.SetDisplayMode(_networkSpeedDisplayMode);
            NetworkSpeedWidgetCenter.SetTransparentBackground(_networkSpeedTransparentBackground);
            NetworkSpeedWidgetCenter.SetShowNetworkTypeIcon(_showNetworkTypeIcon);
            NetworkSpeedWidgetCenter.SetFontSize(_networkSpeedFontSize);
        }
        if (NetworkSpeedWidgetRight is not null)
        {
            NetworkSpeedWidgetRight.SetDisplayMode(_networkSpeedDisplayMode);
            NetworkSpeedWidgetRight.SetTransparentBackground(_networkSpeedTransparentBackground);
            NetworkSpeedWidgetRight.SetShowNetworkTypeIcon(_showNetworkTypeIcon);
            NetworkSpeedWidgetRight.SetFontSize(_networkSpeedFontSize);
        }
    }

    private static string NormalizeNetworkSpeedPosition(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase) => "Left",
            _ when string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase) => "Center",
            _ => "Right"
        };
    }

    private static string NormalizeNetworkSpeedDisplayMode(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Upload", StringComparison.OrdinalIgnoreCase) => "Upload",
            _ when string.Equals(value, "Download", StringComparison.OrdinalIgnoreCase) => "Download",
            _ => "Both"
        };
    }

    private void ApplyStatusBarShadow()
    {
        if (StatusBarOverlay is null)
        {
            return;
        }

        if (_statusBarShadowEnabled)
        {
            if (Color.TryParse(_statusBarShadowColor, out var shadowColor))
            {
                var opacity = Math.Clamp(_statusBarShadowOpacity, 0, 1);

                StatusBarOverlay.IsVisible = true;

                var gradientBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
                };

                var alpha1 = (byte)(shadowColor.A * opacity * 0.8);
                var alpha2 = (byte)(shadowColor.A * opacity * 0.4);
                var color1 = Color.FromArgb(alpha1, shadowColor.R, shadowColor.G, shadowColor.B);
                var color2 = Color.FromArgb(alpha2, shadowColor.R, shadowColor.G, shadowColor.B);

                gradientBrush.GradientStops.Add(new GradientStop(color1, 0.0));
                gradientBrush.GradientStops.Add(new GradientStop(color2, 0.3));
                gradientBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));

                StatusBarOverlay.Background = gradientBrush;
            }
        }
        else
        {
            StatusBarOverlay.IsVisible = false;
        }
    }

    /// <summary>
    /// 濠碘槅鍋€閸嬫挻绻涢弶鎴剳濠殿喗鎮傞獮鈧ù锝呮贡閸╁绱撴担绋款仹婵炲棎鍨藉浼搭敍濮橆厼鍓ㄦ繛鏉戝悑閼规崘銇愰崒鐐村仺闁绘柧璀﹀楣冩煙?    /// </summary>
    private bool WouldComponentsCollide()
    {
        if (TopStatusBarHost is null)
            return false;

        var leftWidth = GetLeftPanelOccupiedWidth();
        var centerWidth = GetCenterPanelOccupiedWidth();
        var rightWidth = GetRightPanelOccupiedWidth();

        var totalWidth = TopStatusBarHost.Bounds.Width;
        if (totalWidth <= 0)
            return false;

        // 闁荤姳绶ょ槐鏇㈡偩缂佹鈻旀い鎾卞灪閿涚喖鏌涢弽褎鎯堥柣鎾寸懇閹啴宕熼銈嗘緰闂傚倸瀚幊宥囩礊閸涱垳纾?        // 閻庡綊娼荤粻鎴﹀垂椤忓牆鍙?*, 婵炴垶鎼╅崢濂稿垂椤忓牆鍙?Auto, 闂佸憡鐟ラ崯鍧楀垂椤忓牆鍙?*
        // 婵炴垶鎼╅崣鍐ㄎ涢崸妤€绀岄柛婵嗗閸樼敻鎮橀悙鍙夊櫢闁煎灚鍨垮浼村礈瑜嬫禒?
        var centerLeft = (totalWidth - centerWidth) / 2;
        var centerRight = centerLeft + centerWidth;

        // 闁诲海鎳撻ˇ顖炲矗韫囨稒鈷掔痪鎯ь儑閻涒晠鏌ㄥ☉妯煎闁稿孩姘ㄥΣ鎰版偑閸涱垳顦?
        const double safetyMargin = 20;

        // 濠碘槅鍋€閸嬫挻绻涢弶鎴剰濞戞柨绻戠粭鐔活槾缂侇喖绉电粋鎺楁嚋閸倣锕傛煕濮樺墽绱扮紒杈╁缁嬪鎯斿┑濠傚箑闂傚倸鍊瑰娆戜焊椤栫偛鏄ラ柣鏂捐濞奸箖鏌?        // 閻庡綊娼荤紓姘跺疾閸撲胶纾奸柛鏇ㄤ簼椤愪粙鏌涘▎蹇曟瀮缂佹梻鍠栭幃?= leftWidth
        // 婵炴垶鎼╅崣鍐ㄎ涢崸妤€绀岄柛婵嗗閸樼數鈧綊娼荤粻鎺旂博閻斿吋鍋?= centerLeft
        if (leftWidth + safetyMargin > centerLeft)
        {
            return true;
        }

        // 濠碘槅鍋€閸嬫挻绻涢弶鎴剰鐟滄澘鎲＄粭鐔活槾缂侇喖绉电粋鎺楁嚋閸倣锕傛煕濮樺墽绱扮紒杈╁缁嬪鎯斿┑濠傚箑闂傚倸鍊瑰娆戜焊椤栫偛鏄ラ柣鏂捐濞奸箖鏌?        // 闂佸憡鐟ラ崢鏍疾閸撲胶纾奸柛鏇ㄤ簼椤愮晫鈧綊娼荤粻鎺旂博閻斿吋鍋?= totalWidth - rightWidth
        // 婵炴垶鎼╅崣鍐ㄎ涢崸妤€绀岄柛婵嗗閸樼敻鏌涘▎蹇曟瀮缂佹梻鍠栭幃?= centerRight
        if (totalWidth - rightWidth - safetyMargin < centerRight)
        {
            return true;
        }

        if (centerLeft < leftWidth + safetyMargin ||
            centerRight > totalWidth - rightWidth - safetyMargin)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 闂佸吋鍎抽崲鑼躲亹閸パ屽晠闁挎梹瀵у▍鐘绘⒒閸稑鐏繝銏★耿瀹曪繝鎮╅崹顐ｆ闂佹眹鍔岀€氼剟顢欓弮鈧幆鏃堟晜閼测晝顦╅梺鍛婄墪閹冲繒鈧凹鍙冨鑽ゅ鐎ｎ剛宕洪梺?    /// </summary>
    private double GetLeftPanelOccupiedWidth()
    {
        if (TopStatusLeftPanel is null)
            return 0;

        var spacing = TopStatusLeftPanel.Spacing;
        var width = 0.0;
        var visibleCount = 0;

        foreach (var child in TopStatusLeftPanel.Children)
        {
            if (child is Control control && control.IsVisible)
            {
                width += control.Bounds.Width;
                visibleCount++;
            }
        }

        // 濠电儑缍€椤曆勬叏閻愮儤鈷掔痪鎯ь儑閻?        if (visibleCount > 1)
        {
            width += spacing * (visibleCount - 1);
        }

        return width;
    }

    /// <summary>
    /// 闂佸吋鍎抽崲鑼躲亹閸ャ劎鈻旀い鎾卞灪閿涚喖姊婚崼娑樼仾婵犮垺锕㈠畷锟犳偐閸偅娈㈤梺姹囧妼鐎氼剟顢欓弮鈧幆鏃堟晜閼测晝顦╅梺鍛婄墪閹冲繒鈧凹鍙冨鑽ゅ鐎ｎ剛宕洪梺?    /// </summary>
    private double GetCenterPanelOccupiedWidth()
    {
        if (TopStatusCenterPanel is null)
            return 0;

        var spacing = TopStatusCenterPanel.Spacing;
        var width = 0.0;
        var visibleCount = 0;

        foreach (var child in TopStatusCenterPanel.Children)
        {
            if (child is Control control && control.IsVisible)
            {
                width += control.Bounds.Width;
                visibleCount++;
            }
        }

        // 濠电儑缍€椤曆勬叏閻愮儤鈷掔痪鎯ь儑閻?        if (visibleCount > 1)
        {
            width += spacing * (visibleCount - 1);
        }

        return width;
    }

    /// <summary>
    /// 闂佸吋鍎抽崲鑼躲亹閸ヮ剙鐭楅柛蹇撴噺濞呯娀姊婚崼娑樼仾婵犮垺锕㈠畷锟犳偐閸偅娈㈤梺姹囧妼鐎氼剟顢欓弮鈧幆鏃堟晜閼测晝顦╅梺鍛婄墪閹冲繒鈧凹鍙冨鑽ゅ鐎ｎ剛宕洪梺?    /// </summary>
    private double GetRightPanelOccupiedWidth()
    {
        if (TopStatusRightPanel is null)
            return 0;

        var spacing = TopStatusRightPanel.Spacing;
        var width = 0.0;
        var visibleCount = 0;

        foreach (var child in TopStatusRightPanel.Children)
        {
            if (child is Control control && control.IsVisible)
            {
                width += control.Bounds.Width;
                visibleCount++;
            }
        }

        // 濠电儑缍€椤曆勬叏閻愮儤鈷掔痪鎯ь儑閻?        if (visibleCount > 1)
        {
            width += spacing * (visibleCount - 1);
        }

        return width;
    }

    /// <summary>
    /// 濠碘槅鍋€閸嬫捇鏌＄仦璇插姕婵″弶鎮傚畷銉╂晝閳ь剝銇愰崣澶岊浄闁靛鍎查煬顒勬煙缁嬫寧鎼愰柣锝囧亾閹峰懎顓奸崶鈺傜€┑鐑囩秬椤曆勬叏閻愮數纾奸柛鏇ㄤ簼椤?    /// </summary>
    private bool CanAddComponentAtPosition(string position)
    {
        var wouldCollide = WouldComponentsCollide();
        if (!wouldCollide)
            return true;

        var leftWidth = GetLeftPanelOccupiedWidth();
        var centerWidth = GetCenterPanelOccupiedWidth();
        var rightWidth = GetRightPanelOccupiedWidth();

        var estimatedNewComponentWidth = _currentDesktopCellSize > 0 ? _currentDesktopCellSize * 2 : 120;

        return position switch
        {
            "Left" => CanAddToLeft(leftWidth, centerWidth, rightWidth, estimatedNewComponentWidth),
            "Center" => CanAddToCenter(leftWidth, centerWidth, rightWidth, estimatedNewComponentWidth),
            "Right" => CanAddToRight(leftWidth, centerWidth, rightWidth, estimatedNewComponentWidth),
            _ => false
        };
    }

    private bool CanAddToLeft(double leftWidth, double centerWidth, double rightWidth, double newWidth)
    {
        if (TopStatusBarHost is null)
            return false;

        var totalWidth = TopStatusBarHost.Bounds.Width;
        if (totalWidth <= 0)
            return true;

        var newLeftWidth = leftWidth + newWidth + (TopStatusLeftPanel?.Spacing ?? 6);
        var centerLeft = (totalWidth - centerWidth) / 2;

        const double safetyMargin = 20;
        return newLeftWidth + safetyMargin <= centerLeft;
    }

    private bool CanAddToCenter(double leftWidth, double centerWidth, double rightWidth, double newWidth)
    {
        if (TopStatusBarHost is null)
            return false;

        var totalWidth = TopStatusBarHost.Bounds.Width;
        if (totalWidth <= 0)
            return true;

        var newCenterWidth = centerWidth + newWidth + (TopStatusCenterPanel?.Spacing ?? 6);
        var centerLeft = (totalWidth - newCenterWidth) / 2;
        var centerRight = centerLeft + newCenterWidth;

        const double safetyMargin = 20;
        return centerLeft >= leftWidth + safetyMargin &&
               centerRight <= totalWidth - rightWidth - safetyMargin;
    }

    private bool CanAddToRight(double leftWidth, double centerWidth, double rightWidth, double newWidth)
    {
        if (TopStatusBarHost is null)
            return false;

        var totalWidth = TopStatusBarHost.Bounds.Width;
        if (totalWidth <= 0)
            return true;

        var newRightWidth = rightWidth + newWidth + (TopStatusRightPanel?.Spacing ?? 6);
        var centerRight = (totalWidth + centerWidth) / 2;

        const double safetyMargin = 20;
        return totalWidth - newRightWidth - safetyMargin >= centerRight;
    }

    private void ApplyTopStatusComponentVisibility()
    {
        var showClock = _topStatusComponentIds.Contains(BuiltInComponentIds.Clock);
        var hasVisibleTopStatusComponent = false;

        if (ClockWidgetLeft is not null)
            ClockWidgetLeft.IsVisible = false;
        if (ClockWidgetCenter is not null)
            ClockWidgetCenter.IsVisible = false;
        if (ClockWidgetRight is not null)
            ClockWidgetRight.IsVisible = false;

        if (TextCapsuleWidgetLeft is not null)
            TextCapsuleWidgetLeft.IsVisible = false;
        if (TextCapsuleWidgetCenter is not null)
            TextCapsuleWidgetCenter.IsVisible = false;
        if (TextCapsuleWidgetRight is not null)
            TextCapsuleWidgetRight.IsVisible = false;

        if (NetworkSpeedWidgetLeft is not null)
            NetworkSpeedWidgetLeft.IsVisible = false;
        if (NetworkSpeedWidgetCenter is not null)
            NetworkSpeedWidgetCenter.IsVisible = false;
        if (NetworkSpeedWidgetRight is not null)
            NetworkSpeedWidgetRight.IsVisible = false;

        if (showClock)
        {
            var targetPosition = _clockPosition;
            var canAdd = CanAddComponentAtPosition(targetPosition);

            if (canAdd)
            {
                var targetClock = targetPosition switch
                {
                    "Center" => ClockWidgetCenter,
                    "Right" => ClockWidgetRight,
                    _ => ClockWidgetLeft
                };

                if (targetClock is not null)
                {
                    targetClock.IsVisible = true;
                    targetClock.SetTransparentBackground(_statusBarClockTransparentBackground);
                    targetClock.SetDisplayFormat(_clockDisplayFormat);
                    hasVisibleTopStatusComponent = true;
                }
            }
            else
            {
                var alternativePosition = FindAlternativePosition(targetPosition);
                if (alternativePosition is not null)
                {
                    var targetClock = alternativePosition switch
                    {
                        "Center" => ClockWidgetCenter,
                        "Right" => ClockWidgetRight,
                        _ => ClockWidgetLeft
                    };

                    if (targetClock is not null)
                    {
                        targetClock.IsVisible = true;
                        targetClock.SetTransparentBackground(_statusBarClockTransparentBackground);
                        targetClock.SetDisplayFormat(_clockDisplayFormat);
                        hasVisibleTopStatusComponent = true;
                    }
                }
            }
        }

        // 闂佸搫绉烽～澶婄暤娴ｈ濯寸€广儱娲ㄩ弸鍌炴偣娴ｇ鈷旈柣銈呮瀵即宕滆娴犳盯鎮楅悽鍨殌缂併劍鐓￠幆鍐礋椤掍胶鈧噣鎮楀☉娆樻畽闁稿繐鐭傚畷鑸电節閸愩劋绮繛瀵稿Ь椤旀劗妲愬▎鎴炴殰闁挎梻铏庡楣冩煙閸撗冧沪妞ゃ儱鎳庨湁閻庯絽澧庣粈?        if (_showTextCapsule)
        {
            var targetPosition = _textCapsulePosition;
            var canAdd = CanAddComponentAtPosition(targetPosition);

            if (canAdd)
            {
                var targetTextCapsule = targetPosition switch
                {
                    "Left" => TextCapsuleWidgetLeft,
                    "Center" => TextCapsuleWidgetCenter,
                    _ => TextCapsuleWidgetRight
                };

                if (targetTextCapsule is not null)
                {
                    targetTextCapsule.IsVisible = true;
                    targetTextCapsule.SetTransparentBackground(_textCapsuleTransparentBackground);
                    targetTextCapsule.SetText(_textCapsuleContent);
                    hasVisibleTopStatusComponent = true;
                }
            }
            else
            {
                var alternativePosition = FindAlternativePosition(targetPosition);
                if (alternativePosition is not null)
                {
                    var targetTextCapsule = alternativePosition switch
                    {
                        "Left" => TextCapsuleWidgetLeft,
                        "Center" => TextCapsuleWidgetCenter,
                        _ => TextCapsuleWidgetRight
                    };

                    if (targetTextCapsule is not null)
                    {
                        targetTextCapsule.IsVisible = true;
                        targetTextCapsule.SetTransparentBackground(_textCapsuleTransparentBackground);
                        targetTextCapsule.SetText(_textCapsuleContent);
                        hasVisibleTopStatusComponent = true;
                    }
                }
            }
        }

        if (_showNetworkSpeed)
        {
            var targetPosition = _networkSpeedPosition;
            var canAdd = CanAddComponentAtPosition(targetPosition);

            if (canAdd)
            {
                var targetNetworkSpeed = targetPosition switch
                {
                    "Left" => NetworkSpeedWidgetLeft,
                    "Center" => NetworkSpeedWidgetCenter,
                    _ => NetworkSpeedWidgetRight
                };

                if (targetNetworkSpeed is not null)
                {
                    targetNetworkSpeed.IsVisible = true;
                    targetNetworkSpeed.SetTransparentBackground(_networkSpeedTransparentBackground);
                    targetNetworkSpeed.SetDisplayMode(_networkSpeedDisplayMode);
                    hasVisibleTopStatusComponent = true;
                }
            }
            else
            {
                var alternativePosition = FindAlternativePosition(targetPosition);
                if (alternativePosition is not null)
                {
                    var targetNetworkSpeed = alternativePosition switch
                    {
                        "Left" => NetworkSpeedWidgetLeft,
                        "Center" => NetworkSpeedWidgetCenter,
                        _ => NetworkSpeedWidgetRight
                    };

                    if (targetNetworkSpeed is not null)
                    {
                        targetNetworkSpeed.IsVisible = true;
                        targetNetworkSpeed.SetTransparentBackground(_networkSpeedTransparentBackground);
                        targetNetworkSpeed.SetDisplayMode(_networkSpeedDisplayMode);
                        hasVisibleTopStatusComponent = true;
                    }
                }
            }
        }

        if (TopStatusBarHost is not null)
        {
            TopStatusBarHost.IsVisible = hasVisibleTopStatusComponent;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await System.Threading.Tasks.Task.Delay(50);
            AdjustComponentsIfColliding();
        });
    }

    /// <summary>
    /// 閻熸粎澧楅幐鍓у垝瀹ュ棛顩烽悹鍝勬惈缁叉椽鏌ｉ姀銏犳灁妞ゎ偒鍋婇獮姗€鎮欑€涙﹩妲梺鎸庣☉閻線宕靛鍫濈闁靛鍔庡▓鍫曟煛娴ｈ櫣绡€缂傚秴鎳愮槐?    /// </summary>
    private void AdjustComponentsIfColliding()
    {
        if (!WouldComponentsCollide())
            return;

        var leftComponents = GetVisibleLeftComponents();
        var centerComponents = GetVisibleCenterComponents();
        var rightComponents = GetVisibleRightComponents();

        if (TextCapsuleWidgetLeft?.IsVisible == true && WouldComponentsCollide())
        {
            if (CanAddComponentAtPosition("Center"))
            {
                TextCapsuleWidgetLeft.IsVisible = false;
                TextCapsuleWidgetCenter!.IsVisible = true;
                TextCapsuleWidgetCenter.SetTransparentBackground(_textCapsuleTransparentBackground);
                TextCapsuleWidgetCenter.SetText(_textCapsuleContent);
            }
            else if (CanAddComponentAtPosition("Right"))
            {
                TextCapsuleWidgetLeft.IsVisible = false;
                TextCapsuleWidgetRight!.IsVisible = true;
                TextCapsuleWidgetRight.SetTransparentBackground(_textCapsuleTransparentBackground);
                TextCapsuleWidgetRight.SetText(_textCapsuleContent);
            }
            else
            {
                TextCapsuleWidgetLeft.IsVisible = false;
            }
        }

        if (TextCapsuleWidgetRight?.IsVisible == true && WouldComponentsCollide())
        {
            if (CanAddComponentAtPosition("Center"))
            {
                TextCapsuleWidgetRight.IsVisible = false;
                TextCapsuleWidgetCenter!.IsVisible = true;
                TextCapsuleWidgetCenter.SetTransparentBackground(_textCapsuleTransparentBackground);
                TextCapsuleWidgetCenter.SetText(_textCapsuleContent);
            }
            else if (CanAddComponentAtPosition("Left"))
            {
                TextCapsuleWidgetRight.IsVisible = false;
                TextCapsuleWidgetLeft!.IsVisible = true;
                TextCapsuleWidgetLeft.SetTransparentBackground(_textCapsuleTransparentBackground);
                TextCapsuleWidgetLeft.SetText(_textCapsuleContent);
            }
            else
            {
                TextCapsuleWidgetRight.IsVisible = false;
            }
        }

        if (TextCapsuleWidgetCenter?.IsVisible == true && WouldComponentsCollide())
        {
            if (CanAddComponentAtPosition("Left"))
            {
                TextCapsuleWidgetCenter.IsVisible = false;
                TextCapsuleWidgetLeft!.IsVisible = true;
                TextCapsuleWidgetLeft.SetTransparentBackground(_textCapsuleTransparentBackground);
                TextCapsuleWidgetLeft.SetText(_textCapsuleContent);
            }
            else if (CanAddComponentAtPosition("Right"))
            {
                TextCapsuleWidgetCenter.IsVisible = false;
                TextCapsuleWidgetRight!.IsVisible = true;
                TextCapsuleWidgetRight.SetTransparentBackground(_textCapsuleTransparentBackground);
                TextCapsuleWidgetRight.SetText(_textCapsuleContent);
            }
            else
            {
                TextCapsuleWidgetCenter.IsVisible = false;
            }
        }

        if (NetworkSpeedWidgetLeft?.IsVisible == true && WouldComponentsCollide())
        {
            if (CanAddComponentAtPosition("Center"))
            {
                NetworkSpeedWidgetLeft.IsVisible = false;
                NetworkSpeedWidgetCenter!.IsVisible = true;
                NetworkSpeedWidgetCenter.SetTransparentBackground(_networkSpeedTransparentBackground);
                NetworkSpeedWidgetCenter.SetDisplayMode(_networkSpeedDisplayMode);
            }
            else if (CanAddComponentAtPosition("Right"))
            {
                NetworkSpeedWidgetLeft.IsVisible = false;
                NetworkSpeedWidgetRight!.IsVisible = true;
                NetworkSpeedWidgetRight.SetTransparentBackground(_networkSpeedTransparentBackground);
                NetworkSpeedWidgetRight.SetDisplayMode(_networkSpeedDisplayMode);
            }
            else
            {
                NetworkSpeedWidgetLeft.IsVisible = false;
            }
        }

        if (NetworkSpeedWidgetRight?.IsVisible == true && WouldComponentsCollide())
        {
            if (CanAddComponentAtPosition("Center"))
            {
                NetworkSpeedWidgetRight.IsVisible = false;
                NetworkSpeedWidgetCenter!.IsVisible = true;
                NetworkSpeedWidgetCenter.SetTransparentBackground(_networkSpeedTransparentBackground);
                NetworkSpeedWidgetCenter.SetDisplayMode(_networkSpeedDisplayMode);
            }
            else if (CanAddComponentAtPosition("Left"))
            {
                NetworkSpeedWidgetRight.IsVisible = false;
                NetworkSpeedWidgetLeft!.IsVisible = true;
                NetworkSpeedWidgetLeft.SetTransparentBackground(_networkSpeedTransparentBackground);
                NetworkSpeedWidgetLeft.SetDisplayMode(_networkSpeedDisplayMode);
            }
            else
            {
                NetworkSpeedWidgetRight.IsVisible = false;
            }
        }

        if (NetworkSpeedWidgetCenter?.IsVisible == true && WouldComponentsCollide())
        {
            if (CanAddComponentAtPosition("Left"))
            {
                NetworkSpeedWidgetCenter.IsVisible = false;
                NetworkSpeedWidgetLeft!.IsVisible = true;
                NetworkSpeedWidgetLeft.SetTransparentBackground(_networkSpeedTransparentBackground);
                NetworkSpeedWidgetLeft.SetDisplayMode(_networkSpeedDisplayMode);
            }
            else if (CanAddComponentAtPosition("Right"))
            {
                NetworkSpeedWidgetCenter.IsVisible = false;
                NetworkSpeedWidgetRight!.IsVisible = true;
                NetworkSpeedWidgetRight.SetTransparentBackground(_networkSpeedTransparentBackground);
                NetworkSpeedWidgetRight.SetDisplayMode(_networkSpeedDisplayMode);
            }
            else
            {
                NetworkSpeedWidgetCenter.IsVisible = false;
            }
        }
    }

    /// <summary>
    /// 闂佸搫琚崕鍙夌珶濮椻偓瀹曪綁顢涘鍕闂佹眹鍔岀€氼厼霉濞戞瑧顩烽柨婵嗗缁夊绱?    /// </summary>
    private string? FindAlternativePosition(string originalPosition)
    {
        // 闁诲繐绻戠换鍡涙儊椤栫偛绠ラ柍褜鍓熷鍨緞婵犲倽顔夐梺鐓庣－閺咁偄鈻撻幋鐐村鐎广儱娲ㄩ弸?
        var positions = new[] { "Left", "Center", "Right" };
        foreach (var position in positions)
        {
            if (position != originalPosition && CanAddComponentAtPosition(position))
            {
                return position;
            }
        }
        return null;
    }

    /// <summary>
    /// 闂佸吋鍎抽崲鑼躲亹閸パ屽晠闁挎梹瀵у▍鐘绘煕濞嗘ê鐏ユい顐㈡缁辨帡宕熼鍜佸仺闂佸憡甯楅〃澶愬Υ?    /// </summary>
    private List<Control> GetVisibleLeftComponents()
    {
        var result = new List<Control>();
        if (TopStatusLeftPanel is null) return result;

        foreach (var child in TopStatusLeftPanel.Children)
        {
            if (child is Control control && control.IsVisible)
                result.Add(control);
        }
        return result;
    }

    /// <summary>
    /// 闂佸吋鍎抽崲鑼躲亹閸ャ劎鈻旀い鎾卞灪閿涚喖鏌涘▎妯虹仴妞ゎ偄妫涚槐鎺楀礋椤忓拋鍋ㄩ梺鍛婂笚椤ㄥ濡?    /// </summary>
    private List<Control> GetVisibleCenterComponents()
    {
        var result = new List<Control>();
        if (TopStatusCenterPanel is null) return result;

        foreach (var child in TopStatusCenterPanel.Children)
        {
            if (child is Control control && control.IsVisible)
                result.Add(control);
        }
        return result;
    }

    /// <summary>
    /// 闂佸吋鍎抽崲鑼躲亹閸ヮ剙鐭楅柛蹇撴噺濞呯娀鏌涘▎妯虹仴妞ゎ偄妫涚槐鎺楀礋椤忓拋鍋ㄩ梺鍛婂笚椤ㄥ濡?    /// </summary>
    private List<Control> GetVisibleRightComponents()
    {
        var result = new List<Control>();
        if (TopStatusRightPanel is null) return result;

        foreach (var child in TopStatusRightPanel.Children)
        {
            if (child is Control control && control.IsVisible)
                result.Add(control);
        }
        return result;
    }

    private TaskbarContext GetCurrentTaskbarContext()
    {
        return TaskbarContext.Desktop;
    }

    private void ApplyTaskbarActionVisibility(TaskbarContext context)
    {
        if (BackToWindowsButton is null ||
            TaskbarProfileButton is null)
        {
            return;
        }

        var showMinimize = _pinnedTaskbarActions.Contains(TaskbarActionId.MinimizeToWindows);

        BackToWindowsButton.IsVisible = showMinimize;
        TaskbarProfileButton.IsVisible = true;

        if (TaskbarFixedActionsHost is not null)
        {
            TaskbarFixedActionsHost.IsVisible = showMinimize;
        }

        if (TaskbarSettingsActionHost is not null)
        {
            TaskbarSettingsActionHost.IsVisible = true;
        }

        UpdateOpenSettingsActionVisualState();

        var dynamicActions = ResolveDynamicTaskbarActions(context)
            .Where(action => action.IsVisible)
            .ToList();
        var hasDynamicActions = dynamicActions.Count > 0;
        BuildDynamicTaskbarVisuals(dynamicActions, _currentDesktopCellSize);

        if (TaskbarDynamicActionsHost is not null)
        {
            TaskbarDynamicActionsHost.IsVisible = hasDynamicActions;
        }
    }

    private void UpdateOpenSettingsActionVisualState()
    {
        var effectiveCellSize = _currentDesktopCellSize > 0
            ? _currentDesktopCellSize
            : Math.Max(32, Math.Min(Bounds.Width, Bounds.Height) / Math.Max(1, _targetShortSideCells));
        RefreshTaskbarProfilePresentation();
        ApplyWidgetSizing(effectiveCellSize);
    }

    private void OpenComponentLibraryWindow()
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        _isComponentLibraryOpen = true;
        UpdateDesktopComponentHostEditState();
        ShowComponentLibraryCategoryView();
        RestoreComponentLibraryAfterDesktopEdit();
        ComponentLibraryWindow.IsVisible = true;
        ComponentLibraryWindow.Opacity = 0;
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
        RestoreComponentLibraryWindowPosition();

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isComponentLibraryOpen || ComponentLibraryWindow is null)
            {
                return;
            }

            BuildComponentLibraryCategoryPages();
            ComponentLibraryWindow.Opacity = 1;
            SyncComponentLibraryCollapseExpandedState();
        }, DispatcherPriority.Background);
    }

    private void CloseComponentLibraryWindow(bool reopenSettings)
    {
        if (!_isComponentLibraryOpen || ComponentLibraryWindow is null)
        {
            return;
        }

        RestoreComponentLibraryAfterDesktopEdit();
        _isComponentLibraryOpen = false;
        CancelDesktopComponentDrag();
        CancelDesktopComponentResize(restoreOriginalSpan: true);
        ClearDesktopComponentSelection();
        ClearSelectedLauncherTile(refreshTaskbar: false);
        UpdateDesktopComponentHostEditState();
        ComponentLibraryWindow.Opacity = 0;
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        DispatcherTimer.RunOnce(() =>
        {
            if (_isComponentLibraryOpen || ComponentLibraryWindow is null)
            {
                return;
            }

            ComponentLibraryWindow.IsVisible = false;
            ClearComponentLibraryPreviewControls();

            var shouldReopenSettings = reopenSettings && _reopenSettingsAfterComponentLibraryClose;
            _reopenSettingsAfterComponentLibraryClose = false;
            if (shouldReopenSettings)
            {
                (Application.Current as App)?.OpenIndependentSettingsModule("ComponentLibraryClose");
            }
        }, FluttermotionToken.Slow);
    }

    private void OpenDetachedComponentLibraryWindow()
    {
        _detachedComponentLibraryWindow ??= CreateDetachedComponentLibraryWindow();
        _detachedComponentLibraryWindow.Reload();
        if (!_detachedComponentLibraryWindow.IsVisible)
        {
            if (IsVisible)
            {
                _detachedComponentLibraryWindow.Show(this);
            }
            else
            {
                _detachedComponentLibraryWindow.Show();
            }

            return;
        }

        _detachedComponentLibraryWindow.Activate();
    }

    private void CloseDetachedComponentLibraryWindow()
    {
        if (_detachedComponentLibraryWindow is null)
        {
            return;
        }

        _detachedComponentLibraryWindow.Hide();
    }

    private ComponentLibraryWindow CreateDetachedComponentLibraryWindow()
    {
        var window = new ComponentLibraryWindow(
            _componentLibraryService,
            cellSize =>
            {
                var appearanceSnapshot = HostAppearanceThemeProvider.GetOrCreate().GetCurrent();
                return new ComponentLibraryCreateContext(
                    cellSize,
                    _timeZoneService,
                    _weatherDataService,
                    _recommendationInfoService,
                    _calculatorDataService,
                    _settingsFacade);
            },
            L,
            previewKeyResolver: ResolveDetachedLibraryPreviewKey,
            previewEntryResolver: ResolveDetachedLibraryPreviewEntry,
            warmPreviewRequested: RequestDetachedLibraryPreviewWarm,
            renderPreviewRequested: RequestDetachedLibraryPreviewRender);
        window.AddComponentRequested += OnDetachedComponentLibraryAddComponentRequested;
        window.Closed += OnDetachedComponentLibraryClosed;
        return window;
    }

    private void OnDetachedComponentLibraryAddComponentRequested(object? sender, string componentId)
    {
        _ = sender;
        if (string.IsNullOrWhiteSpace(componentId) ||
            _currentDesktopSurfaceIndex < 0 ||
            _currentDesktopSurfaceIndex >= _desktopPageCount ||
            !_desktopPageComponentGrids.TryGetValue(_currentDesktopSurfaceIndex, out var pageGrid) ||
            !_componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor))
        {
            return;
        }

        var span = NormalizeComponentCellSpan(
            componentId,
            ComponentPlacementRules.EnsureMinimumSize(
                descriptor.Definition,
                descriptor.Definition.MinWidthCells,
                descriptor.Definition.MinHeightCells));
        var row = Math.Max(0, (pageGrid.RowDefinitions.Count - span.HeightCells) / 2);
        var column = Math.Max(0, (pageGrid.ColumnDefinitions.Count - span.WidthCells) / 2);
        PlaceDesktopComponentOnPage(componentId, _currentDesktopSurfaceIndex, row, column);
    }

    private void OnDetachedComponentLibraryClosed(object? sender, EventArgs e)
    {
        _ = e;
        if (ReferenceEquals(sender, _detachedComponentLibraryWindow))
        {
            _detachedComponentLibraryWindow.AddComponentRequested -= OnDetachedComponentLibraryAddComponentRequested;
            _detachedComponentLibraryWindow.Closed -= OnDetachedComponentLibraryClosed;
            _detachedComponentLibraryWindow = null;
        }
    }

    private static DesktopComponentPlacementSnapshot ClonePlacementSnapshot(DesktopComponentPlacementSnapshot placement)
    {
        return new DesktopComponentPlacementSnapshot
        {
            PlacementId = placement.PlacementId,
            PageIndex = placement.PageIndex,
            ComponentId = placement.ComponentId,
            Row = placement.Row,
            Column = placement.Column,
            WidthCells = placement.WidthCells,
            HeightCells = placement.HeightCells
        };
    }

    private void OnSettingsWindowStateChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        SyncSettingsWindowState();
    }

    private void SyncSettingsWindowState()
    {
        var isOpen = (Application.Current as App)?.SettingsWindowService?.IsOpen == true;
        _isSettingsOpen = isOpen;
        UpdateDesktopPageAwareComponentContext();
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
        UpdateOpenSettingsActionVisualState();
    }

    private void InitializeDesktopComponentDragHandlers()
    {
        // Global handlers: we capture the pointer during drag, then track move/release anywhere.
        AddHandler(PointerMovedEvent, OnDesktopComponentDragPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnDesktopComponentDragPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnDesktopComponentDragPointerCaptureLost, RoutingStrategies.Tunnel);
    }

    private IReadOnlyList<TaskbarActionItem> ResolveDynamicTaskbarActions(TaskbarContext context)
    {
        if (context == TaskbarContext.Desktop && _isComponentLibraryOpen)
        {
            var actions = new List<TaskbarActionItem>();
            var isLauncherSurface = _currentDesktopSurfaceIndex == LauncherSurfaceIndex;
            if (isLauncherSurface && IsLauncherTileSelected())
            {
                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.HideLauncherEntry,
                    L("launcher.action.hide", "Hide"),
                    "Hide",
                    IsVisible: true,
                    CommandKey: "launcher.hide"));
                return actions;
            }

            if (_selectedDesktopComponentHost is not null)
            {
                if (TryGetSelectedDesktopPlacement(out var selectedPlacement) &&
                    _componentEditorRegistry.TryGetDescriptor(selectedPlacement.ComponentId, out _))
                {
                    actions.Add(new TaskbarActionItem(
                        TaskbarActionId.EditComponent,
                        L("component.edit", "Edit"),
                        "Edit",
                        IsVisible: true,
                        CommandKey: "component.edit"));
                }

                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.DeleteComponent,
                    L("component.delete", "Delete"),
                    "Delete",
                    IsVisible: true,
                    CommandKey: "component.delete"));

                return actions;
            }

            var canAddPage = _desktopPageCount < MaxDesktopPageCount;
            var canDeletePage = _desktopPageCount > MinDesktopPageCount;

            if (canAddPage)
            {
                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.AddDesktopPage,
                    L("desktop.add_page", "Add page"),
                    "Add",
                    IsVisible: true,
                    CommandKey: "desktop.add_page"));
            }

            if (canDeletePage)
            {
                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.DeleteDesktopPage,
                    L("desktop.delete_page", "Delete page"),
                    "Delete",
                    IsVisible: true,
                    CommandKey: "desktop.delete_page"));
            }

            return actions;
        }

        if (!_enableDynamicTaskbarActions)
        {
            return Array.Empty<TaskbarActionItem>();
        }

        _ = context;
        return Array.Empty<TaskbarActionItem>();
    }

    private void BuildDynamicTaskbarVisuals(IReadOnlyList<TaskbarActionItem> actions, double cellSize)
    {
        if (TaskbarDynamicActionsPanel is not null)
        {
            TaskbarDynamicActionsPanel.Children.Clear();
        }

        if (actions.Count == 0 || TaskbarDynamicActionsPanel is null)
        {
            return;
        }

        // Match taskbar typographic scale to the current grid cell size.
        var taskbarCellHeight = Math.Clamp(cellSize * 0.76, 36, 76);
        var fontSize = Math.Clamp(taskbarCellHeight * 0.36, 11, 22);
        var iconSize = Math.Clamp(taskbarCellHeight * 0.44, 12, 26);
        var padding = Math.Clamp(taskbarCellHeight * 0.20, 6, 14);
        var cornerRadius = Math.Clamp(taskbarCellHeight * 0.32, 8, 16);
        var spacing = Math.Clamp(taskbarCellHeight * 0.18, 4, 10);

        var pageCountText = $"{_currentDesktopSurfaceIndex + 1}/{_desktopPageCount}";
        var pageCountBlock = new TextBlock
        {
            Text = pageCountText,
            Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush"),
            FontSize = fontSize,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, spacing, 0)
        };

        var pageCountContainer = new Border
        {
            Background = GetThemeBrush("AdaptiveButtonBackgroundBrush"),
            CornerRadius = new CornerRadius(cornerRadius),
            Padding = new Thickness(padding),
            Child = pageCountBlock,
            Margin = new Thickness(0, 0, spacing, 0)
        };

        TaskbarDynamicActionsPanel.Children.Add(pageCountContainer);

        foreach (var action in actions)
        {
            if (!action.IsVisible)
            {
                continue;
            }

            var isDeleteAction = action.Id == TaskbarActionId.DeleteDesktopPage ||
                                 action.Id == TaskbarActionId.DeleteComponent;
            var isHideAction = action.Id == TaskbarActionId.HideLauncherEntry;

            var iconSymbol = action.Id switch
            {
                TaskbarActionId.EditComponent => Symbol.Edit,
                _ when isDeleteAction || isHideAction => Symbol.Delete,
                _ => Symbol.Add
            };

            Control icon = new SymbolIcon
            {
                Symbol = iconSymbol,
                IconVariant = IconVariant.Regular,
                FontSize = iconSize
            };

            var buttonContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = spacing * 0.6,
                Children =
                {
                    icon,
                    new TextBlock
                    {
                        Text = action.Title,
                        FontSize = fontSize,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                }
            };

            var button = new Button
            {
                Content = buttonContent,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(padding),
                Foreground = (isDeleteAction || isHideAction)
                    ? new SolidColorBrush(Color.Parse("#FFFF6B6B"))
                    : Foreground,
                Tag = action.CommandKey
            };
            button.Click += OnDynamicTaskbarActionClick;

            TaskbarDynamicActionsPanel.Children.Add(button);

        }
    }

    private void OnDynamicTaskbarActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string commandKey)
        {
            return;
        }

        switch (commandKey)
        {
            case "desktop.add_page":
                AddDesktopPage();
                break;
            case "desktop.delete_page":
                ConfirmAndDeleteCurrentDesktopPage();
                break;
            case "component.delete":
                DeleteSelectedComponent();
                break;
            case "component.edit":
                OpenSelectedComponentEditor();
                break;
            case "launcher.hide":
                HideSelectedLauncherEntry();
                break;
        }
    }

    private async void ConfirmAndDeleteCurrentDesktopPage()
    {
        if (_desktopPageCount <= MinDesktopPageCount)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = L("desktop.delete_page_confirm.title", "Delete desktop page"),
            Content = L("desktop.delete_page_confirm.message", "This will permanently remove the current desktop page and all widgets placed on it.\n\nThis action cannot be undone."),
            PrimaryButtonText = L("desktop.delete_page_confirm.close", "Cancel"),
            SecondaryButtonText = L("desktop.delete_page_confirm.primary", "Delete"),
            DefaultButton = FAContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync(this);
        if (result == FAContentDialogResult.Secondary)
        {
            DeleteCurrentDesktopPage();
        }
    }

    private void DeleteSelectedComponent()
    {
        if (_selectedDesktopComponentHost is null || _selectedDesktopComponentHost.Tag is not string placementId)
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return;
        }

        var before = ClonePlacementSnapshot(placement);

        if (string.Equals(_componentEditorWindowService.CurrentPlacementId, placement.PlacementId, StringComparison.OrdinalIgnoreCase))
        {
            _componentEditorWindowService.Close();
        }

        ClearTimeZoneServiceBindings(_selectedDesktopComponentHost);
        DisposeComponentIfNeeded(_selectedDesktopComponentHost);

        if (_desktopPageComponentGrids.TryGetValue(placement.PageIndex, out var pageGrid))
        {
            pageGrid.Children.Remove(_selectedDesktopComponentHost);
        }

        _desktopComponentPlacements.Remove(placement);
        _componentSettingsStore.DeleteForComponent(placement.ComponentId, placement.PlacementId);
        RemovePlacementPreviewImage(placement.PlacementId);

        ClearDesktopComponentSelection();

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        PersistSettings();
        TelemetryServices.Usage?.TrackDesktopComponentDeleted(before, "component.delete");
    }

    private void OpenSelectedComponentEditor()
    {
        if (!TryGetSelectedDesktopPlacement(out var placement) ||
            !_componentEditorRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            return;
        }

        _componentEditorWindowService.Open(new ComponentEditorOpenRequest(
            Owner: this,
            Descriptor: descriptor,
            ComponentId: placement.ComponentId,
            PlacementId: placement.PlacementId,
            RefreshAction: () => RefreshDesktopComponentPlacement(placement.PlacementId)));

        TelemetryServices.Usage?.TrackDesktopComponentEditorOpened(ClonePlacementSnapshot(placement), "component.edit");
    }

    private bool TryGetSelectedDesktopPlacement(out DesktopComponentPlacementSnapshot placement)
    {
        placement = null!;
        if (_selectedDesktopComponentHost?.Tag is not string placementId)
        {
            return false;
        }

        var matchedPlacement = _desktopComponentPlacements.FirstOrDefault(candidate =>
            string.Equals(candidate.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (matchedPlacement is null)
        {
            return false;
        }

        placement = matchedPlacement;
        return true;
    }

    private void RefreshDesktopComponentPlacement(string placementId)
    {
        if (string.IsNullOrWhiteSpace(placementId))
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(candidate =>
            string.Equals(candidate.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null ||
            !_desktopPageComponentGrids.TryGetValue(placement.PageIndex, out var pageGrid))
        {
            return;
        }

        var host = pageGrid.Children
            .OfType<Border>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, placementId, StringComparison.OrdinalIgnoreCase));
        if (host is null)
        {
            RestoreDesktopPageComponents(placement.PageIndex);
            ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
            QueuePlacementPreviewRefresh(placement);
            return;
        }

        var component = CreateDesktopComponentControl(placement.ComponentId, placement.PlacementId, placement.PageIndex);
        if (component is null)
        {
            return;
        }

        if (TryGetContentHost(host) is not Border contentHost)
        {
            RestoreDesktopPageComponents(placement.PageIndex);
            ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
            return;
        }

        ClearTimeZoneServiceBindings(host);
        DisposeComponentIfNeeded(host);
        contentHost.Child = component;
        ApplyDesktopEditStateToHost(host, _isComponentLibraryOpen);
        InvalidateDesktopPageAwareComponentContextCache();
        UpdateDesktopPageAwareComponentContext();
        if (_selectedDesktopComponentHost == host)
        {
            ApplySelectionStateToHost(host, true);
        }

        QueuePlacementPreviewRefresh(placement);
    }

    private static void DisposeComponentIfNeeded(Border host)
    {
        if (TryGetContentHost(host) is Border contentHost && contentHost.Child is Control componentControl)
        {
            if (componentControl is IDisposable disposableComponent)
            {
                disposableComponent.Dispose();
            }
        }
    }

    // Legacy in-window popup editor is removed; component editing now routes through the Material editor window service.

    private void AddDesktopPage()
    {
        if (_desktopPageCount >= MaxDesktopPageCount)
        {
            return;
        }

        _desktopPageCount = Math.Clamp(_desktopPageCount + 1, MinDesktopPageCount, MaxDesktopPageCount);
        _currentDesktopSurfaceIndex = Math.Clamp(_desktopPageCount - 1, 0, LauncherSurfaceIndex);
        RebuildDesktopGrid();
        PersistSettings();
        
        // Refresh taskbar actions after page count changes.
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void DeleteCurrentDesktopPage()
    {
        if (_desktopPageCount <= MinDesktopPageCount)
        {
            return;
        }

        var placementsToRemove = _desktopComponentPlacements
            .Where(p => p.PageIndex == _currentDesktopSurfaceIndex)
            .ToList();

        if (_desktopPageComponentGrids.TryGetValue(_currentDesktopSurfaceIndex, out var pageGrid))
        {
            ClearTimeZoneServiceBindings(pageGrid.Children.OfType<Control>().ToList());
            foreach (var child in pageGrid.Children.OfType<Border>())
            {
                DisposeComponentIfNeeded(child);
            }
        }
        
        foreach (var placement in placementsToRemove)
        {
            _desktopComponentPlacements.Remove(placement);
            _componentSettingsStore.DeleteForComponent(placement.ComponentId, placement.PlacementId);
        }
        RemovePlacementPreviewImages(placementsToRemove);

        _desktopPageCount = Math.Clamp(_desktopPageCount - 1, MinDesktopPageCount, MaxDesktopPageCount);
        
        // Clamp current page index to valid range after deletion.
        _currentDesktopSurfaceIndex = Math.Clamp(_currentDesktopSurfaceIndex, 0, _desktopPageCount - 1);
        
        // Update remaining page indices after deletion.
        foreach (var placement in _desktopComponentPlacements)
        {
            if (placement.PageIndex > _currentDesktopSurfaceIndex)
            {
                placement.PageIndex--;
            }
        }

        RebuildDesktopGrid();
        PersistSettings();
        
        // Refresh taskbar actions after page count changes.
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void InitializeDesktopComponentPlacements(DesktopLayoutSettingsSnapshot snapshot)
    {
        _desktopComponentPlacements.Clear();

        if (snapshot.DesktopComponentPlacements is null)
        {
            return;
        }

        foreach (var placement in snapshot.DesktopComponentPlacements)
        {
            if (placement is null || string.IsNullOrWhiteSpace(placement.ComponentId))
            {
                continue;
            }

            var placementId = string.IsNullOrWhiteSpace(placement.PlacementId)
                ? Guid.NewGuid().ToString("N")
                : placement.PlacementId.Trim();
            var componentId = placement.ComponentId.Trim();
            if (!_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor) ||
                !runtimeDescriptor.Definition.AllowDesktopPlacement)
            {
                continue;
            }

            var (widthCells, heightCells) = NormalizeComponentCellSpan(
                componentId,
                ComponentPlacementRules.EnsureMinimumSize(
                    runtimeDescriptor.Definition,
                    placement.WidthCells,
                    placement.HeightCells));

            _desktopComponentPlacements.Add(new DesktopComponentPlacementSnapshot
            {
                PlacementId = placementId,
                PageIndex = Math.Max(0, placement.PageIndex),
                ComponentId = componentId,
                Row = Math.Max(0, placement.Row),
                Column = Math.Max(0, placement.Column),
                WidthCells = widthCells,
                HeightCells = heightCells
            });
        }
    }

    private void RestoreDesktopPageComponents(int pageIndex)
    {
        if (!_desktopPageComponentGrids.TryGetValue(pageIndex, out var pageGrid))
        {
            return;
        }

        ClearTimeZoneServiceBindings(pageGrid.Children.OfType<Control>().ToList());
        pageGrid.Children.Clear();
        InvalidateDesktopPageAwareComponentContextCache();

        var maxColumns = pageGrid.ColumnDefinitions.Count;
        var maxRows = pageGrid.RowDefinitions.Count;
        if (maxColumns <= 0 || maxRows <= 0)
        {
            return;
        }

        foreach (var placement in _desktopComponentPlacements.Where(p => p.PageIndex == pageIndex))
        {
            if (!_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var runtimeDescriptor) ||
                !runtimeDescriptor.Definition.AllowDesktopPlacement)
            {
                continue;
            }

            var (widthCells, heightCells) = NormalizeComponentCellSpan(
                placement.ComponentId,
                ComponentPlacementRules.EnsureMinimumSize(
                    runtimeDescriptor.Definition,
                    placement.WidthCells,
                    placement.HeightCells));

            var clampedColumn = Math.Clamp(placement.Column, 0, Math.Max(0, maxColumns - widthCells));
            var clampedRow = Math.Clamp(placement.Row, 0, Math.Max(0, maxRows - heightCells));

            var host = CreateDesktopComponentHost(placement);
            if (host is null)
            {
                continue;
            }

            placement.Column = clampedColumn;
            placement.Row = clampedRow;
            placement.WidthCells = widthCells;
            placement.HeightCells = heightCells;

            Grid.SetColumn(host, clampedColumn);
            Grid.SetRow(host, clampedRow);
            Grid.SetColumnSpan(host, widthCells);
            Grid.SetRowSpan(host, heightCells);
            pageGrid.Children.Add(host);
        }

        UpdateDesktopPageAwareComponentContext();
    }

    private void PlaceDesktopComponentOnPage(string componentId, int pageIndex, int row, int column)
    {
        if (!_desktopPageComponentGrids.TryGetValue(pageIndex, out var pageGrid))
        {
            return;
        }

        if (!_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor) ||
            !runtimeDescriptor.Definition.AllowDesktopPlacement)
        {
            return;
        }

        var (widthCells, heightCells) = NormalizeComponentCellSpan(
            componentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                runtimeDescriptor.Definition.MinWidthCells,
                runtimeDescriptor.Definition.MinHeightCells));

        var maxColumns = pageGrid.ColumnDefinitions.Count;
        var maxRows = pageGrid.RowDefinitions.Count;
        if (maxColumns <= 0 || maxRows <= 0)
        {
            return;
        }

        column = Math.Clamp(column, 0, Math.Max(0, maxColumns - widthCells));
        row = Math.Clamp(row, 0, Math.Max(0, maxRows - heightCells));

        var placementId = Guid.NewGuid().ToString("N");
        var placement = new DesktopComponentPlacementSnapshot
        {
            PlacementId = placementId,
            PageIndex = pageIndex,
            ComponentId = componentId,
            Row = row,
            Column = column,
            WidthCells = widthCells,
            HeightCells = heightCells
        };

        var host = CreateDesktopComponentHost(placement);
        if (host is null)
        {
            return;
        }

        Grid.SetColumn(host, column);
        Grid.SetRow(host, row);
        Grid.SetColumnSpan(host, widthCells);
        Grid.SetRowSpan(host, heightCells);
        pageGrid.Children.Add(host);

        _desktopComponentPlacements.Add(placement);
        QueuePlacementPreviewRefresh(placement);
        InvalidateDesktopPageAwareComponentContextCache();
        UpdateDesktopPageAwareComponentContext();
        PersistSettings();
        TelemetryServices.Usage?.TrackDesktopComponentPlaced(ClonePlacementSnapshot(placement), "component.create");

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private Border? CreateDesktopComponentHost(DesktopComponentPlacementSnapshot placement)
    {
        if (string.IsNullOrWhiteSpace(placement.PlacementId))
        {
            placement.PlacementId = Guid.NewGuid().ToString("N");
        }

        var component = CreateDesktopComponentControl(placement.ComponentId, placement.PlacementId, placement.PageIndex);
        if (component is null)
        {
            return null;
        }

        var componentCornerRadius = GetComponentCornerRadius(placement.ComponentId);

        var visualInset = GetDesktopComponentVisualInset(
            Math.Max(1, placement.WidthCells),
            Math.Max(1, placement.HeightCells));

        var contentHost = new Border
        {
            Tag = DesktopComponentContentHostTag,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(componentCornerRadius),
            ClipToBounds = true,
            Padding = visualInset,
            Child = component
        };

        // Separate visual arc size from hit target size for better touch usability.
        var handleTouchSize = Math.Clamp(_currentDesktopCellSize * 0.72, 30, 54);
        var handleVisualSize = Math.Clamp(_currentDesktopCellSize * 0.56, 20, 40);
        var handlePadding = Math.Max(2, (handleTouchSize - handleVisualSize) / 2);
        var arcThickness = Math.Clamp(_currentDesktopCellSize * 0.17, 7, 14);
        var arcData = Geometry.Parse("M 24,6 A 18,18 0 0 1 6,24");

        var resizeHandleVisual = new Grid
        {
            Width = handleVisualSize,
            Height = handleVisualSize,
            IsHitTestVisible = false
        };
        resizeHandleVisual.Children.Add(new PathShape
        {
            Data = arcData,
            Stretch = Stretch.Fill,
            Stroke = GetThemeBrush("AdaptiveTextAccentBrush"),
            StrokeThickness = arcThickness + 3,
            StrokeLineCap = PenLineCap.Round
        });
        resizeHandleVisual.Children.Add(new PathShape
        {
            Data = arcData,
            Stretch = Stretch.Fill,
            Stroke = GetThemeBrush("AdaptiveAccentBrush"),
            StrokeThickness = arcThickness,
            StrokeLineCap = PenLineCap.Round
        });

        var resizeHandle = new Border
        {
            Tag = DesktopComponentResizeHandleTag,
            Width = handleTouchSize,
            Height = handleTouchSize,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(handleTouchSize * 0.5),
            Padding = new Thickness(handlePadding),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(
                0,
                0,
                -Math.Clamp(handleTouchSize * 0.42, 10, 24),
                -Math.Clamp(handleTouchSize * 0.42, 10, 24)),
            Child = resizeHandleVisual,
            Opacity = 1,
            IsVisible = false,
            IsHitTestVisible = false
        };
        resizeHandle.PointerPressed += OnDesktopComponentResizeHandlePointerPressed;

        var hostChrome = new Grid
        {
            ClipToBounds = false
        };
        hostChrome.Children.Add(contentHost);
        hostChrome.Children.Add(resizeHandle);

        var host = new Border
        {
            Tag = placement.PlacementId,
            Background = Brushes.Transparent,
            ClipToBounds = false,
            CornerRadius = new CornerRadius(componentCornerRadius),
            Child = hostChrome
        };
        host.Classes.Add(DesktopComponentHostClass);
        ApplyDesktopEditStateToHost(host, _isComponentLibraryOpen);
        host.PointerPressed += OnDesktopComponentHostPointerPressed;
        return host;
    }

    private (int WidthCells, int HeightCells) NormalizeComponentCellSpan(
        string componentId,
        (int WidthCells, int HeightCells) span)
    {
        if (_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            var normalized = ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                span.WidthCells,
                span.HeightCells);
            if (runtimeDescriptor.Definition.ResizeMode == DesktopComponentResizeMode.Free)
            {
                return normalized;
            }

            return NormalizeAspectRatioForComponent(componentId, normalized);
        }

        return NormalizeAspectRatioForComponent(
            componentId,
            (Math.Max(1, span.WidthCells), Math.Max(1, span.HeightCells)));
    }

    private DesktopComponentResizeMode GetComponentResizeMode(string componentId)
    {
        if (_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            return runtimeDescriptor.Definition.ResizeMode;
        }

        return DesktopComponentResizeMode.Proportional;
    }

    private static (int WidthCells, int HeightCells) NormalizeAspectRatioForComponent(
        string componentId,
        (int WidthCells, int HeightCells) span)
    {
        if (string.Equals(componentId, BuiltInComponentIds.DesktopWhiteboard, StringComparison.OrdinalIgnoreCase))
        {
            // Support both portrait ratios and snap to nearest viable scale tier.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 1, HeightUnit: 2, MinScale: 2), // 2x4, 3x6, 4x8...
                new ComponentScaleRule(WidthUnit: 3, HeightUnit: 4, MinScale: 1)); // 3x4, 6x8...
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopBlackboardLandscape, StringComparison.OrdinalIgnoreCase))
        {
            // Support both landscape ratios and snap to nearest viable scale tier.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2), // 4x2, 6x3, 8x4...
                new ComponentScaleRule(WidthUnit: 4, HeightUnit: 3, MinScale: 1)); // 4x3, 8x6...
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopDailyPoetry, StringComparison.OrdinalIgnoreCase))
        {
            // Keep recommendation card at a 2:1 ratio with a minimum footprint of 4x2.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopCnrDailyNews, StringComparison.OrdinalIgnoreCase))
        {
            // Keep CNR widget at a 2:1 ratio: 4x2, 6x3, 8x4...
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopIfengNews, StringComparison.OrdinalIgnoreCase))
        {
            // Keep iFeng news widget square with a minimum footprint of 4x4.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 1, HeightUnit: 1, MinScale: 4));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopBilibiliHotSearch, StringComparison.OrdinalIgnoreCase))
        {
            // Keep Bilibili hot search widget at a 2:1 ratio: 4x2, 6x3, 8x4...
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopBaiduHotSearch, StringComparison.OrdinalIgnoreCase))
        {
            // Keep Baidu hot search widget at a 2:1 ratio: 4x2, 6x3, 8x4...
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopStcn24Forum, StringComparison.OrdinalIgnoreCase))
        {
            // Keep STCN forum widget square with a minimum footprint of 4x4.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 1, HeightUnit: 1, MinScale: 4));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopExchangeRateCalculator, StringComparison.OrdinalIgnoreCase))
        {
            // Keep exchange rate converter square with minimum size 4x4.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 1, HeightUnit: 1, MinScale: 4));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopStudyNoiseCurve, StringComparison.OrdinalIgnoreCase))
        {
            // Keep noise curve widget in a 2:1 ratio with minimum 4x2.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopWorldClock, StringComparison.OrdinalIgnoreCase))
        {
            // Keep world clock widget at 2:1 ratio: 4x2, 6x3, 8x4...
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopStudyScoreOverview, StringComparison.OrdinalIgnoreCase))
        {
            // Keep score overview widget square: 4x4, 5x5, 6x6...
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 1, HeightUnit: 1, MinScale: 4));
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopZhiJiaoHub, StringComparison.OrdinalIgnoreCase))
        {
            // ZhiJiao Hub allows free resize but starts at 2x2
            // Allow any aspect ratio, minimum 2x2
            var width = Math.Max(2, span.WidthCells);
            var height = Math.Max(2, span.HeightCells);
            return (width, height);
        }

        return span;
    }

    private static (int WidthCells, int HeightCells) SnapSpanToScaleRules(
        (int WidthCells, int HeightCells) span,
        params ComponentScaleRule[] rules)
    {
        var targetWidth = Math.Max(1, span.WidthCells);
        var targetHeight = Math.Max(1, span.HeightCells);

        var hasCandidate = false;
        var bestWidth = targetWidth;
        var bestHeight = targetHeight;
        var bestArea = -1;
        var bestDistance = double.MaxValue;

        foreach (var rule in rules)
        {
            if (rule.WidthUnit <= 0 || rule.HeightUnit <= 0 || rule.MinScale <= 0)
            {
                continue;
            }

            var maxScale = Math.Min(targetWidth / rule.WidthUnit, targetHeight / rule.HeightUnit);
            if (maxScale < rule.MinScale)
            {
                continue;
            }

            for (var scale = rule.MinScale; scale <= maxScale; scale++)
            {
                var width = rule.WidthUnit * scale;
                var height = rule.HeightUnit * scale;
                var area = width * height;
                var dx = targetWidth - width;
                var dy = targetHeight - height;
                var distance = dx * dx + dy * dy;

                if (!hasCandidate ||
                    area > bestArea ||
                    (area == bestArea && distance < bestDistance))
                {
                    hasCandidate = true;
                    bestWidth = width;
                    bestHeight = height;
                    bestArea = area;
                    bestDistance = distance;
                }
            }
        }

        return hasCandidate
            ? (bestWidth, bestHeight)
            : (targetWidth, targetHeight);
    }

    private double GetComponentCornerRadius(string componentId)
    {
        var appearanceSnapshot = HostAppearanceThemeProvider.GetOrCreate().GetCurrent();

        if (_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            return runtimeDescriptor.ResolveCornerRadius(new ComponentChromeContext(
                componentId,
                null,
                _currentDesktopCellSize,
                appearanceSnapshot.CornerRadiusTokens));
        }

        return Math.Max(0d, appearanceSnapshot.CornerRadiusTokens.Component.TopLeft);
    }

    private Thickness GetDesktopComponentVisualInset(int widthCells, int heightCells)
    {
        // Keep the drop/selection bounds on grid cells while reducing visual footprint.
        var baseInset = Math.Clamp(_currentDesktopCellSize * 0.08, 2, 10);
        var horizontal = Math.Clamp(baseInset + Math.Max(0, widthCells - 1) * 0.25, 2, 12);
        var vertical = Math.Clamp(baseInset * 0.85 + Math.Max(0, heightCells - 1) * 0.2, 2, 10);
        return new Thickness(horizontal, vertical, horizontal, vertical);
    }

    private static Border? FindDesktopComponentHost(Visual? visual)
    {
        var current = visual;
        while (current is not null)
        {
            if (current is Border border && border.Classes.Contains(DesktopComponentHostClass))
            {
                return border;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static Border? TryGetContentHost(Border host)
    {
        if (host.Child is Grid hostChrome)
        {
            return hostChrome.Children
                .OfType<Border>()
                .FirstOrDefault(child =>
                    string.Equals(child.Tag?.ToString(), DesktopComponentContentHostTag, StringComparison.Ordinal));
        }

        return null;
    }

    private static void ClearTimeZoneServiceBindings(IEnumerable<Control> roots)
    {
        foreach (var root in roots)
        {
            ClearTimeZoneServiceBindings(root);
        }
    }

    private static void ClearTimeZoneServiceBindings(Control root)
    {
        if (root is ITimeZoneAwareComponentWidget timeZoneAwareRoot)
        {
            timeZoneAwareRoot.ClearTimeZoneService();
        }

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is ITimeZoneAwareComponentWidget timeZoneAwareChild)
            {
                timeZoneAwareChild.ClearTimeZoneService();
            }
        }
    }

    private void InvalidateDesktopPageAwareComponentContextCache()
    {
        _desktopPageContextInitialized = false;
        _desktopPageContextActiveMask = 0;
    }

    private int BuildDesktopPageAwareComponentActiveMask()
    {
        if (_isSettingsOpen)
        {
            return 0;
        }

        var activeMask = 0;
        if (_desktopSurfacePageWidth > 1 &&
            _desktopPagesHostTransform is not null &&
            (_isDesktopSwipeActive ||
             _desktopPageContextSettlingSourceIndex is not null ||
             _desktopPageContextSettlingTargetIndex is not null))
        {
            var viewportLeft = -_desktopPagesHostTransform.X;
            var viewportRight = viewportLeft + _desktopSurfacePageWidth;
            for (var pageIndex = 0; pageIndex < _desktopPageCount; pageIndex++)
            {
                var pageLeft = pageIndex * _desktopSurfacePageWidth;
                var pageRight = pageLeft + _desktopSurfacePageWidth;
                if (pageRight > viewportLeft + 0.5d && pageLeft < viewportRight - 0.5d)
                {
                    activeMask |= 1 << pageIndex;
                }
            }
        }

        if (_currentDesktopSurfaceIndex >= 0 && _currentDesktopSurfaceIndex < _desktopPageCount)
        {
            activeMask |= 1 << _currentDesktopSurfaceIndex;
        }

        if (_desktopPageContextSettlingSourceIndex is int sourceIndex &&
            sourceIndex >= 0 &&
            sourceIndex < _desktopPageCount)
        {
            activeMask |= 1 << sourceIndex;
        }

        if (_desktopPageContextSettlingTargetIndex is int targetIndex &&
            targetIndex >= 0 &&
            targetIndex < _desktopPageCount)
        {
            activeMask |= 1 << targetIndex;
        }

        return activeMask;
    }

    private void UpdateDesktopPageAwareComponentContext()
    {
        var isEditMode = _isComponentLibraryOpen || _isSettingsOpen;
        var activeMask = BuildDesktopPageAwareComponentActiveMask();
        var pageUpdateMask = !_desktopPageContextInitialized || isEditMode != _desktopPageContextEditMode
            ? _desktopPageComponentGrids.Keys.Aggregate(0, (mask, pageIndex) => mask | (1 << pageIndex))
            : activeMask ^ _desktopPageContextActiveMask;

        if (_desktopPageContextInitialized &&
            pageUpdateMask == 0 &&
            isEditMode == _desktopPageContextEditMode &&
            activeMask == _desktopPageContextActiveMask)
        {
            return;
        }

        foreach (var pair in _desktopPageComponentGrids)
        {
            var pageBit = 1 << pair.Key;
            if ((pageUpdateMask & pageBit) == 0)
            {
                continue;
            }

            var isOnActivePage = (activeMask & pageBit) != 0;
            foreach (var host in pair.Value.Children.OfType<Border>())
            {
                if (!host.Classes.Contains(DesktopComponentHostClass))
                {
                    continue;
                }

                if (TryGetContentHost(host)?.Child is Control componentRoot)
                {
                    ApplyDesktopPageContext(componentRoot, isOnActivePage, isEditMode);
                }
            }
        }

        _desktopPageContextInitialized = true;
        _desktopPageContextEditMode = isEditMode;
        _desktopPageContextActiveMask = activeMask;
    }

    private static void ApplyDesktopPageContext(Control root, bool isOnActivePage, bool isEditMode)
    {
        if (root is IDesktopPageVisibilityAwareComponentWidget awareRoot)
        {
            awareRoot.SetDesktopPageContext(isOnActivePage, isEditMode);
        }

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is IDesktopPageVisibilityAwareComponentWidget awareChild)
            {
                awareChild.SetDesktopPageContext(isOnActivePage, isEditMode);
            }
        }
    }

    private static Border? TryGetResizeHandle(Border host)
    {
        if (host.Child is Grid hostChrome)
        {
            return hostChrome.Children
                .OfType<Border>()
                .FirstOrDefault(child =>
                    string.Equals(child.Tag?.ToString(), DesktopComponentResizeHandleTag, StringComparison.Ordinal));
        }

        return null;
    }

    private bool IsPointerOnSelectedFrameBorder(Border host, Point pointerInHost)
    {
        if (host != _selectedDesktopComponentHost || !_isComponentLibraryOpen)
        {
            return false;
        }

        var width = host.Bounds.Width;
        var height = host.Bounds.Height;
        if (width <= 1 || height <= 1)
        {
            return false;
        }

        var borderBand = Math.Clamp(_currentDesktopCellSize * 0.15, 8, 22);
        var onLeft = pointerInHost.X <= borderBand;
        var onRight = pointerInHost.X >= width - borderBand;
        var onTop = pointerInHost.Y <= borderBand;
        var onBottom = pointerInHost.Y >= height - borderBand;
        return onLeft || onRight || onTop || onBottom;
    }

    private Control? CreateDesktopComponentControl(string componentId, string? placementId = null, int? pageIndex = null)
    {
        if (!_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            return null;
        }

        return CreateDesktopComponentControl(runtimeDescriptor, _currentDesktopCellSize, placementId, pageIndex, "DesktopSurface");
    }

    private Control? CreateDesktopComponentControl(
        string componentId,
        double cellSize,
        string? placementId,
        int? pageIndex,
        string action)
    {
        if (!_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            return null;
        }

        return CreateDesktopComponentControl(runtimeDescriptor, cellSize, placementId, pageIndex, action);
    }

    private Control? CreateDesktopComponentControl(
        DesktopComponentRuntimeDescriptor runtimeDescriptor,
        double cellSize,
        string? placementId,
        int? pageIndex,
        string action)
    {
        try
        {
            var appearanceSnapshot = HostAppearanceThemeProvider.GetOrCreate().GetCurrent();
            var createContext = new ComponentLibraryCreateContext(
                cellSize,
                _timeZoneService,
                _weatherDataService,
                _recommendationInfoService,
                _calculatorDataService,
                _settingsFacade,
                placementId);
            if (!_componentLibraryService.TryCreateControl(runtimeDescriptor.Definition.Id, createContext, out var component, out var exception) ||
                component is null)
            {
                throw exception ?? new InvalidOperationException("Component library service returned no control.");
            }

            component.Classes.Add(DesktopComponentClass);
            return component;
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn(
                "ComponentRuntime",
                $"Action={action}; ComponentId={runtimeDescriptor.Definition.Id}; PlacementId={placementId ?? string.Empty}; PageIndex={pageIndex?.ToString() ?? string.Empty}; ExceptionType={ex.GetType().FullName}; IsFatal=false",
                ex);

            var failureView = new DesktopComponentFailureView(
                runtimeDescriptor.Definition.DisplayName,
                runtimeDescriptor.Definition.Id,
                placementId,
                pageIndex,
                action,
                ex);
            failureView.ApplyCellSize(cellSize);
            failureView.Classes.Add(DesktopComponentClass);
            return failureView;
        }
    }

    internal bool IsComponentLibraryOpenFromService => _isComponentLibraryOpen;
    internal bool IsDetachedComponentLibraryWindowOpenFromService => _detachedComponentLibraryWindow is { IsVisible: true };

    internal void OpenComponentLibraryWindowFromService()
    {
        OpenComponentLibraryWindow();
    }

    internal void CloseComponentLibraryWindowFromService()
    {
        CloseComponentLibraryWindow(reopenSettings: false);
    }

    internal void OpenDetachedComponentLibraryWindowFromService()
    {
        OpenDetachedComponentLibraryWindow();
    }

    internal void CloseDetachedComponentLibraryWindowFromService()
    {
        CloseDetachedComponentLibraryWindow();
    }

    private void CollapseComponentLibraryPanel()
    {
        // Animate component library panel collapsing downward
        if (ComponentLibraryWindow is not null)
        {
            ComponentLibraryWindow.Height = 0;
            ComponentLibraryWindow.IsVisible = false;
        }

        _isComponentLibraryOpen = false;
        CancelDesktopComponentDrag();
        CancelDesktopComponentResize(restoreOriginalSpan: true);
        CloseDetachedComponentLibraryWindow();
        ClearDesktopComponentSelection();
        ClearSelectedLauncherTile(refreshTaskbar: false);
        UpdateDesktopComponentHostEditState();
        ClearComponentLibraryPreviewControls();
        UpdateComponentLibraryLayout(_currentDesktopCellSize);
    }

    private void UpdateDesktopComponentHostEditState()
    {
        foreach (var pageGrid in _desktopPageComponentGrids.Values)
        {
            foreach (var child in pageGrid.Children)
            {
                if (child is Border host && host.Classes.Contains(DesktopComponentHostClass))
                {
                    ApplyDesktopEditStateToHost(host, _isComponentLibraryOpen);
                }
            }
        }

        UpdateDesktopPageAwareComponentContext();
    }

    private void ApplyDesktopEditStateToHost(Border host, bool isEditMode)
    {
        host.IsHitTestVisible = true;
        var keepContentInteractive = ShouldKeepContentInteractiveInEditMode(host);

        if (TryGetContentHost(host) is Border contentHost)
        {
            // In edit mode, keep selected interactive widgets usable; drag/resize still uses host border/handles.
            contentHost.IsHitTestVisible = !isEditMode || keepContentInteractive;
            if (contentHost.Child is Control componentControl)
            {
                componentControl.IsHitTestVisible = !isEditMode || keepContentInteractive;
            }
        }

        var isSelected = host == _selectedDesktopComponentHost;
        ApplySelectionStateToHost(host, isSelected);
    }

    private bool ShouldKeepContentInteractiveInEditMode(Border host)
    {
        if (!_isComponentLibraryOpen ||
            host.Tag is not string placementId)
        {
            return false;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return false;
        }

        return string.Equals(
            placement.ComponentId,
            BuiltInComponentIds.DesktopStudySessionHistory,
            StringComparison.OrdinalIgnoreCase);
    }

    private void OnDesktopComponentHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen || HasActiveDesktopEditSession)
        {
            return;
        }

        if (DesktopPagesViewport is null ||
            sender is not Border host ||
            host.Tag is not string placementId ||
            !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return;
        }

        var wasSelected = host == _selectedDesktopComponentHost;
        SetSelectedDesktopComponent(host);
        if (!wasSelected)
        {
            e.Handled = true;
            return;
        }

        var pointerInHost = e.GetPosition(host);
        if (IsPointerOnSelectedFrameBorder(host, pointerInHost))
        {
            BeginDesktopComponentResizeDrag(host, placement, e);
            if (IsDesktopEditResizeMode)
            {
                e.Handled = true;
            }

            return;
        }

        BeginDesktopComponentMoveDrag(host, placement, e);
        e.Handled = true;
    }

    private void SetSelectedDesktopComponent(Border? host)
    {
        ClearSelectedLauncherTile(refreshTaskbar: false);

        // Clear previous selection
        if (_selectedDesktopComponentHost is not null && _selectedDesktopComponentHost != host)
        {
            ApplySelectionStateToHost(_selectedDesktopComponentHost, false);
        }

        // Set new selection
        _selectedDesktopComponentHost = host;
        if (host is not null)
        {
            ApplySelectionStateToHost(host, true);
        }

        // Refresh taskbar actions to show delete/edit buttons
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void ApplySelectionStateToHost(Border host, bool isSelected)
    {
        var showSelection = isSelected && _isComponentLibraryOpen;
        host.BorderThickness = showSelection
            ? new Thickness(Math.Clamp(_currentDesktopCellSize * 0.04, 1, 3))
            : new Thickness(0);
        host.BorderBrush = showSelection ? GetThemeBrush("AdaptiveAccentBrush") : null;

        if (TryGetResizeHandle(host) is Border resizeHandle)
        {
            resizeHandle.IsVisible = showSelection;
            resizeHandle.IsHitTestVisible = showSelection;
        }
    }

    private void ClearDesktopComponentSelection()
    {
        if (_selectedDesktopComponentHost is not null)
        {
            ApplySelectionStateToHost(_selectedDesktopComponentHost, false);
            _selectedDesktopComponentHost = null;
        }

        _componentEditorWindowService.Close();
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void BeginDesktopComponentMoveDrag(Border sourceHost, DesktopComponentPlacementSnapshot placement, PointerPressedEventArgs e)
    {
        if (HasActiveDesktopEditSession ||
            DesktopPagesViewport is null ||
            !TryGetCurrentDesktopGridGeometry(out var grid) ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var runtimeDescriptor))
        {
            return;
        }

        var (widthCells, heightCells) = NormalizeComponentCellSpan(
            placement.ComponentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                placement.WidthCells,
                placement.HeightCells));

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        _desktopEditOriginalRect = DesktopPlacementMath.GetCellRect(grid, placement.Column, placement.Row, widthCells, heightCells);
        _desktopEditStartRow = placement.Row;
        _desktopEditStartColumn = placement.Column;
        var pointerOffset = DesktopPlacementMath.Subtract(
            pointerInViewport,
            new Point(_desktopEditOriginalRect.X, _desktopEditOriginalRect.Y));

        _desktopEditSession = DesktopEditSession.CreateDraggingExisting(
            placement.ComponentId,
            placement.PlacementId,
            placement.PageIndex,
            widthCells,
            heightCells,
            pointerInViewport,
            pointerOffset,
            GetComponentLibraryBoundsInViewport());

        CollapseComponentLibraryForDesktopEdit(ResolveDesktopEditTitle(placement.ComponentId));
        SetDesktopEditSourceHost(sourceHost, 0.22);
        EnsureDesktopEditOverlayPresenter();
        UpdateDesktopEditOverlayMetadata(placement.ComponentId, widthCells, heightCells, L("component.move", "Move"));
        ApplyDesktopEditOverlayPreviewImage(placement.ComponentId, placement.PlacementId, widthCells, heightCells);
        PrimeDesktopEditPreviewImage(
            placement.ComponentId,
            placement.PlacementId,
            placement.PageIndex,
            widthCells,
            heightCells);
        _desktopEditOverlayPresenter?.SetPreviewRect(_desktopEditOriginalRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(_desktopEditOriginalRect);
        _desktopEditOverlayPresenter?.SetInvalid(false);
        _desktopEditOverlayPresenter?.Show(DesktopEditGhostVisualStyle.StandardLift);
        UpdateDesktopEditSession(pointerInViewport);

        e.Pointer.Capture(this);
    }

    private void BeginDesktopComponentNewDrag(string componentId, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            HasActiveDesktopEditSession ||
            DesktopPagesViewport is null ||
            _currentDesktopCellSize <= 0 ||
            !_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor) ||
            !runtimeDescriptor.Definition.AllowDesktopPlacement)
        {
            return;
        }

        var (widthCells, heightCells) = NormalizeComponentCellSpan(
            componentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                runtimeDescriptor.Definition.MinWidthCells,
                runtimeDescriptor.Definition.MinHeightCells));

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        var previewSize = GetComponentPixelSize(widthCells, heightCells, _currentDesktopCellSize, _currentDesktopCellGap);
        var pointerOffset = new Point(previewSize.Width * 0.5, previewSize.Height * 0.5);

        _desktopEditOriginalRect = new Rect(
            DesktopPlacementMath.Subtract(pointerInViewport, pointerOffset),
            previewSize);
        _desktopEditSession = DesktopEditSession.CreatePendingNew(
            componentId,
            _currentDesktopSurfaceIndex,
            widthCells,
            heightCells,
            pointerInViewport,
            pointerOffset,
            GetComponentLibraryBoundsInViewport());

        EnsureDesktopEditOverlayPresenter();
        UpdateDesktopEditOverlayMetadata(componentId, widthCells, heightCells, L("component_library.drag_hint", "Drag to place"));
        ApplyDesktopEditOverlayPreviewImage(componentId, placementId: null, widthCells, heightCells);
        PrimeDesktopEditPreviewImage(
            componentId,
            placementId: null,
            _currentDesktopSurfaceIndex,
            widthCells,
            heightCells);
        _desktopEditOverlayPresenter?.SetPreviewRect(_desktopEditOriginalRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(null);
        _desktopEditOverlayPresenter?.SetInvalid(false);

        e.Pointer.Capture(this);
    }

    private void OnDesktopComponentResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            HasActiveDesktopEditSession ||
            DesktopPagesViewport is null ||
            sender is not Border handle ||
            !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var host = FindDesktopComponentHost(handle);
        if (host?.Tag is not string placementId)
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return;
        }

        SetSelectedDesktopComponent(host);
        BeginDesktopComponentResizeDrag(host, placement, e);
        if (IsDesktopEditResizeMode)
        {
            e.Handled = true;
        }
    }

    private void BeginDesktopComponentResizeDrag(
        Border sourceHost,
        DesktopComponentPlacementSnapshot placement,
        PointerPressedEventArgs e)
    {
        if (HasActiveDesktopEditSession ||
            DesktopPagesViewport is null ||
            _currentDesktopCellSize <= 0 ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var runtimeDescriptor) ||
            !_desktopPageComponentGrids.TryGetValue(placement.PageIndex, out var pageGrid) ||
            !TryGetCurrentDesktopGridGeometry(out var grid))
        {
            return;
        }

        var startSpan = NormalizeComponentCellSpan(
            placement.ComponentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                placement.WidthCells,
                placement.HeightCells));

        var minSpan = NormalizeComponentCellSpan(
            placement.ComponentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                runtimeDescriptor.Definition.MinWidthCells,
                runtimeDescriptor.Definition.MinHeightCells));

        var maxWidthCells = Math.Max(startSpan.WidthCells, pageGrid.ColumnDefinitions.Count - placement.Column);
        var maxHeightCells = Math.Max(startSpan.HeightCells, pageGrid.RowDefinitions.Count - placement.Row);
        if (maxWidthCells <= 0 || maxHeightCells <= 0)
        {
            return;
        }

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        _desktopEditOriginalRect = DesktopPlacementMath.GetCellRect(
            grid,
            placement.Column,
            placement.Row,
            startSpan.WidthCells,
            startSpan.HeightCells);
        _desktopEditStartWidthCells = startSpan.WidthCells;
        _desktopEditStartHeightCells = startSpan.HeightCells;
        _desktopEditMinWidthCells = Math.Max(1, Math.Min(minSpan.WidthCells, maxWidthCells));
        _desktopEditMinHeightCells = Math.Max(1, Math.Min(minSpan.HeightCells, maxHeightCells));
        _desktopEditMaxWidthCells = maxWidthCells;
        _desktopEditMaxHeightCells = maxHeightCells;
        _desktopEditResizeMode = runtimeDescriptor.Definition.ResizeMode;

        _desktopEditSession = DesktopEditSession.CreateResizingExisting(
            placement.ComponentId,
            placement.PlacementId,
            placement.PageIndex,
            startSpan.WidthCells,
            startSpan.HeightCells,
            pointerInViewport,
            GetComponentLibraryBoundsInViewport()) with
        {
            TargetRow = placement.Row,
            TargetColumn = placement.Column
        };

        CollapseComponentLibraryForDesktopEdit(ResolveDesktopEditTitle(placement.ComponentId));
        SetDesktopEditSourceHost(sourceHost, 0.22);
        EnsureDesktopEditOverlayPresenter();
        UpdateDesktopEditOverlayMetadata(placement.ComponentId, startSpan.WidthCells, startSpan.HeightCells, L("component.resize", "Resize"));
        ApplyDesktopEditOverlayPreviewImage(placement.ComponentId, placement.PlacementId, startSpan.WidthCells, startSpan.HeightCells);
        PrimeDesktopEditPreviewImage(
            placement.ComponentId,
            placement.PlacementId,
            placement.PageIndex,
            startSpan.WidthCells,
            startSpan.HeightCells);
        _desktopEditOverlayPresenter?.SetPreviewRect(_desktopEditOriginalRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(_desktopEditOriginalRect);
        _desktopEditOverlayPresenter?.SetInvalid(false);
        _desktopEditOverlayPresenter?.Show(DesktopEditGhostVisualStyle.StandardLift);
        UpdateDesktopEditSession(pointerInViewport);
        e.Pointer.Capture(this);
    }

    private void CancelDesktopComponentResize(bool restoreOriginalSpan)
    {
        if (!IsDesktopEditResizeMode && !_isDesktopEditCommitPending)
        {
            return;
        }

        if (restoreOriginalSpan && _desktopEditSourceHost is not null)
        {
            Grid.SetColumnSpan(_desktopEditSourceHost, Math.Max(1, _desktopEditStartWidthCells));
            Grid.SetRowSpan(_desktopEditSourceHost, Math.Max(1, _desktopEditStartHeightCells));
        }

        CancelDesktopEditSession(animate: false);
    }

    private void OnDesktopComponentDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DesktopPagesViewport is null)
        {
            return;
        }

        if (!HasActiveDesktopEditSession || _isDesktopEditCommitPending)
        {
            return;
        }

        UpdateDesktopEditSession(e.GetPosition(DesktopPagesViewport));
    }

    private void OnDesktopComponentDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DesktopPagesViewport is null)
        {
            return;
        }

        if (!HasActiveDesktopEditSession)
        {
            return;
        }

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        var success = CompleteDesktopEditSession(pointerInViewport);
        if (!success)
        {
            CancelDesktopEditSession(animate: !_desktopEditSession.IsPendingNew);
        }

        e.Pointer.Capture(null);
        if (success)
        {
            e.Handled = true;
        }
    }

    private void OnDesktopComponentDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDesktopEditCommitPending)
        {
            return;
        }

        if (!HasActiveDesktopEditSession)
        {
            return;
        }

        CancelDesktopEditSession(animate: !_desktopEditSession.IsPendingNew);
    }

    private bool TryMoveExistingDesktopComponent(string placementId, int row, int column)
    {
        if (string.IsNullOrWhiteSpace(placementId))
        {
            return false;
        }

        if (!TryGetDesktopPlacementById(placementId, out var placement))
        {
            return false;
        }

        var before = ClonePlacementSnapshot(placement);
        if (!DesktopPlacementMath.HasCellPositionChanged(placement.Row, placement.Column, row, column))
        {
            return false;
        }

        var host = _desktopEditSourceHost;
        if (host is null &&
            _desktopPageComponentGrids.TryGetValue(placement.PageIndex, out var pageGrid))
        {
            host = pageGrid.Children
                .OfType<Border>()
                .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, placementId, StringComparison.OrdinalIgnoreCase));
        }

        placement.Row = Math.Max(0, row);
        placement.Column = Math.Max(0, column);

        if (host is not null)
        {
            Grid.SetRow(host, placement.Row);
            Grid.SetColumn(host, placement.Column);
            ApplyDesktopEditStateToHost(host, _isComponentLibraryOpen);
        }

        PersistSettings();
        TelemetryServices.Usage?.TrackDesktopComponentMoved(before, ClonePlacementSnapshot(placement), "component.move");
        return true;
    }

    private void CancelDesktopComponentDrag()
    {
        if (!IsDesktopEditDragMode && !_isDesktopEditCommitPending)
        {
            return;
        }

        CancelDesktopEditSession(animate: false);
    }

    private void ShowComponentLibraryCategoryView()
    {
        if (ComponentLibraryCategoriesView is not null)
        {
            ComponentLibraryCategoriesView.IsVisible = true;
        }

        if (ComponentLibraryComponentsView is not null)
        {
            ComponentLibraryComponentsView.IsVisible = false;
        }
    }

    private void ShowComponentLibraryComponentsView()
    {
        if (ComponentLibraryCategoriesView is not null)
        {
            ComponentLibraryCategoriesView.IsVisible = false;
        }

        if (ComponentLibraryComponentsView is not null)
        {
            ComponentLibraryComponentsView.IsVisible = true;
        }
    }

    private void BuildComponentLibraryCategoryPages()
    {
        if (ComponentLibraryCategoryViewport is null ||
            ComponentLibraryCategoryPagesHost is null ||
            ComponentLibraryCategoryPagesContainer is null ||
            ComponentLibraryEmptyTextBlock is null)
        {
            return;
        }

        _componentLibraryCategories = GetComponentLibraryCategories();
        var categoryCount = _componentLibraryCategories.Count;
        ComponentLibraryEmptyTextBlock.IsVisible = categoryCount == 0;

        ComponentLibraryCategoryPagesContainer.Children.Clear();
        ComponentLibraryCategoryPagesContainer.RowDefinitions.Clear();
        ComponentLibraryCategoryPagesContainer.ColumnDefinitions.Clear();
        ComponentLibraryCategoryPagesContainer.Width = double.NaN;
        ComponentLibraryCategoryPagesContainer.Height = double.NaN;
        ComponentLibraryCategoryPagesHost.Width = double.NaN;
        ComponentLibraryCategoryPagesHost.Height = double.NaN;

        if (categoryCount == 0)
        {
            _componentLibraryCategoryIndex = 0;
            _componentLibraryActiveCategoryId = null;
            UpdateComponentLibraryComponentNavigationButtons();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_componentLibraryActiveCategoryId))
        {
            var activeIndex = _componentLibraryCategories
                .Select((category, index) => (category, index))
                .FirstOrDefault(tuple =>
                    string.Equals(tuple.category.Id, _componentLibraryActiveCategoryId, StringComparison.OrdinalIgnoreCase))
                .index;
            _componentLibraryCategoryIndex = Math.Clamp(activeIndex, 0, Math.Max(0, categoryCount - 1));
        }
        else
        {
            _componentLibraryCategoryIndex = Math.Clamp(_componentLibraryCategoryIndex, 0, Math.Max(0, categoryCount - 1));
        }

        _componentLibraryActiveCategoryId = _componentLibraryCategories[_componentLibraryCategoryIndex].Id;

        ComponentLibraryCategoryPagesContainer.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        for (var i = 0; i < categoryCount; i++)
        {
            var category = _componentLibraryCategories[i];
            var isSelected = i == _componentLibraryCategoryIndex;
            var row = new RowDefinition(GridLength.Auto);
            ComponentLibraryCategoryPagesContainer.RowDefinitions.Add(row);

            var icon = new SymbolIcon
            {
                Symbol = category.Icon,
                IconVariant = IconVariant.Regular,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new TextBlock
            {
                Text = category.Title,
                FontSize = 15,
                FontWeight = isSelected ? FontWeight.Bold : FontWeight.SemiBold,
                Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children = { icon, title }
            };
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(title, 1);

            var itemButton = new Button
            {
                Tag = i,
                Margin = new Thickness(0, 0, 0, i < categoryCount - 1 ? 8 : 0),
                Padding = new Thickness(12, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = isSelected
                    ? GetThemeBrush("AdaptiveNavItemSelectedBackgroundBrush")
                    : GetThemeBrush("AdaptiveNavItemBackgroundBrush"),
                BorderBrush = GetThemeBrush("AdaptiveButtonBorderBrush"),
                BorderThickness = new Thickness(isSelected ? 1.5 : 1),
                Content = contentGrid
            };
            itemButton.Click += OnComponentLibraryCategoryItemClick;

            Grid.SetRow(itemButton, i);
            Grid.SetColumn(itemButton, 0);
            ComponentLibraryCategoryPagesContainer.Children.Add(itemButton);
        }

        _componentLibraryCategoryHostTransform = null;
        _componentLibraryCategoryPageWidth = 0;

        if (ComponentLibraryBackTextBlock is not null)
        {
            ComponentLibraryBackTextBlock.Text = L("common.back", "Back");
        }

        EnsureComponentLibraryPreviewWarmup();
    }

    private IReadOnlyList<ComponentLibraryCategory> GetComponentLibraryCategories()
    {
        var categories = _componentLibraryService.GetDesktopCategories();
        if (categories.Count == 0)
        {
            return Array.Empty<ComponentLibraryCategory>();
        }

        return categories
            .Select(category => new ComponentLibraryCategory(
                category.Id,
                ResolveComponentLibraryCategoryIcon(category.Id),
                GetLocalizedComponentLibraryCategoryTitle(category.Id),
                category.Components))
            .ToList();
    }

    private Symbol ResolveComponentLibraryCategoryIcon(string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Clock;
        }

        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.CalendarDate;
        }

        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.WeatherSunny;
        }

        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Edit;
        }

        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Play;
        }

        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Apps;
        }

        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Calculator;
        }

        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Hourglass;
        }

        if (string.Equals(categoryId, "File", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Folder;
        }

        return Symbol.Apps;
    }

    private string GetLocalizedComponentLibraryCategoryTitle(string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.clock", "Clock");
        }

        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.date", "Calendar");
        }

        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.weather", "Weather");
        }

        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.board", "Board");
        }

        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.media", "Media");
        }

        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.info", "Info");
        }

        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.calculator", "Calculator");
        }

        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.study", "Study");
        }

        if (string.Equals(categoryId, "File", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.file", "File");
        }

        return categoryId;
    }

    private void ApplyComponentLibraryCategoryOffset()
    {
        if (_componentLibraryCategoryHostTransform is null || _componentLibraryCategoryPageWidth <= 0)
        {
            return;
        }

        _componentLibraryCategoryHostTransform.X = -_componentLibraryCategoryIndex * _componentLibraryCategoryPageWidth;
    }

    private void ApplyComponentLibraryComponentOffset()
    {
        if (_componentLibraryComponentHostTransform is null || _componentLibraryComponentPageWidth <= 0)
        {
            return;
        }

        _componentLibraryComponentHostTransform.X = -_componentLibraryComponentIndex * _componentLibraryComponentPageWidth;
        UpdateComponentLibraryComponentNavigationButtons();
    }

    private void UpdateComponentLibraryComponentNavigationButtons()
    {
        if (ComponentLibraryPrevComponentButton is null || ComponentLibraryNextComponentButton is null)
        {
            return;
        }

        var maxIndex = Math.Max(0, _componentLibraryActiveComponents.Count - 1);
        var hasMultiplePages = maxIndex > 0;

        ComponentLibraryPrevComponentButton.IsVisible = hasMultiplePages;
        ComponentLibraryNextComponentButton.IsVisible = hasMultiplePages;

        if (!hasMultiplePages)
        {
            ComponentLibraryPrevComponentButton.IsEnabled = false;
            ComponentLibraryNextComponentButton.IsEnabled = false;
            return;
        }

        ComponentLibraryPrevComponentButton.IsEnabled = _componentLibraryComponentIndex > 0;
        ComponentLibraryNextComponentButton.IsEnabled = _componentLibraryComponentIndex < maxIndex;
    }

    private void OpenComponentLibraryCurrentCategory()
    {
        if (_componentLibraryCategories.Count == 0)
        {
            return;
        }

        _componentLibraryCategoryIndex = Math.Clamp(_componentLibraryCategoryIndex, 0, Math.Max(0, _componentLibraryCategories.Count - 1));
        var category = _componentLibraryCategories[_componentLibraryCategoryIndex];
        _componentLibraryActiveCategoryId = category.Id;
        _componentLibraryComponentIndex = 0;
        _ = WarmComponentLibraryCategoryPreviewsAsync(category);
        BuildComponentLibraryComponentPages(category);
        ShowComponentLibraryComponentsView();
    }

    private void BuildComponentLibraryComponentPages(ComponentLibraryCategory category)
    {
        if (ComponentLibraryComponentViewport is null ||
            ComponentLibraryComponentPagesHost is null ||
            ComponentLibraryComponentPagesContainer is null)
        {
            return;
        }

        _componentLibraryActiveComponents = category.Components;
        var componentCount = _componentLibraryActiveComponents.Count;

        ClearTimeZoneServiceBindings(ComponentLibraryComponentPagesContainer.Children.OfType<Control>().ToList());
        ComponentLibraryComponentPagesContainer.Children.Clear();
        ComponentLibraryComponentPagesContainer.RowDefinitions.Clear();
        ComponentLibraryComponentPagesContainer.ColumnDefinitions.Clear();
        ClearComponentLibraryPreviewVisualTargets();
        if (componentCount == 0)
        {
            _componentLibraryComponentIndex = 0;
            UpdateComponentLibraryComponentNavigationButtons();
            return;
        }

        var viewportWidth = ComponentLibraryComponentViewport.Bounds.Width;
        if (viewportWidth <= 1)
        {
            if (ComponentLibraryComponentViewport.Parent is Control parent && parent.Bounds.Width > 1)
            {
                // Parent includes left/right nav buttons; reserve space to get true viewport width.
                viewportWidth = Math.Max(1, parent.Bounds.Width - 96);
            }
            else if (ComponentLibraryWindow is not null)
            {
                viewportWidth = Math.Max(1, ComponentLibraryWindow.Bounds.Width - 150);
            }
        }

        var viewportHeight = ComponentLibraryComponentViewport.Bounds.Height;
        if (viewportHeight <= 1)
        {
            if (ComponentLibraryComponentViewport.Parent is Control parent && parent.Bounds.Height > 1)
            {
                viewportHeight = Math.Max(1, parent.Bounds.Height);
            }
            else if (ComponentLibraryWindow is not null)
            {
                viewportHeight = Math.Max(1, ComponentLibraryWindow.Bounds.Height - 170);
            }
        }

        _componentLibraryComponentPageWidth = Math.Max(1, viewportWidth);
        ComponentLibraryComponentPagesHost.Width = _componentLibraryComponentPageWidth * componentCount;
        ComponentLibraryComponentPagesHost.Height = viewportHeight;
        ComponentLibraryComponentPagesContainer.Width = ComponentLibraryComponentPagesHost.Width;
        ComponentLibraryComponentPagesContainer.Height = viewportHeight;

        ComponentLibraryComponentPagesContainer.RowDefinitions.Add(new RowDefinition(new GridLength(viewportHeight, GridUnitType.Pixel)));
        for (var i = 0; i < componentCount; i++)
        {
            ComponentLibraryComponentPagesContainer.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(_componentLibraryComponentPageWidth, GridUnitType.Pixel)));
        }

        _componentLibraryComponentIndex = Math.Clamp(_componentLibraryComponentIndex, 0, Math.Max(0, componentCount - 1));

        for (var i = 0; i < componentCount; i++)
        {
            var component = _componentLibraryActiveComponents[i];

            var page = new Grid
            {
                Width = _componentLibraryComponentPageWidth,
                Height = viewportHeight,
                Background = Brushes.Transparent
            };

            // Fit the preview to the page while preserving component cell span proportions.
            var previewMaxWidth = _componentLibraryComponentPageWidth * 0.94;
            var previewMaxHeight = viewportHeight * 0.86;
            var previewSpan = NormalizeComponentCellSpan(
                component.ComponentId,
                (component.MinWidthCells, component.MinHeightCells));
            var previewCellSize = Math.Min(
                previewMaxWidth / Math.Max(1, previewSpan.WidthCells),
                previewMaxHeight / Math.Max(1, previewSpan.HeightCells));
            previewCellSize = Math.Clamp(previewCellSize, 24, 96);

            var previewWidth = previewSpan.WidthCells * previewCellSize;
            var previewHeight = previewSpan.HeightCells * previewCellSize;
            var previewKey = CreateComponentTypePreviewKey(component.ComponentId, previewSpan.WidthCells, previewSpan.HeightCells);
            var cachedPreviewImage = ResolveComponentTypePreviewImage(component.ComponentId, previewSpan.WidthCells, previewSpan.HeightCells);

            var previewImage = new Image
            {
                Width = previewWidth,
                Height = previewHeight,
                Stretch = Stretch.Uniform,
                Source = cachedPreviewImage,
                IsVisible = cachedPreviewImage is not null,
                IsHitTestVisible = false
            };

            var previewFallback = new Border
            {
                Width = previewWidth,
                Height = previewHeight,
                Background = GetThemeBrush("AdaptiveCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AdaptiveButtonBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Clamp(Math.Min(previewWidth, previewHeight) * 0.18, 12, 28)),
                IsVisible = cachedPreviewImage is null,
                Child = new TextBlock
                {
                    Text = L("component_library.preview_loading", "Preparing preview"),
                    FontSize = 11,
                    Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            RegisterComponentLibraryPreviewVisual(previewKey, previewImage, previewFallback);

            var previewSurface = new Grid
            {
                Width = previewWidth,
                Height = previewHeight,
                IsHitTestVisible = false,
                Children =
                {
                    previewImage,
                    previewFallback
                }
            };

            var previewBorder = new Border
            {
                Width = previewWidth,
                Height = previewHeight,
                ClipToBounds = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = previewSurface,
                Tag = component.ComponentId
            };
            previewBorder.PointerPressed += OnComponentLibraryComponentPreviewPointerPressed;

            var label = new TextBlock
            {
                Text = GetLocalizedComponentDisplayName(component),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var hint = new TextBlock
            {
                Text = L("component_library.drag_hint", "Drag to place"),
                FontSize = 12,
                Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    previewBorder,
                    label,
                    hint
                }
            };

            page.Children.Add(stack);

            Grid.SetRow(page, 0);
            Grid.SetColumn(page, i);
            ComponentLibraryComponentPagesContainer.Children.Add(page);

            if (cachedPreviewImage is null)
            {
                _ = EnsureComponentTypePreviewImageAsync(component.ComponentId, previewSpan.WidthCells, previewSpan.HeightCells);
            }
            else
            {
                ApplyPreviewEntryToEmbeddedVisuals(previewKey);
            }
        }

        _componentLibraryComponentHostTransform = ComponentLibraryComponentPagesHost.RenderTransform as TranslateTransform;
        if (_componentLibraryComponentHostTransform is null)
        {
            _componentLibraryComponentHostTransform = new TranslateTransform();
            ComponentLibraryComponentPagesHost.RenderTransform = _componentLibraryComponentHostTransform;
        }

        ApplyComponentLibraryComponentOffset();
        UpdateComponentLibraryComponentNavigationButtons();
    }

    private void ClearComponentLibraryPreviewControls()
    {
        if (ComponentLibraryComponentPagesContainer is null)
        {
            return;
        }

        ClearTimeZoneServiceBindings(ComponentLibraryComponentPagesContainer.Children.OfType<Control>().ToList());
        ComponentLibraryComponentPagesContainer.Children.Clear();
        ComponentLibraryComponentPagesContainer.RowDefinitions.Clear();
        ComponentLibraryComponentPagesContainer.ColumnDefinitions.Clear();
        ClearComponentLibraryPreviewVisualTargets();
    }

    private string GetLocalizedComponentDisplayName(ComponentLibraryComponentEntry component)
    {
        return string.IsNullOrWhiteSpace(component.DisplayNameLocalizationKey)
            ? component.DisplayName
            : L(component.DisplayNameLocalizationKey, component.DisplayName);
    }

    private void OnComponentLibraryComponentPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border ||
            border.Tag is not string componentId ||
            !e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginDesktopComponentNewDrag(componentId, e);
        if (HasActiveDesktopEditSession)
        {
            e.Handled = true;
        }
    }

    private bool _isComponentLibraryWindowDragging;
    private Point _componentLibraryWindowDragStartPoint;
    private Thickness _componentLibraryWindowOriginalMargin;
    private bool _isComponentLibraryWindowPositionCustomized;
    
    private void OnComponentLibraryWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ComponentLibraryWindow is null || !_isComponentLibraryOpen || IsComponentLibraryTemporarilyCollapsedForDesktopEdit())
        {
            return;
        }

        var point = e.GetPosition(ComponentLibraryWindow);
        if (point.Y > 40) // 闂傚倷绀侀幖顐ょ矓閺夋嚚娲敇椤兘鍋撻崒娑氼浄閻庯綆浜滈崬銊╂椤愩垺澶勭紒瀣崄閵囨劙顢涢悙鑼啇闁哄鐗婇崕鎶姐€呴鍕€电痪顓炴噺閻濐亞绱?0px
        {
            return;
        }

        _isComponentLibraryWindowDragging = true;
        _componentLibraryWindowDragStartPoint = e.GetPosition(this);
        _componentLibraryWindowOriginalMargin = ComponentLibraryWindow.Margin;
        
        e.Pointer.Capture(ComponentLibraryWindow);
        e.Handled = true;
    }

    private void OnComponentLibraryWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsComponentLibraryTemporarilyCollapsedForDesktopEdit())
        {
            if (_isComponentLibraryWindowDragging)
            {
                _isComponentLibraryWindowDragging = false;
                e.Pointer.Capture(null);
            }

            return;
        }

        if (!_isComponentLibraryWindowDragging || ComponentLibraryWindow is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _componentLibraryWindowDragStartPoint;
        
        var newMargin = new Thickness(
            Math.Max(10, _componentLibraryWindowOriginalMargin.Left + delta.X),
            Math.Max(10, _componentLibraryWindowOriginalMargin.Top + delta.Y),
            Math.Max(10, _componentLibraryWindowOriginalMargin.Right - delta.X),
            Math.Max(10, _componentLibraryWindowOriginalMargin.Bottom - delta.Y)
        );
        
        ComponentLibraryWindow.Margin = newMargin;
        SyncComponentLibraryCollapseExpandedState();
        e.Handled = true;
    }

    private void OnComponentLibraryWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (IsComponentLibraryTemporarilyCollapsedForDesktopEdit())
        {
            if (_isComponentLibraryWindowDragging)
            {
                _isComponentLibraryWindowDragging = false;
                e.Pointer.Capture(null);
            }

            return;
        }

        if (!_isComponentLibraryWindowDragging)
        {
            return;
        }

        _isComponentLibraryWindowDragging = false;
        e.Pointer.Capture(null);
        
        if (ComponentLibraryWindow is not null)
        {
            SaveComponentLibraryWindowPosition();
        }
        
        e.Handled = true;
    }

    private void OnComponentLibraryBackClick(object? sender, RoutedEventArgs e)
    {
        ShowComponentLibraryCategoryView();
        BuildComponentLibraryCategoryPages();
    }

    private void OnComponentLibraryCategoryItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not int categoryIndex ||
            _componentLibraryCategories.Count == 0)
        {
            return;
        }

        _componentLibraryCategoryIndex = Math.Clamp(categoryIndex, 0, Math.Max(0, _componentLibraryCategories.Count - 1));
        OpenComponentLibraryCurrentCategory();
    }

    private void OnComponentLibraryPrevComponentClick(object? sender, RoutedEventArgs e)
    {
        if (_componentLibraryActiveComponents.Count <= 1)
        {
            return;
        }

        _componentLibraryComponentIndex = Math.Max(0, _componentLibraryComponentIndex - 1);
        ApplyComponentLibraryComponentOffset();
    }

    private void OnComponentLibraryNextComponentClick(object? sender, RoutedEventArgs e)
    {
        var maxIndex = Math.Max(0, _componentLibraryActiveComponents.Count - 1);
        if (maxIndex <= 0)
        {
            return;
        }

        _componentLibraryComponentIndex = Math.Min(maxIndex, _componentLibraryComponentIndex + 1);
        ApplyComponentLibraryComponentOffset();
    }

    private void OnComponentLibraryCategoryViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            _componentLibraryCategories.Count == 0 ||
            ComponentLibraryCategoryViewport is null ||
            _componentLibraryCategoryHostTransform is null ||
            !e.GetCurrentPoint(ComponentLibraryCategoryViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isComponentLibraryCategoryGestureActive = true;
        _componentLibraryCategoryGestureStartPoint = e.GetPosition(ComponentLibraryCategoryViewport);
        _componentLibraryCategoryGestureCurrentPoint = _componentLibraryCategoryGestureStartPoint;
        _componentLibraryCategoryGestureBaseOffset = -_componentLibraryCategoryIndex * _componentLibraryCategoryPageWidth;
        e.Pointer.Capture(ComponentLibraryCategoryViewport);
    }

    private void OnComponentLibraryCategoryViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isComponentLibraryCategoryGestureActive ||
            ComponentLibraryCategoryViewport is null ||
            _componentLibraryCategoryHostTransform is null)
        {
            return;
        }

        _componentLibraryCategoryGestureCurrentPoint = e.GetPosition(ComponentLibraryCategoryViewport);
        var deltaX = _componentLibraryCategoryGestureCurrentPoint.X - _componentLibraryCategoryGestureStartPoint.X;
        var minOffset = -Math.Max(0, _componentLibraryCategories.Count - 1) * _componentLibraryCategoryPageWidth;
        var tentative = _componentLibraryCategoryGestureBaseOffset + deltaX;
        _componentLibraryCategoryHostTransform.X = Math.Clamp(tentative, minOffset, 0);
    }

    private void OnComponentLibraryCategoryViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isComponentLibraryCategoryGestureActive ||
            ComponentLibraryCategoryViewport is null)
        {
            return;
        }

        _isComponentLibraryCategoryGestureActive = false;
        e.Pointer.Capture(null);

        var endPoint = e.GetPosition(ComponentLibraryCategoryViewport);
        var deltaX = endPoint.X - _componentLibraryCategoryGestureStartPoint.X;
        var deltaY = endPoint.Y - _componentLibraryCategoryGestureStartPoint.Y;

        var tapThreshold = 6;
        if (Math.Abs(deltaX) <= tapThreshold && Math.Abs(deltaY) <= tapThreshold)
        {
            OpenComponentLibraryCurrentCategory();
            return;
        }

        var swipeThreshold = Math.Max(40, _componentLibraryCategoryPageWidth * 0.18);
        if (deltaX <= -swipeThreshold)
        {
            _componentLibraryCategoryIndex = Math.Min(_componentLibraryCategoryIndex + 1, Math.Max(0, _componentLibraryCategories.Count - 1));
        }
        else if (deltaX >= swipeThreshold)
        {
            _componentLibraryCategoryIndex = Math.Max(_componentLibraryCategoryIndex - 1, 0);
        }

        _componentLibraryActiveCategoryId = _componentLibraryCategories.Count > 0
            ? _componentLibraryCategories[_componentLibraryCategoryIndex].Id
            : null;

        ApplyComponentLibraryCategoryOffset();
    }

    private void OnComponentLibraryCategoryViewportPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isComponentLibraryCategoryGestureActive)
        {
            return;
        }

        _isComponentLibraryCategoryGestureActive = false;
        ApplyComponentLibraryCategoryOffset();
    }

    private void OnComponentLibraryComponentViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            _componentLibraryActiveComponents.Count <= 1 ||
            ComponentLibraryComponentViewport is null ||
            _componentLibraryComponentHostTransform is null ||
            !e.GetCurrentPoint(ComponentLibraryComponentViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isComponentLibraryComponentGestureActive = true;
        _componentLibraryComponentGestureStartPoint = e.GetPosition(ComponentLibraryComponentViewport);
        _componentLibraryComponentGestureCurrentPoint = _componentLibraryComponentGestureStartPoint;
        _componentLibraryComponentGestureBaseOffset = -_componentLibraryComponentIndex * _componentLibraryComponentPageWidth;
        e.Pointer.Capture(ComponentLibraryComponentViewport);
    }

    private void OnComponentLibraryComponentViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isComponentLibraryComponentGestureActive ||
            ComponentLibraryComponentViewport is null ||
            _componentLibraryComponentHostTransform is null)
        {
            return;
        }

        _componentLibraryComponentGestureCurrentPoint = e.GetPosition(ComponentLibraryComponentViewport);
        var deltaX = _componentLibraryComponentGestureCurrentPoint.X - _componentLibraryComponentGestureStartPoint.X;
        var minOffset = -Math.Max(0, _componentLibraryActiveComponents.Count - 1) * _componentLibraryComponentPageWidth;
        var tentative = _componentLibraryComponentGestureBaseOffset + deltaX;
        _componentLibraryComponentHostTransform.X = Math.Clamp(tentative, minOffset, 0);
    }

    private void SaveComponentLibraryWindowPosition()
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        var margin = ComponentLibraryWindow.Margin;
        _savedComponentLibraryMargin = margin;
        _isComponentLibraryWindowPositionCustomized = true;
        SyncComponentLibraryCollapseExpandedState();
    }

    private void RestoreComponentLibraryWindowPosition()
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        ComponentLibraryWindow.Margin = _savedComponentLibraryMargin;
        SyncComponentLibraryCollapseExpandedState();
    }

    private Thickness _savedComponentLibraryMargin = new Thickness(24, 24, 24, 100);

    private void OnComponentLibraryComponentViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isComponentLibraryComponentGestureActive ||
            ComponentLibraryComponentViewport is null)
        {
            return;
        }

        _isComponentLibraryComponentGestureActive = false;
        e.Pointer.Capture(null);

        var endPoint = e.GetPosition(ComponentLibraryComponentViewport);
        var deltaX = endPoint.X - _componentLibraryComponentGestureStartPoint.X;

        var swipeThreshold = Math.Max(40, _componentLibraryComponentPageWidth * 0.18);
        if (deltaX <= -swipeThreshold)
        {
            _componentLibraryComponentIndex = Math.Min(_componentLibraryComponentIndex + 1, Math.Max(0, _componentLibraryActiveComponents.Count - 1));
        }
        else if (deltaX >= swipeThreshold)
        {
            _componentLibraryComponentIndex = Math.Max(_componentLibraryComponentIndex - 1, 0);
        }

        ApplyComponentLibraryComponentOffset();
    }

    private void OnComponentLibraryComponentViewportPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isComponentLibraryComponentGestureActive)
        {
            return;
        }

        _isComponentLibraryComponentGestureActive = false;
        ApplyComponentLibraryComponentOffset();
    }

    internal void SaveAllWhiteboardNotes()
    {
        foreach (var pageGrid in _desktopPageComponentGrids.Values)
        {
            foreach (var host in pageGrid.Children.OfType<Border>())
            {
                var contentHost = TryGetContentHost(host);
                if (contentHost?.Child is WhiteboardWidget whiteboard)
                {
                    whiteboard.ForceSaveNote();
                }
                else if (contentHost?.Child is StickyNoteWidget stickyNote)
                {
                    stickyNote.ForceSave();
                }
            }
        }
    }
}
