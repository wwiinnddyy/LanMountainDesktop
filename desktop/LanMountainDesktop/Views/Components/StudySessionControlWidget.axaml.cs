using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using Material.Icons;

namespace LanMountainDesktop.Views.Components;

public partial class StudySessionControlWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IDisposable
{
    private static readonly Color[] PrimaryColorCandidates =
    {
        Color.Parse("#FFEAF5FF"),
        Color.Parse("#FFDDEEFF"),
        Color.Parse("#FFCEE3FA"),
        Color.Parse("#FF1B2E45"),
        Color.Parse("#FF233A54"),
        Color.Parse("#FFFFFFFF"),
        Color.Parse("#FF101C2A")
    };

    private static readonly Color[] SecondaryColorCandidates =
    {
        Color.Parse("#FFC7D9EC"),
        Color.Parse("#FFBAD0E8"),
        Color.Parse("#FFD9E8F6"),
        Color.Parse("#FF2F4763"),
        Color.Parse("#FF385673"),
        Color.Parse("#FFEAF3FA"),
        Color.Parse("#FF1A2C40")
    };

    private static readonly Color[] WarningColorCandidates =
    {
        Color.Parse("#FFFFD4D4"),
        Color.Parse("#FFFEE2E2"),
        Color.Parse("#FF7F1D1D"),
        Color.Parse("#FF991B1B"),
        Color.Parse("#FFFFFFFF"),
        Color.Parse("#FF111827")
    };

    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private readonly StudyAnalyticsMonitoringLeaseCoordinator _monitoringLeaseCoordinator = StudyAnalyticsMonitoringLeaseCoordinatorFactory.CreateDefault();
    private LanMountainDesktop.AirAppSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();
    private readonly StudySnapshotRenderGate _renderGate;
    private readonly DispatcherTimer _uiTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isSubscribed;
    private bool _isDisposed;
    private bool _isCompactMode;
    private bool _isUltraCompactMode;
    private bool _studyEnabled = true;
    private IDisposable? _monitoringLease;
    private string? _transientMessage;
    private DateTimeOffset _transientMessageExpireAt;

    public StudySessionControlWidget()
    {
        InitializeComponent();

        _renderGate = new StudySnapshotRenderGate(CanRenderSnapshot, ApplySnapshot);
        _uiTimer.Tick += OnUiTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        ApplySnapshot(_studyAnalyticsService.GetSnapshot());
    }

    public void ApplyCellSize(double cellSize)
    {
        ComponentDesignMetrics.ApplyCellSize(
            ref _currentCellSize, cellSize, UpdateAdaptiveLayout);
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        var wasOnActivePage = _isOnActivePage;
        _isOnActivePage = isOnActivePage;
        UpdateMonitoringLeaseState();
        UpdateTimerState();
        if (isOnActivePage && !wasOnActivePage)
        {
            RefreshVisual();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadLanguageCode();
        StudySnapshotSubscription.Subscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);

        UpdateMonitoringLeaseState();
        UpdateTimerState();
        RefreshVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StudyMonitoringLease.Release(ref _monitoringLease);
        _uiTimer.Stop();
        _renderGate.Clear();

        StudySnapshotSubscription.Unsubscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        StudyComponentLifecycle.RefreshOnResize(RootBorder, UpdateAdaptiveLayout, ApplyTypographyByBackground);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        RefreshVisual();
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        RefreshVisual();
    }

    private bool CanRenderSnapshot()
    {
        return _isAttached && _isOnActivePage;
    }

    private void UpdateTimerState()
    {
        if (_isAttached && _isOnActivePage)
        {
            if (!_uiTimer.IsEnabled)
            {
                _uiTimer.Start();
            }

            return;
        }

        _uiTimer.Stop();
    }

    private void UpdateMonitoringLeaseState() =>
        StudyMonitoringLease.Sync(ref _monitoringLease, _monitoringLeaseCoordinator, _studyEnabled, _isAttached, _isOnActivePage);

    private void OnActionButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var snapshot = _studyAnalyticsService.GetSnapshot();
        var isRunning = snapshot.Session.State == StudySessionRuntimeState.Running;

        var success = isRunning
            ? _studyAnalyticsService.StopStudySession()
            : _studyAnalyticsService.StartStudySession();

        if (!success)
        {
            _transientMessage = isRunning
                ? L("study.session_control.stop_failed", "Unable to stop session")
                : L("study.session_control.start_failed", "Unable to start session");
            _transientMessageExpireAt = DateTimeOffset.UtcNow.AddSeconds(2.2);
        }
        else
        {
            _transientMessage = null;
        }

        RefreshVisual();
    }

    private void RefreshVisual()
    {
        _renderGate.Queue(_studyAnalyticsService.GetSnapshot());
    }

    private void ApplySnapshot(StudyAnalyticsSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);

        if (!_studyEnabled)
        {
            PrimaryTextBlock.Text = L("study.widget.disabled_title", "自习功能未启用");
            SecondaryTextBlock.Text = L("study.widget.disabled_hint", "请在设置中开启");
            ActionIcon.Kind = MaterialIconKind.Settings;
            ApplyActionBadgeStyle(panelColor, StudyPanelPalette.DisabledBadge);
            return;
        }

        if (_transientMessage is not null && now > _transientMessageExpireAt)
        {
            _transientMessage = null;
        }

        var isRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        if (isRunning)
        {
            PrimaryTextBlock.Text = L("study.session_control.action.stop", "Stop Study Session");
            SecondaryTextBlock.Text = _transientMessage ?? string.Format(
                L("study.session_control.running_elapsed_format", "Elapsed {0}"),
                FormatElapsed(snapshot.Session.Elapsed));
            ActionIcon.Kind = MaterialIconKind.Stop;
            ApplyActionBadgeStyle(panelColor, Color.Parse("#FFF97373"));
            ApplyTransientWarningTintIfNeeded(panelColor);
            return;
        }

        PrimaryTextBlock.Text = L("study.session_control.action.start", "Start Study Session");
        SecondaryTextBlock.Text = _transientMessage ?? ResolveIdleHint(snapshot);
        ActionIcon.Kind = MaterialIconKind.Play;
        ApplyActionBadgeStyle(panelColor, Color.Parse("#FF60A5FA"));
        ApplyTransientWarningTintIfNeeded(panelColor);
    }

    private string ResolveIdleHint(StudyAnalyticsSnapshot snapshot)
    {
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported)
        {
            return L("study.environment.status.unsupported", "Unsupported");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Error || snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return L("study.environment.status.error", "Error");
        }

        if (snapshot.Session.State == StudySessionRuntimeState.Completed && snapshot.LastSessionReport is not null)
        {
            return string.Format(
                L("study.session_control.last_session_format", "Last {0}"),
                FormatElapsed(snapshot.LastSessionReport.Duration));
        }

        return L("study.session_control.idle_hint", "Tap the right button to start");
    }

    private void UpdateAdaptiveLayout()
    {
        var cellScale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.78, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 280d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 140d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.56, 2.2);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.05), 0.56, 2.2);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 220) || (Bounds.Height > 1 && Bounds.Height < 92);
        _isUltraCompactMode = scale < 0.74 || (Bounds.Width > 1 && Bounds.Width < 180) || (Bounds.Height > 1 && Bounds.Height < 76);

        var compactMultiplier = _isUltraCompactMode ? 0.78 : _isCompactMode ? 0.90 : 1.0;
        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.Padding = new Thickness(
            Math.Clamp(14 * scale * compactMultiplier, 7, 22),
            Math.Clamp(10 * scale * compactMultiplier, 5, 16));

        LayoutGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(6 * scale, 3, 8)
            : _isCompactMode
                ? Math.Clamp(8 * scale, 4, 10)
                : Math.Clamp(10 * scale, 6, 14);

        PrimaryTextBlock.FontSize = Math.Clamp(17 * scale, 10, 30);
        SecondaryTextBlock.FontSize = Math.Clamp(11 * scale, 8, 18);
        LeftTextStack.Spacing = _isUltraCompactMode ? 0 : Math.Clamp(2 * scale, 1, 4);

        var buttonSize = Math.Clamp(48 * scale * compactMultiplier, 28, 72);
        ActionButton.Width = buttonSize;
        ActionButton.Height = buttonSize;
        ActionIconBorder.Width = buttonSize;
        ActionIconBorder.Height = buttonSize;
        ActionIconBorder.CornerRadius = new CornerRadius(buttonSize / 2d);
        ActionIcon.Width = Math.Clamp(buttonSize * 0.44, 14, 30);
        ActionIcon.Height = Math.Clamp(buttonSize * 0.44, 14, 30);

        SecondaryTextBlock.IsVisible = !_isUltraCompactMode;
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = StudyPanelPalette.BuildSamples(panelColor);
        var primary = AdaptiveBrushFactory.Create(samples, PrimaryColorCandidates, minContrast: 4.5);
        var secondary = AdaptiveBrushFactory.Create(samples, SecondaryColorCandidates, minContrast: 4.5);

        PrimaryTextBlock.Foreground = primary;
        SecondaryTextBlock.Foreground = secondary;
    }

    private void ApplyTransientWarningTintIfNeeded(Color panelColor)
    {
        if (string.IsNullOrWhiteSpace(_transientMessage))
        {
            return;
        }

        var samples = StudyPanelPalette.BuildSamples(panelColor);
        SecondaryTextBlock.Foreground = AdaptiveBrushFactory.Create(samples, WarningColorCandidates, minContrast: 4.5);
    }

    private void ApplyActionBadgeStyle(Color panelColor, Color baseColor)
    {
        var badgeColor = StudyPanelPalette.ResolveBadgeColor(panelColor, baseColor);
    ActionIconBorder.Background = new SolidColorBrush(badgeColor);
    ActionIconBorder.BorderBrush = StudyPanelPalette.BadgeBorderBrush;
    ActionIcon.Foreground = StudyPanelPalette.ResolveBadgeForeground(panelColor, badgeColor, PrimaryColorCandidates);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return elapsed.ToString(@"hh\:mm\:ss");
        }

        return elapsed.ToString(@"mm\:ss");
    }

    private void ReloadLanguageCode()
    {
        StudyComponentSettings.Reload(
            ref _languageCode, ref _studyEnabled, _settingsService, _localizationService);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _uiTimer.Stop();
        _uiTimer.Tick -= OnUiTimerTick;
        _renderGate.Dispose();
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        StudySnapshotSubscription.Unsubscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);
    }
}
