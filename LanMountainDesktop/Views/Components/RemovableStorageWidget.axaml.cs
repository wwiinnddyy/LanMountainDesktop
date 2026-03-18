using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class RemovableStorageWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IComponentPlacementContextAware, IDisposable
{
    private readonly record struct RemovableStoragePalette(
        Color BackgroundFrom,
        Color BackgroundTo,
        Color Border,
        Color AccentOrb,
        Color AccentGlow,
        Color IconBadgeBackground,
        Color IconForeground,
        Color PrimaryText,
        Color SecondaryText,
        Color StatusText,
        Color Accent,
        Color OnAccent,
        Color SecondaryButtonBackground,
        Color SecondaryButtonBorder,
        Color SecondaryButtonForeground,
        Color DisabledButtonBackground,
        Color DisabledButtonBorder,
        Color DisabledButtonForeground);

    private readonly DispatcherTimer _pollTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private readonly IRemovableStorageService _removableStorageService = new RemovableStorageService();
    private readonly LocalizationService _localizationService = new();
    private ISettingsService _settingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsStore = HostComponentSettingsStoreProvider.GetOrCreate();

    private IReadOnlyList<RemovableStorageDrive> _connectedDrives = Array.Empty<RemovableStorageDrive>();
    private string _componentId = BuiltInComponentIds.DesktopRemovableStorage;
    private string _placementId = string.Empty;
    private string _languageCode = "zh-CN";
    private string? _componentColorScheme;
    private string _selectedDriveRootPath = string.Empty;
    private string? _statusOverrideText;
    private double _currentCellSize = 48;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isRefreshing;
    private bool _isDisposed;

    public RemovableStorageWidget()
    {
        InitializeComponent();

        _pollTimer.Tick += OnPollTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ApplyCellSize(_currentCellSize);
        ReloadSettings();
        ApplyVisualState();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        ApplyLayoutMetrics();
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        var shouldRefresh = !_isOnActivePage && isOnActivePage;
        _isOnActivePage = isOnActivePage;
        UpdatePollingState();

        if (shouldRefresh)
        {
            _ = RefreshDriveListAsync();
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopRemovableStorage
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        RefreshFromSettings();
    }

    public void RefreshFromSettings()
    {
        ReloadSettings();
        ApplyVisualState();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = sender;
        _ = e;
        _isAttached = true;
        UpdatePollingState();
        _ = RefreshDriveListAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = sender;
        _ = e;
        _isAttached = false;
        UpdatePollingState();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyLayoutMetrics();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyVisualState();
    }

    private async void OnPollTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        await RefreshDriveListAsync();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var drive = GetSelectedDrive();
        if (drive is null)
        {
            return;
        }

        if (_removableStorageService.OpenDrive(drive.RootPath))
        {
            _statusOverrideText = L("removable_storage.widget.ready", "Ready to open or eject.");
            ApplyVisualState();
            return;
        }

        _statusOverrideText = L("removable_storage.widget.open_failed", "Failed to open this drive.");
        ApplyVisualState();
        await RefreshDriveListAsync();
    }

    private async void OnEjectClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var drive = GetSelectedDrive();
        if (drive is null)
        {
            return;
        }

        _statusOverrideText = L("removable_storage.widget.ejecting", "Ejecting drive...");
        ApplyVisualState();

        var ejected = _removableStorageService.EjectDrive(drive.RootPath);
        _statusOverrideText = ejected
            ? L("removable_storage.widget.ejecting", "Ejecting drive...")
            : L("removable_storage.widget.eject_failed", "Could not eject this drive. Close any files on it and try again.");
        ApplyVisualState();
        await RefreshDriveListAsync();
    }

    private async Task RefreshDriveListAsync()
    {
        if (_isDisposed || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;

        try
        {
            var previousDriveRoots = new HashSet<string>(
                _connectedDrives.Select(drive => drive.RootPath),
                StringComparer.OrdinalIgnoreCase);

            var latestDrives = await Task.Run(() => _removableStorageService.GetConnectedDrives());
            if (_isDisposed)
            {
                return;
            }

            var newlyInsertedDrive = latestDrives.FirstOrDefault(drive => !previousDriveRoots.Contains(drive.RootPath));
            _connectedDrives = latestDrives;

            if (newlyInsertedDrive is not null)
            {
                _selectedDriveRootPath = newlyInsertedDrive.RootPath;
            }
            else if (string.IsNullOrWhiteSpace(_selectedDriveRootPath) ||
                     !_connectedDrives.Any(drive => string.Equals(drive.RootPath, _selectedDriveRootPath, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedDriveRootPath = _connectedDrives.FirstOrDefault().RootPath ?? string.Empty;
            }

            if (_connectedDrives.Count == 0)
            {
                _selectedDriveRootPath = string.Empty;
                _statusOverrideText = null;
            }
            else if (newlyInsertedDrive is not null)
            {
                _statusOverrideText = null;
            }

            ReloadSettings();
            ApplyVisualState();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RemovableStorageWidget", "Failed to refresh removable storage widget.", ex);
            _statusOverrideText = L("removable_storage.widget.refresh_failed", "Drive list refresh failed.");
            ApplyVisualState();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ReloadSettings()
    {
        try
        {
            var appSettings = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            var componentSettings = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
            _languageCode = _localizationService.NormalizeLanguageCode(appSettings.LanguageCode);
            _componentColorScheme = componentSettings.ColorSchemeSource;
        }
        catch
        {
            _languageCode = _localizationService.NormalizeLanguageCode(_languageCode);
        }
    }

    private void ApplyVisualState()
    {
        var drive = GetSelectedDrive();
        var hasDrive = drive is not null;
        var palette = ResolvePalette();

        RootBorder.Background = CreateGradientBrush(palette.BackgroundFrom, palette.BackgroundTo);
        RootBorder.BorderBrush = CreateBrush(palette.Border);
        AccentOrb.Background = CreateBrush(palette.AccentOrb);
        AccentGlow.Background = CreateBrush(palette.AccentGlow);
        IconBadge.Background = CreateBrush(palette.IconBadgeBackground);
        DriveIcon.Foreground = CreateBrush(palette.IconForeground);
        DriveNameTextBlock.Foreground = CreateBrush(palette.PrimaryText);
        DriveDetailTextBlock.Foreground = CreateBrush(palette.SecondaryText);
        StatusTextBlock.Foreground = CreateBrush(palette.StatusText);

        if (hasDrive)
        {
            ApplyButtonPalette(
                OpenButton,
                OpenButtonIcon,
                OpenButtonTextBlock,
                palette.Accent,
                palette.OnAccent,
                palette.Accent);
            ApplyButtonPalette(
                EjectButton,
                EjectButtonIcon,
                EjectButtonTextBlock,
                palette.SecondaryButtonBackground,
                palette.SecondaryButtonForeground,
                palette.SecondaryButtonBorder);
        }
        else
        {
            ApplyButtonPalette(
                OpenButton,
                OpenButtonIcon,
                OpenButtonTextBlock,
                palette.DisabledButtonBackground,
                palette.DisabledButtonForeground,
                palette.DisabledButtonBorder);
            ApplyButtonPalette(
                EjectButton,
                EjectButtonIcon,
                EjectButtonTextBlock,
                palette.DisabledButtonBackground,
                palette.DisabledButtonForeground,
                palette.DisabledButtonBorder);
        }

        OpenButton.IsEnabled = hasDrive;
        EjectButton.IsEnabled = hasDrive;

        OpenButtonTextBlock.Text = L("removable_storage.action.open", "Open");
        EjectButtonTextBlock.Text = L("removable_storage.action.eject", "Eject");

        if (hasDrive)
        {
            var selectedDrive = drive!;
            DriveNameTextBlock.Text = ResolveDriveName(selectedDrive);
            DriveDetailTextBlock.Text = selectedDrive.DriveLetter;
            StatusTextBlock.Text = _statusOverrideText ??
                                   L("removable_storage.widget.ready", "Ready to open or eject.");
        }
        else
        {
            DriveNameTextBlock.Text = L("removable_storage.widget.empty_title", "No device inserted");
            DriveDetailTextBlock.Text = L("removable_storage.widget.empty_subtitle", "Insert a USB drive to show it here.");
            StatusTextBlock.Text = L("removable_storage.widget.empty_hint", "Buttons stay disabled until a removable device is inserted.");
        }

        ApplyLayoutMetrics();
    }

    private void ApplyLayoutMetrics()
    {
        var scale = ResolveScale();
        var width = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 2;

        var cornerRadius = Math.Clamp(_currentCellSize * 0.44, 18, 34);
        RootBorder.CornerRadius = new CornerRadius(cornerRadius);
        RootBorder.Padding = new Thickness(
            Math.Clamp(16 * scale, 10, 24),
            Math.Clamp(15 * scale, 10, 22),
            Math.Clamp(16 * scale, 10, 24),
            Math.Clamp(15 * scale, 10, 22));

        LayoutGrid.RowSpacing = Math.Clamp(10 * scale, 8, 16);
        HeaderGrid.ColumnSpacing = Math.Clamp(12 * scale, 8, 16);
        HeaderTextStack.Spacing = Math.Clamp(2 * scale, 1, 4);

        var badgeSize = Math.Clamp(44 * scale, 38, 60);
        IconBadge.Width = badgeSize;
        IconBadge.Height = badgeSize;
        IconBadge.CornerRadius = new CornerRadius(badgeSize * 0.5);
        DriveIcon.FontSize = Math.Clamp(24 * scale, 20, 32);

        DriveNameTextBlock.FontSize = Math.Clamp(16 * scale, 13, 24);
        DriveDetailTextBlock.FontSize = Math.Clamp(11.5 * scale, 10, 16);
        StatusTextBlock.FontSize = Math.Clamp(12 * scale, 10, 17);
        StatusTextBlock.MaxWidth = Math.Max(96, width - (RootBorder.Padding.Left + RootBorder.Padding.Right));

        var buttonHeight = Math.Clamp(42 * scale, 38, 54);
        var buttonPadding = Math.Clamp(14 * scale, 10, 20);
        var buttonCornerRadius = Math.Clamp(buttonHeight * 0.5, 18, 999);

        OpenButton.Height = buttonHeight;
        OpenButton.Padding = new Thickness(buttonPadding, 0);
        OpenButton.CornerRadius = new CornerRadius(buttonCornerRadius);

        EjectButton.Height = buttonHeight;
        EjectButton.Padding = new Thickness(buttonPadding, 0);
        EjectButton.CornerRadius = new CornerRadius(buttonCornerRadius);

        OpenButtonIcon.FontSize = Math.Clamp(16 * scale, 14, 20);
        EjectButtonIcon.FontSize = Math.Clamp(16 * scale, 14, 20);
        OpenButtonTextBlock.FontSize = Math.Clamp(13 * scale, 11.5, 18);
        EjectButtonTextBlock.FontSize = Math.Clamp(13 * scale, 11.5, 18);

        AccentOrb.Width = Math.Clamp(width * 0.44, 96, 176);
        AccentOrb.Height = AccentOrb.Width;
        AccentOrb.CornerRadius = new CornerRadius(AccentOrb.Width * 0.5);
        AccentGlow.Height = Math.Clamp(76 * scale, 52, 110);
        AccentGlow.CornerRadius = new CornerRadius(AccentGlow.Height * 0.5);
    }

    private RemovableStorageDrive? GetSelectedDrive()
    {
        if (_connectedDrives.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_selectedDriveRootPath))
        {
            var selected = _connectedDrives.FirstOrDefault(drive =>
                string.Equals(drive.RootPath, _selectedDriveRootPath, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return _connectedDrives[0];
    }

    private string ResolveDriveName(RemovableStorageDrive drive)
    {
        return string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? L("removable_storage.widget.default_name", "Removable Drive")
            : drive.VolumeLabel.Trim();
    }

    private RemovableStoragePalette ResolvePalette()
    {
        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        if (!useMonetColor)
        {
            var nativeAccent = Color.Parse("#FF65A8FF");
            var nativeBackgroundFrom = Color.Parse("#FF10345F");
            var nativeBackgroundTo = Color.Parse("#FF0D213E");
            var nativePrimaryText = Color.Parse("#FFF4F8FF");
            var nativeSecondaryText = Color.Parse("#C8D9F5FF");
            var nativeDisabled = Color.Parse("#30465D7A");

            return new RemovableStoragePalette(
                nativeBackgroundFrom,
                nativeBackgroundTo,
                Color.Parse("#6A97D6FF"),
                Color.Parse("#2F8BC5FF"),
                Color.Parse("#4C79BFFF"),
                Color.Parse("#335BAAFF"),
                Color.Parse("#FFF5FAFF"),
                nativePrimaryText,
                nativeSecondaryText,
                Color.Parse("#D8E7FFFF"),
                nativeAccent,
                ColorMath.EnsureContrast(Color.Parse("#FF071420"), nativeAccent, 4.5),
                Color.Parse("#24FFFFFF"),
                Color.Parse("#5A9ACDFF"),
                nativePrimaryText,
                nativeDisabled,
                Color.Parse("#4D6782A0"),
                Color.Parse("#8FA8BDD1"));
        }

        var surfaceRaised = ResolveThemeColor("AdaptiveSurfaceRaisedBrush", "#FF1A2332");
        var surfaceOverlay = ResolveThemeColor("AdaptiveSurfaceOverlayBrush", "#FF111827");
        var accent = ResolveThemeColor("AdaptiveAccentBrush", "#FF61A8FF");
        var onAccent = ResolveThemeColor("AdaptiveOnAccentBrush", "#FFFFFFFF");
        var primaryText = ResolveThemeColor("AdaptiveTextPrimaryBrush", "#FFF8FAFC");
        var secondaryText = ResolveThemeColor("AdaptiveTextSecondaryBrush", "#FFD0D7E3");
        var mutedText = ResolveThemeColor("AdaptiveTextMutedBrush", "#FFAFB8C7");
        var disabledButtonBackground = ColorMath.WithAlpha(ColorMath.Blend(surfaceRaised, surfaceOverlay, 0.35), 0xD8);
        var disabledButtonBorder = ColorMath.WithAlpha(ColorMath.Blend(surfaceRaised, accent, 0.18), 0x88);
        var disabledButtonForeground = ColorMath.WithAlpha(primaryText, 0x88);

        var backgroundFrom = ColorMath.Blend(surfaceRaised, accent, 0.18);
        var backgroundTo = ColorMath.Blend(surfaceOverlay, surfaceRaised, 0.46);
        var border = ColorMath.WithAlpha(ColorMath.Blend(accent, surfaceRaised, 0.38), 0xB8);
        var iconBadgeBackground = ColorMath.Blend(surfaceRaised, accent, 0.28);
        var iconForeground = ColorMath.EnsureContrast(accent, iconBadgeBackground, 3.0);
        var secondaryButtonBackground = ColorMath.WithAlpha(ColorMath.Blend(surfaceRaised, accent, 0.10), 0xE6);
        var secondaryButtonBorder = ColorMath.WithAlpha(ColorMath.Blend(accent, surfaceRaised, 0.46), 0xC6);

        return new RemovableStoragePalette(
            backgroundFrom,
            backgroundTo,
            border,
            ColorMath.WithAlpha(accent, 0x28),
            ColorMath.WithAlpha(ColorMath.Blend(accent, backgroundFrom, 0.26), 0x74),
            iconBadgeBackground,
            iconForeground,
            primaryText,
            secondaryText,
            mutedText,
            accent,
            onAccent,
            secondaryButtonBackground,
            secondaryButtonBorder,
            primaryText,
            disabledButtonBackground,
            disabledButtonBorder,
            disabledButtonForeground);
    }

    private Color ResolveThemeColor(string resourceKey, string fallbackHex)
    {
        if (this.TryFindResource(resourceKey, out var resource))
        {
            if (resource is ISolidColorBrush solidBrush)
            {
                return solidBrush.Color;
            }

            if (resource is SolidColorBrush directSolidBrush)
            {
                return directSolidBrush.Color;
            }
        }

        return Color.Parse(fallbackHex);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.72, 2.2);
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 220d, 0.72, 2.4) : 1;
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 220d, 0.72, 2.4) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.72, 2.2);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private static void ApplyButtonPalette(
        Button button,
        FluentIcon icon,
        TextBlock textBlock,
        Color background,
        Color foreground,
        Color border)
    {
        button.Background = CreateBrush(background);
        button.BorderBrush = CreateBrush(border);
        button.BorderThickness = new Thickness(1);
        button.Foreground = CreateBrush(foreground);
        icon.Foreground = CreateBrush(foreground);
        textBlock.Foreground = CreateBrush(foreground);
    }

    private static IBrush CreateGradientBrush(Color from, Color to)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(from, 0),
                new GradientStop(to, 1)
            }
        };
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        return new(color);
    }

    private void UpdatePollingState()
    {
        if (_isAttached && _isOnActivePage)
        {
            if (!_pollTimer.IsEnabled)
            {
                _pollTimer.Start();
            }

            return;
        }

        _pollTimer.Stop();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pollTimer.Stop();
        _pollTimer.Tick -= OnPollTimerTick;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
    }
}
