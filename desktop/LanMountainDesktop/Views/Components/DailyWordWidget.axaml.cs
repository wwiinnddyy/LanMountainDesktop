using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Helpers;
using LanMountainDesktop.Shared.Threading;

namespace LanMountainDesktop.Views.Components;

public partial class DailyWordWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget, ISettingsAwareComponentWidget
{
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    private readonly bool _isDesignModePreview = Design.IsDesignMode;
    private LanMountainDesktop.AirAppSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private bool _isAttached;
    private readonly ComponentFeedRefresh _feed = new();
    private bool _isNightVisual = true;

    public DailyWordWidget()
    {
        InitializeComponent();

        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        if (_isDesignModePreview)
        {
            ApplyCellSize(_currentCellSize);
            ApplyDesignTimePreview();
            return;
        }

        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshButton.Click += OnRefreshButtonClick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

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
        _isAttached = true;
        ApplyAutoRefreshSettings();
        UpdateRefreshButtonState();
        _ = RefreshWordAsync(forceRefresh: false);
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

        WordTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#FF9D6C") : Color.Parse("#F07541"));
        PronunciationTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#6B7078"));
        MeaningTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#2B2F35"));
        ExampleTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#2B2F35"));
        ExampleTranslationTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#7A8088"));

        RefreshButton.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#14A0A6AF"));
        RefreshIcon.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#626870"));

        StatusTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#6A6F77"));
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

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
        WordTextBlock.Text = CompactText.Normalize(snapshot.Word);
        PronunciationTextBlock.Text = BuildPronunciationText(snapshot);
        MeaningTextBlock.Text = BuildMeaningText(snapshot.Meaning);
        ExampleTextBlock.Text = BuildExampleText(snapshot.ExampleSentence);
        ExampleTranslationTextBlock.Text = BuildExampleTranslation(snapshot.ExampleTranslation);

        StatusTextBlock.IsVisible = false;
        UpdateAdaptiveLayout();
    }

    private void ApplyLoadingState()
    {
        WordTextBlock.Text = L("dailyword.widget.loading_word", "daily word");
        PronunciationTextBlock.Text = L("dailyword.widget.loading_pronunciation", "Fetching pronunciation...");
        MeaningTextBlock.Text = L("dailyword.widget.loading_meaning", "Fetching meaning...");
        ExampleTextBlock.Text = L("dailyword.widget.loading_example", "Fetching example sentence...");
        ExampleTranslationTextBlock.Text = L("dailyword.widget.loading_example_translation", "Loading...");
        StatusTextBlock.Text = L("dailyword.widget.loading", "Loading...");
        StatusTextBlock.IsVisible = true;
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        WordTextBlock.Text = L("dailyword.widget.fallback_word", "daily word");
        PronunciationTextBlock.Text = L("dailyword.widget.fallback_pronunciation", "Pronunciation unavailable");
        MeaningTextBlock.Text = L("dailyword.widget.fallback_meaning", "Youdao dictionary is temporarily unavailable.");
        ExampleTextBlock.Text = L("dailyword.widget.fallback_example", "Tap the refresh button and try again.");
        ExampleTranslationTextBlock.Text = L("dailyword.widget.fallback_example_translation", "It will retry when network recovers.");
        StatusTextBlock.Text = L("dailyword.widget.fetch_failed", "Daily word fetch failed");
        StatusTextBlock.IsVisible = true;
        UpdateAdaptiveLayout();
    }

    private void ApplyDesignTimePreview()
    {
        ComponentThemeMode.RefreshNightVisual(this, ref _isNightVisual, ApplyNightModeVisual, fallbackToNightWhenSurfaceUnknown: true);

        WordTextBlock.Text = "serendipity";
        PronunciationTextBlock.Text = "UK /,seren'dipiti/ | US /,seren'dipiti/";
        MeaningTextBlock.Text = "n. finding something valuable by accident; a pleasant surprise.";
        ExampleTextBlock.Text = "The widget preview became useful by pure serendipity.";
        ExampleTranslationTextBlock.Text = "A mocked sample sentence shown only in design mode.";
        StatusTextBlock.Text = string.Empty;
        StatusTextBlock.IsVisible = false;

        RefreshButton.IsEnabled = false;
        RefreshButton.Opacity = 1.0;
        RefreshIcon.Opacity = 0.82;

        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var isFourByThree = false;
        if (Bounds.Width > 1 && Bounds.Height > 1)
        {
            var widthRatio = Bounds.Width / (_currentCellSize * BaseWidthCells);
            var heightRatio = Bounds.Height / (_currentCellSize * BaseHeightCells);
            isFourByThree = widthRatio >= 0.9 && heightRatio >= 1.35;
        }

        var containerRadius = ComponentChromeCornerRadiusHelper.ResolveLgRectangle();
        RootBorder.CornerRadius = containerRadius;
        RootBorder.Padding = new Thickness(0);

        CardBorder.CornerRadius = containerRadius;
        CardBorder.Padding = new Thickness(
            Math.Clamp(16 * scale, 8, 24),
            Math.Clamp(14 * scale, 7, 22),
            Math.Clamp(16 * scale, 8, 24),
            Math.Clamp(14 * scale, 7, 22));

        var refreshSize = Math.Clamp(38 * scale, 22, 48);
        RefreshButton.Width = refreshSize;
        RefreshButton.Height = refreshSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshSize / 2d);
        RefreshIcon.FontSize = Math.Clamp(19 * scale, 12, 26);

        HaloEllipse.Width = Math.Clamp(totalWidth * 0.52, 120, 340);
        HaloEllipse.Height = HaloEllipse.Width;
        AccentCorner.Width = Math.Clamp(totalWidth * 0.20, 66, 132);
        AccentCorner.Height = AccentCorner.Width;
        AccentCorner.CornerRadius = new CornerRadius(AccentCorner.Width / 2d);

        var horizontalPadding = RootBorder.Padding.Left + RootBorder.Padding.Right + CardBorder.Padding.Left + CardBorder.Padding.Right;
        var contentWidth = Math.Max(98, totalWidth - horizontalPadding);
        var wordWidth = Math.Max(70, contentWidth - refreshSize - Math.Clamp(8 * scale, 5, 14));
        WordTextBlock.MaxWidth = wordWidth;
        PronunciationTextBlock.MaxWidth = contentWidth;
        MeaningTextBlock.MaxWidth = contentWidth;
        ExampleTextBlock.MaxWidth = contentWidth;
        ExampleTranslationTextBlock.MaxWidth = contentWidth;

        var compactLayout = totalHeight < _currentCellSize * 1.72;
        MeaningTextBlock.MaxLines = compactLayout ? 1 : (isFourByThree ? 3 : 2);
        ExampleTextBlock.MaxLines = compactLayout ? 1 : (isFourByThree ? 4 : 2);
        ExampleTranslationTextBlock.IsVisible = !compactLayout || isFourByThree;
        ExampleTranslationTextBlock.MaxLines = isFourByThree ? 2 : 1;

        var contentHeight = Math.Max(52, totalHeight - RootBorder.Padding.Top - RootBorder.Padding.Bottom - CardBorder.Padding.Top - CardBorder.Padding.Bottom);
        var wordHeightBudget = Math.Max(18, contentHeight * 0.24);
        var pronunciationHeightBudget = Math.Max(14, contentHeight * 0.16);
        var meaningHeightBudget = Math.Max(16, contentHeight * (compactLayout ? 0.26 : (isFourByThree ? 0.35 : 0.30)));
        var exampleHeightBudget = Math.Max(16, contentHeight - wordHeightBudget - pronunciationHeightBudget - meaningHeightBudget - Math.Clamp(16 * scale, 8, 24));
        if (!ExampleTranslationTextBlock.IsVisible)
        {
            exampleHeightBudget += Math.Clamp(11 * scale, 5, 18);
        }

        var wordBase = Math.Clamp(56 * scale, 18, 72);
        WordTextBlock.FontSize = ComponentTypography.FitFontSize(
            WordTextBlock.Text,
            wordWidth,
            wordHeightBudget,
            maxLines: 1,
            minFontSize: Math.Max(14, wordBase * 0.56),
            maxFontSize: wordBase,
            weight: FontWeight.Bold,
            lineHeightFactor: 1.04);
        WordTextBlock.LineHeight = WordTextBlock.FontSize * 1.04;

        var pronunciationBase = Math.Clamp(27 * scale, 10, 36);
        PronunciationTextBlock.FontSize = ComponentTypography.FitFontSize(
            PronunciationTextBlock.Text,
            contentWidth,
            pronunciationHeightBudget,
            maxLines: 1,
            minFontSize: Math.Max(8.6, pronunciationBase * 0.62),
            maxFontSize: pronunciationBase,
            weight: FontWeight.SemiBold,
            lineHeightFactor: 1.08);
        PronunciationTextBlock.LineHeight = PronunciationTextBlock.FontSize * 1.08;

        var meaningBase = Math.Clamp(25 * scale, 10, 34);
        MeaningTextBlock.FontSize = ComponentTypography.FitFontSize(
            MeaningTextBlock.Text,
            contentWidth,
            meaningHeightBudget,
            maxLines: Math.Max(1, MeaningTextBlock.MaxLines),
            minFontSize: Math.Max(9.2, meaningBase * 0.60),
            maxFontSize: meaningBase,
            weight: FontWeight.SemiBold,
            lineHeightFactor: 1.10);
        MeaningTextBlock.LineHeight = MeaningTextBlock.FontSize * 1.10;

        var exampleBase = Math.Clamp(22 * scale, 9, 30);
        ExampleTextBlock.FontSize = ComponentTypography.FitFontSize(
            ExampleTextBlock.Text,
            contentWidth,
            exampleHeightBudget,
            maxLines: Math.Max(1, ExampleTextBlock.MaxLines),
            minFontSize: Math.Max(8.8, exampleBase * 0.58),
            maxFontSize: exampleBase,
            weight: FontWeight.Medium,
            lineHeightFactor: 1.08);
        ExampleTextBlock.LineHeight = ExampleTextBlock.FontSize * 1.08;

        var translationBase = Math.Clamp(20 * scale, 8, 28);
        ExampleTranslationTextBlock.FontSize = ComponentTypography.FitFontSize(
            ExampleTranslationTextBlock.Text,
            contentWidth,
            Math.Max(10, exampleHeightBudget * 0.44),
            maxLines: 1,
            minFontSize: Math.Max(7.8, translationBase * 0.62),
            maxFontSize: translationBase,
            weight: FontWeight.Medium,
            lineHeightFactor: 1.06);
        ExampleTranslationTextBlock.LineHeight = ExampleTranslationTextBlock.FontSize * 1.06;

        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 9, 24);
    }

    private void UpdateRefreshButtonState()
    {
        RefreshButton.IsEnabled = !_feed.IsBusy;
        RefreshButton.Opacity = _isAttached ? 1.0 : 0.85;
        RefreshIcon.Opacity = _feed.IsBusy ? 0.56 : 1.0;
    }

    private void UpdateLanguageCode() =>
        _languageCode = _localizationService.ResolveLanguageCode(() => _appSettingsService.Load().LanguageCode);

    private void ApplyAutoRefreshSettings()
    {
        DailyWordAutoRefresh.Apply(_componentSettingsService, _refreshTimer, _isAttached);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.56, 2.0);
        
        var widthCells = BaseWidthCells;
        var heightCells = BaseHeightCells;
        
        if (Bounds.Width > 1 && Bounds.Height > 1)
        {
            var widthRatio = Bounds.Width / (_currentCellSize * widthCells);
            var heightRatio = Bounds.Height / (_currentCellSize * heightCells);
            
            if (widthRatio >= 0.9 && heightRatio >= 1.35)
            {
                heightCells = 3;
            }
        }
        
        var widthScale = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * widthCells), 0.56, 2.0)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * heightCells), 0.56, 2.0)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.56, 2.0);
    }

    private string BuildPronunciationText(DailyWordSnapshot snapshot)
    {
        var uk = CompactText.Normalize(snapshot.UkPronunciation);
        var us = CompactText.Normalize(snapshot.UsPronunciation);
        var isZh = _localizationService.IsChineseLanguage(_languageCode);

        if (!string.IsNullOrWhiteSpace(uk) && !string.IsNullOrWhiteSpace(us))
        {
            return isZh
                ? $"英 /{uk}/ · 美 /{us}/"
                : $"UK /{uk}/ · US /{us}/";
        }

        if (!string.IsNullOrWhiteSpace(uk))
        {
            return isZh ? $"英 /{uk}/" : $"UK /{uk}/";
        }

        if (!string.IsNullOrWhiteSpace(us))
        {
            return isZh ? $"美 /{us}/" : $"US /{us}/";
        }

        return isZh ? "英/美 发音暂无" : "Pronunciation unavailable";
    }

    private static string BuildMeaningText(string? rawMeaning)
    {
        var normalized = CompactText.Normalize(rawMeaning);
        return string.IsNullOrWhiteSpace(normalized)
            ? "Meaning unavailable"
            : normalized;
    }

    private static string BuildExampleText(string? sentence)
    {
        var normalized = CompactText.Normalize(sentence);
        return string.IsNullOrWhiteSpace(normalized)
            ? "No example sentence."
            : normalized;
    }

    private static string BuildExampleTranslation(string? translation)
    {
        var normalized = CompactText.Normalize(translation);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized;
    }

}
