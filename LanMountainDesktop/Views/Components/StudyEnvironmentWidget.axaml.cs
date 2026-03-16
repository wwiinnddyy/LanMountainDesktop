using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class StudyEnvironmentWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IDisposable
{
    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private readonly StudyAnalyticsMonitoringLeaseCoordinator _monitoringLeaseCoordinator = StudyAnalyticsMonitoringLeaseCoordinatorFactory.CreateDefault();
    private LanMountainDesktop.PluginSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly DispatcherTimer _uiTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    private double _currentCellSize = 48;
    private bool _showDisplayDb = true;
    private bool _showDbfs;
    private string? _componentColorScheme;
    private string _languageCode = "zh-CN";
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isDisposed;
    private IDisposable? _monitoringLease;

    public StudyEnvironmentWidget()
    {
        InitializeComponent();

        _uiTimer.Tick += OnUiTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ReloadDisplaySettings();
        ApplyCellSize(_currentCellSize);
        RefreshVisual();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = Math.Clamp(_currentCellSize / 48d, 0.82, 2.2);

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.34, 10, 28));
        RootBorder.Padding = new Thickness(
            Math.Clamp(14 * scale, 8, 20),
            Math.Clamp(10 * scale, 6, 16));

        StatusTitleTextBlock.FontSize = Math.Clamp(11 * scale, 9, 18);
        StatusValueTextBlock.FontSize = Math.Clamp(20 * scale, 12, 34);
        NoiseValueTextBlock.FontSize = Math.Clamp(22 * scale, 12, 38);
        NoiseSubValueTextBlock.FontSize = Math.Clamp(12 * scale, 9, 18);
        UpdateAdaptiveLayout();
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        var wasOnActivePage = _isOnActivePage;
        _isOnActivePage = isOnActivePage;
        
        UpdateMonitoringLeaseState();
        
        if (isOnActivePage && !wasOnActivePage)
        {
            RefreshVisual();
        }
        
        UpdateTimerState();
    }

    public void RefreshFromSettings()
    {
        ReloadDisplaySettings();
        RefreshVisual();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadDisplaySettings();
        UpdateMonitoringLeaseState();
        UpdateTimerState();
        RefreshVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _monitoringLease?.Dispose();
        _monitoringLease = null;
        _uiTimer.Stop();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
        UpdateAdaptiveLayout();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        RefreshVisual();
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        RefreshVisual();
    }

    private void UpdateTimerState()
    {
        if (_isAttached && _isOnActivePage)
        {
            _uiTimer.Start();
            return;
        }

        _uiTimer.Stop();
    }

    private void UpdateMonitoringLeaseState()
    {
        if (_isAttached)
        {
            _monitoringLease ??= _monitoringLeaseCoordinator.AcquireLease();
            return;
        }

        _monitoringLease?.Dispose();
        _monitoringLease = null;
    }

    private void ReloadDisplaySettings()
    {
        var appSnapshot = _appSettingsService.Load();
        var componentSnapshot = _componentSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);
        _showDisplayDb = componentSnapshot.StudyEnvironmentShowDisplayDb;
        _showDbfs = componentSnapshot.StudyEnvironmentShowDbfs;
        _componentColorScheme = componentSnapshot.ColorSchemeSource;
        if (!_showDisplayDb && !_showDbfs)
        {
            _showDisplayDb = true;
        }
    }

    private void RefreshVisual()
    {
        var snapshot = _studyAnalyticsService.GetSnapshot();
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;

        StatusTitleTextBlock.Text = L("study.environment.status_label", "Environment");

        if (isSessionReport && snapshot.LastSessionReport is not null)
        {
            StatusValueTextBlock.Text = L("study.score_overview.mode.session", "Session");
            StatusValueTextBlock.Foreground = TryResolveThemeBrush("AdaptiveTextPrimaryBrush", "#FFEFF3FF");

            if (!StudySessionReportProjection.TryAggregate(snapshot.LastSessionReport, snapshot.Config, out var aggregate))
            {
                NoiseValueTextBlock.Text = L("study.environment.value.unavailable", "--");
                NoiseSubValueTextBlock.IsVisible = false;
                UpdateAdaptiveLayout();
                return;
            }

            var reportShowDisplay = _showDisplayDb;
            var reportShowDbfs = _showDbfs;
            if (!reportShowDisplay && !reportShowDbfs)
            {
                reportShowDisplay = true;
            }

            if (reportShowDisplay && reportShowDbfs)
            {
                NoiseValueTextBlock.Text = FormatDisplayDb(aggregate.AverageDisplayDb);
                NoiseSubValueTextBlock.Text = FormatDbfs(aggregate.AverageDbfs);
                NoiseSubValueTextBlock.IsVisible = true;
                return;
            }

            NoiseValueTextBlock.Text = reportShowDisplay
                ? FormatDisplayDb(aggregate.AverageDisplayDb)
                : FormatDbfs(aggregate.AverageDbfs);
            NoiseSubValueTextBlock.IsVisible = false;
            UpdateAdaptiveLayout();
            return;
        }

        StatusValueTextBlock.Text = ResolveStatusText(snapshot);
        StatusValueTextBlock.Foreground = ResolveStatusBrush(snapshot);

        if (snapshot.LatestRealtimePoint is not { } realtimePoint)
        {
            NoiseValueTextBlock.Text = L("study.environment.value.unavailable", "--");
            NoiseSubValueTextBlock.IsVisible = false;
            UpdateAdaptiveLayout();
            return;
        }

        var showDisplay = _showDisplayDb;
        var showDbfs = _showDbfs;
        if (!showDisplay && !showDbfs)
        {
            showDisplay = true;
        }

        if (showDisplay && showDbfs)
        {
            NoiseValueTextBlock.Text = FormatDisplayDb(realtimePoint.DisplayDb);
            NoiseSubValueTextBlock.Text = FormatDbfs(realtimePoint.Dbfs);
            NoiseSubValueTextBlock.IsVisible = true;
            return;
        }

        NoiseValueTextBlock.Text = showDisplay
            ? FormatDisplayDb(realtimePoint.DisplayDb)
            : FormatDbfs(realtimePoint.Dbfs);
        NoiseSubValueTextBlock.IsVisible = false;
        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = Math.Clamp(_currentCellSize / 48d, 0.82, 2.2);
        var width = Bounds.Width;
        var height = Bounds.Height;
        var showingDualNoiseLines = _showDisplayDb && _showDbfs;

        // Collapse the "Environment" label when space is tight so core values remain readable.
        var collapseByCell = _currentCellSize <= 40;
        var collapseByBounds =
            (width > 0 && width < (showingDualNoiseLines ? 230 : 200)) ||
            (height > 0 && height < (showingDualNoiseLines ? 102 : 82));
        var hideStatusLabel = collapseByCell || collapseByBounds;

        StatusTitleTextBlock.IsVisible = !hideStatusLabel;
        LeftStatusStack.Spacing = hideStatusLabel ? 0 : Math.Clamp(2 * scale, 1, 4);
        LayoutGrid.ColumnSpacing = hideStatusLabel
            ? Math.Clamp(6 * scale, 4, 10)
            : Math.Clamp(10 * scale, 7, 14);
    }

    private string ResolveStatusText(StudyAnalyticsSnapshot snapshot)
    {
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported)
        {
            return L("study.environment.status.unsupported", "Unsupported");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Error || snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return L("study.environment.status.error", "Error");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Paused)
        {
            return L("study.environment.status.paused", "Paused");
        }

        if (snapshot.StreamStatus == NoiseStreamStatus.Noisy)
        {
            return L("study.environment.status.noisy", "Noisy");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Running && snapshot.StreamStatus == NoiseStreamStatus.Quiet)
        {
            return L("study.environment.status.quiet", "Quiet");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Ready)
        {
            return L("study.environment.status.ready", "Ready");
        }

        return L("study.environment.status.initializing", "Initializing");
    }

    private IBrush ResolveStatusBrush(StudyAnalyticsSnapshot snapshot)
    {
        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported ||
            snapshot.State == StudyAnalyticsRuntimeState.Error ||
            snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return useMonetColor ? CreateBrush("#FF6FD7A2") : CreateBrush("#FFFF7B7B");
        }

        if (snapshot.StreamStatus == NoiseStreamStatus.Noisy)
        {
            return useMonetColor ? CreateBrush("#FF4FC3F7") : CreateBrush("#FFFFB14A");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Running &&
            snapshot.StreamStatus == NoiseStreamStatus.Quiet)
        {
            return useMonetColor ? CreateBrush("#FF81C784") : CreateBrush("#FF6FD7A2");
        }

        return TryResolveThemeBrush("AdaptiveTextPrimaryBrush", "#FFEFF3FF");
    }

    private string FormatDisplayDb(double value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            L("study.environment.value.display_format", "{0:F1} dB"),
            value);
    }

    private string FormatDbfs(double value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            L("study.environment.value.dbfs_format", "{0:F1} dBFS"),
            value);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private static SolidColorBrush CreateBrush(string hexColor)
    {
        return new SolidColorBrush(Color.Parse(hexColor));
    }

    private IBrush TryResolveThemeBrush(string resourceKey, string fallbackHex)
    {
        if (this.TryFindResource(resourceKey, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return CreateBrush(fallbackHex);
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
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        _monitoringLease?.Dispose();
        _monitoringLease = null;
    }
}
