using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Helpers;
using LanMountainDesktop.Shared.Threading;

namespace LanMountainDesktop.Views.Components;

public partial class DailyWord2x2Widget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget, ISettingsAwareComponentWidget
{
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private const int BaseWidthCells = 2;
    private const int BaseHeightCells = 2;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    private LanMountainDesktop.AirAppSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private DailyWordSnapshot? _latestSnapshot;
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private bool _isAttached;
    private readonly ComponentFeedRefresh _feed = new();
    private bool _isNightVisual = true;
    private bool _isMeaningVisible;

    public DailyWord2x2Widget()
    {
        InitializeComponent();

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ApplyCellSize(_currentCellSize);
        UpdateLanguageCode();
        ApplyAutoRefreshSettings();
        ApplyLoadingState();
        UpdateRefreshButtonState();
    }

    public void ApplyCellSize(double cellSize)
    {
        ComponentDesignMetrics.ApplyCellSize(
            ref _currentCellSize, cellSize, UpdateAdaptiveLayout);
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
        RecommendationServiceBinding.Attach(
            ref _recommendationService,
            recommendationInfoService,
            DefaultRecommendationService,
            () => _isAttached,
            () => RefreshWordAsync(forceRefresh: false));
    }

    public void RefreshFromSettings()
    {
        RecommendationServiceBinding.AfterSettingsChange(
            _recommendationService.ClearCache,
            ApplyAutoRefreshSettings,
            () => _isAttached,
            () => RefreshWordAsync(forceRefresh: true));
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ComponentRefreshLifetime.Attach(
            ref _isAttached, ApplyAutoRefreshSettings, UpdateRefreshButtonState,
            () => RefreshWordAsync(forceRefresh: false));
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ComponentRefreshLifetime.Detach(ref _isAttached, _refreshTimer, ref _feed.InFlight, UpdateRefreshButtonState);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ComponentThemeMode.RefreshNightVisual(this, ref _isNightVisual, ApplyNightModeVisual, fallbackToNightWhenSurfaceUnknown: true);
    }

    private void ApplyNightModeVisual()
    {
        CardBorder.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#1B2129") : Color.Parse("#FCFBFA"));

        WordTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#2B2F35"));
        MeaningTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5A6069"));
        HiddenHintTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#8A9099"));

        RefreshButton.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#EEF1F4"));
        RefreshIcon.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5E6671"));

        StatusTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#6A6F77"));
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_feed.IsBusy)
        {
            return;
        }

        await RefreshWordAsync(forceRefresh: true);
        e.Handled = true;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshWordAsync(forceRefresh: false);
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_latestSnapshot is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual sourceVisual)
        {
            for (Visual? current = sourceVisual; current is not null; current = current.GetVisualParent())
            {
                if (ReferenceEquals(current, RefreshButton))
                {
                    return;
                }
            }
        }

        _isMeaningVisible = !_isMeaningVisible;
        UpdateRevealState();
        UpdateAdaptiveLayout();
        e.Handled = true;
    }

    private Task RefreshWordAsync(bool forceRefresh) =>
        _feed.RunAsync(
            () => _isAttached,
            () => DailyWordFeed.BeginRefresh(UpdateRefreshButtonState, UpdateLanguageCode),
            async token =>
            {
                var snapshot = await DailyWordFeed.RequestAsync(_recommendationService, _languageCode, forceRefresh, token);
                if (snapshot is null)
                {
                    return false;
                }

                ApplySnapshot(snapshot);
                return true;
            },
            ApplyFailedState,
            UpdateRefreshButtonState);

    private void ApplySnapshot(DailyWordSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        WordTextBlock.Text = CompactText.Normalize(snapshot.Word);
        MeaningTextBlock.Text = BuildMeaningPreview(snapshot.Meaning);
        HiddenHintTextBlock.Text = L("dailyword2x2.widget.tap_to_show", "Tap to reveal meaning");
        StatusTextBlock.IsVisible = false;

        UpdateRevealState();
        UpdateAdaptiveLayout();
    }

    private void ApplyLoadingState()
    {
        _latestSnapshot = null;
        _isMeaningVisible = false;
        WordTextBlock.Text = L("dailyword.widget.loading_word", "daily word");
        MeaningTextBlock.Text = L("dailyword.widget.loading_meaning", "Fetching meaning...");
        HiddenHintTextBlock.Text = L("dailyword.widget.loading", "Loading...");
        StatusTextBlock.Text = L("dailyword.widget.loading", "Loading...");
        StatusTextBlock.IsVisible = true;
        UpdateRevealState();
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        _latestSnapshot = null;
        _isMeaningVisible = false;
        WordTextBlock.Text = L("dailyword.widget.fallback_word", "daily word");
        MeaningTextBlock.Text = L("dailyword.widget.fallback_meaning", "Youdao dictionary is temporarily unavailable.");
        HiddenHintTextBlock.Text = L("dailyword.widget.fetch_failed", "Daily word fetch failed");
        StatusTextBlock.Text = L("dailyword.widget.fetch_failed", "Daily word fetch failed");
        StatusTextBlock.IsVisible = true;
        UpdateRevealState();
        UpdateAdaptiveLayout();
    }

    private void UpdateRevealState()
    {
        var canShowMeaning = _latestSnapshot is not null && !string.IsNullOrWhiteSpace(MeaningTextBlock.Text);
        var showMeaning = _isMeaningVisible && canShowMeaning;
        MeaningTextBlock.IsVisible = showMeaning;
        HiddenHintTextBlock.IsVisible = !showMeaning;

        if (!showMeaning && _latestSnapshot is not null)
        {
            HiddenHintTextBlock.Text = L("dailyword2x2.widget.tap_to_show", "Tap to reveal meaning");
        }
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var unifiedMainRectangle = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = unifiedMainRectangle;
        CardBorder.CornerRadius = unifiedMainRectangle;
        CardBorder.Padding = new Thickness(
            Math.Clamp(12 * scale, 8, 18),
            Math.Clamp(11 * scale, 7, 16),
            Math.Clamp(12 * scale, 8, 18),
            Math.Clamp(11 * scale, 7, 16));

        var refreshSize = Math.Clamp(30 * scale, 20, 38);
        RefreshButton.Width = refreshSize;
        RefreshButton.Height = refreshSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshSize / 2d);
        RefreshIcon.FontSize = Math.Clamp(14 * scale, 10, 20);

        var contentWidth = Math.Max(80, totalWidth - CardBorder.Padding.Left - CardBorder.Padding.Right);
        var wordWidth = Math.Max(48, contentWidth - refreshSize - Math.Clamp(6 * scale, 4, 10));
        WordTextBlock.MaxWidth = wordWidth;

        var contentHeight = Math.Max(52, totalHeight - CardBorder.Padding.Top - CardBorder.Padding.Bottom);
        var wordHeightBudget = Math.Max(18, contentHeight * 0.34);
        var detailHeightBudget = Math.Max(18, contentHeight - wordHeightBudget - Math.Clamp(8 * scale, 4, 14));

        WordTextBlock.FontSize = ComponentTypography.FitFontSize(
            WordTextBlock.Text,
            wordWidth,
            wordHeightBudget,
            maxLines: 1,
            minFontSize: Math.Clamp(18 * scale, 12, 22),
            maxFontSize: Math.Clamp(38 * scale, 20, 50),
            weight: FontWeight.Bold,
            lineHeightFactor: 1.02);
        WordTextBlock.LineHeight = WordTextBlock.FontSize * 1.02;

        var detailFont = ComponentTypography.FitFontSize(
            MeaningTextBlock.IsVisible ? MeaningTextBlock.Text : HiddenHintTextBlock.Text,
            contentWidth,
            detailHeightBudget,
            maxLines: MeaningTextBlock.IsVisible ? 5 : 4,
            minFontSize: Math.Clamp(12 * scale, 9, 14),
            maxFontSize: Math.Clamp(18 * scale, 12, 22),
            weight: FontWeight.SemiBold,
            lineHeightFactor: 1.10);

        MeaningTextBlock.MaxWidth = contentWidth;
        MeaningTextBlock.FontSize = detailFont;
        MeaningTextBlock.LineHeight = detailFont * 1.10;
        MeaningTextBlock.MaxLines = totalHeight < _currentCellSize * 1.8 ? 4 : 5;

        HiddenHintTextBlock.MaxWidth = contentWidth;
        HiddenHintTextBlock.FontSize = detailFont;
        HiddenHintTextBlock.LineHeight = detailFont * 1.10;
        HiddenHintTextBlock.MaxLines = totalHeight < _currentCellSize * 1.8 ? 3 : 4;

        StatusTextBlock.FontSize = Math.Clamp(14 * scale, 9, 18);
    }

    private void UpdateRefreshButtonState()
    {
        ComponentBusyVisual.ApplyToFeed(RefreshButton, RefreshIcon, _feed.IsBusy);
    }

    private void UpdateLanguageCode() =>
        _languageCode = _localizationService.ResolveLanguageCode(() => _appSettingsService.Load().LanguageCode);

    private void ApplyAutoRefreshSettings()
    {
        DailyWordAutoRefresh.Apply(_componentSettingsService, _refreshTimer, _isAttached);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.56, 2.0);
        var widthScale = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * BaseWidthCells), 0.56, 2.0)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * BaseHeightCells), 0.56, 2.0)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.56, 2.0);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private static string BuildMeaningPreview(string? rawMeaning)
    {
        var normalized = CompactText.Normalize(rawMeaning);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Meaning unavailable";
        }

        var compact = normalized.Replace("；", "; ", StringComparison.Ordinal);
        return compact.Length <= 160 ? compact : $"{compact[..160]}...";
    }

}
