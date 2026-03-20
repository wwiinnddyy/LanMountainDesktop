using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

namespace LanMountainDesktop.Views.Components;

public partial class DailyWord2x2Widget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();

    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 2;
    private const int BaseHeightCells = 2;
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    private LanMountainDesktop.PluginSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private DailyWordSnapshot? _latestSnapshot;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private bool _isNightVisual = true;
    private bool _isMeaningVisible;

    public DailyWord2x2Widget()
    {
        InitializeComponent();

        WordTextBlock.FontFamily = MiSansFontFamily;
        MeaningTextBlock.FontFamily = MiSansFontFamily;
        HiddenHintTextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

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
        _currentCellSize = Math.Max(1, cellSize);
        UpdateAdaptiveLayout();
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
        _recommendationService = recommendationInfoService ?? DefaultRecommendationService;
        if (_isAttached)
        {
            _ = RefreshWordAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        _recommendationService.ClearCache();
        ApplyAutoRefreshSettings();
        if (_isAttached)
        {
            _ = RefreshWordAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRefreshSettings();
        UpdateRefreshButtonState();
        _ = RefreshWordAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        UpdateRefreshButtonState();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _isNightVisual = ResolveNightMode();
        ApplyNightModeVisual();
    }

    private bool ResolveNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush brush)
        {
            return CalculateRelativeLuminance(brush.Color) < 0.45;
        }

        return true;
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
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
        if (_isRefreshing)
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

    private async Task RefreshWordAsync(bool forceRefresh)
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateRefreshButtonState();
        UpdateLanguageCode();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var query = new DailyWordQuery(
                Locale: _languageCode,
                ForceRefresh: forceRefresh);
            var result = await _recommendationService.GetDailyWordAsync(query, cts.Token);
            if (!_isAttached || cts.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyFailedState();
                return;
            }

            ApplySnapshot(result.Data);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled requests.
        }
        catch
        {
            if (_isAttached && !cts.IsCancellationRequested)
            {
                ApplyFailedState();
            }
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                _refreshCts = null;
            }

            cts.Dispose();
            _isRefreshing = false;
            UpdateRefreshButtonState();
        }
    }

    private void ApplySnapshot(DailyWordSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        WordTextBlock.Text = NormalizeCompactText(snapshot.Word);
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

        var unifiedMainRectangle = ResolveUnifiedMainRectangle();
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

        WordTextBlock.FontSize = FitFontSize(
            WordTextBlock.Text,
            wordWidth,
            wordHeightBudget,
            maxLines: 1,
            minFontSize: Math.Clamp(18 * scale, 12, 22),
            maxFontSize: Math.Clamp(38 * scale, 20, 50),
            weight: FontWeight.Bold,
            lineHeightFactor: 1.02);
        WordTextBlock.LineHeight = WordTextBlock.FontSize * 1.02;

        var detailFont = FitFontSize(
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
        RefreshButton.IsEnabled = !_isRefreshing;
        RefreshButton.Opacity = _isRefreshing ? 0.60 : 1.0;
        RefreshIcon.Opacity = _isRefreshing ? 0.60 : 1.0;
    }

    private void UpdateLanguageCode()
    {
        try
        {
            var snapshot = _appSettingsService.Load();
            _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
        }
    }

    private void ApplyAutoRefreshSettings()
    {
        var enabled = true;
        var intervalMinutes = 360;

        try
        {
            var snapshot = _componentSettingsService.Load();
            enabled = snapshot.DailyWordAutoRefreshEnabled;
            intervalMinutes = NormalizeAutoRefreshIntervalMinutes(snapshot.DailyWordAutoRefreshIntervalMinutes);
        }
        catch
        {
            // Keep fallback defaults.
        }

        _autoRefreshEnabled = enabled;
        _refreshTimer.Interval = TimeSpan.FromMinutes(intervalMinutes);

        if (!_isAttached)
        {
            return;
        }

        if (_autoRefreshEnabled)
        {
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }
        else if (_refreshTimer.IsEnabled)
        {
            _refreshTimer.Stop();
        }
    }

    private static int NormalizeAutoRefreshIntervalMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return 360;
        }

        if (SupportedAutoRefreshIntervalsMinutes.Contains(minutes))
        {
            return minutes;
        }

        return SupportedAutoRefreshIntervalsMinutes
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(360);
    }

    private void CancelRefreshRequest()
    {
        var cts = Interlocked.Exchange(ref _refreshCts, null);
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / BaseCellSize, 0.56, 2.0);
        var widthScale = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * BaseWidthCells), 0.56, 2.0)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * BaseHeightCells), 0.56, 2.0)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.56, 2.0);
    }

    private CornerRadius ResolveUnifiedMainRectangle() => new(ResolveUnifiedMainRadiusValue());

    private static double ResolveUnifiedMainRadiusValue() =>
        HostAppearanceThemeProvider.GetOrCreate().GetCurrent().CornerRadiusTokens.Lg.TopLeft;

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private static string BuildMeaningPreview(string? rawMeaning)
    {
        var normalized = NormalizeCompactText(rawMeaning);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Meaning unavailable";
        }

        var compact = normalized.Replace("；", "; ", StringComparison.Ordinal);
        return compact.Length <= 160 ? compact : $"{compact[..160]}...";
    }

    private static string NormalizeCompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(text.Trim(), " ");
    }

    private static double FitFontSize(
        string? text,
        double maxWidth,
        double maxHeight,
        int maxLines,
        double minFontSize,
        double maxFontSize,
        FontWeight weight,
        double lineHeightFactor)
    {
        var content = string.IsNullOrWhiteSpace(text) ? " " : text.Trim();
        var min = Math.Max(6, minFontSize);
        var max = Math.Max(min, maxFontSize);
        var low = min;
        var high = max;
        var best = min;

        for (var i = 0; i < 18; i++)
        {
            var candidate = (low + high) / 2d;
            var lineHeight = candidate * lineHeightFactor;
            var size = MeasureTextSize(content, candidate, weight, Math.Max(1, maxWidth), lineHeight);
            var lineCount = Math.Max(1, (int)Math.Ceiling(size.Height / Math.Max(1, lineHeight)));
            var fits = size.Height <= maxHeight + 0.6 && lineCount <= Math.Max(1, maxLines);

            if (fits)
            {
                best = candidate;
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }

        return best;
    }

    private static Size MeasureTextSize(string text, double fontSize, FontWeight weight, double maxWidth, double lineHeight)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontFamily = MiSansFontFamily,
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = lineHeight
        };

        probe.Measure(new Size(Math.Max(1, maxWidth), double.PositiveInfinity));
        return probe.DesiredSize;
    }
}
