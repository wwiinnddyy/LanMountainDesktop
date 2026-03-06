using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class DailyWordWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();

    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;
    private static readonly int[] SupportedAutoRefreshIntervalsMinutes = [30, 60, 180, 360, 720, 1440];

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;

    public DailyWordWidget()
    {
        InitializeComponent();

        WordTextBlock.FontFamily = MiSansFontFamily;
        PronunciationTextBlock.FontFamily = MiSansFontFamily;
        MeaningTextBlock.FontFamily = MiSansFontFamily;
        ExampleTextBlock.FontFamily = MiSansFontFamily;
        ExampleTranslationTextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshButton.Click += OnRefreshButtonClick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

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
        WordTextBlock.Text = NormalizeCompactText(snapshot.Word);
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

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));
        RootBorder.Padding = new Thickness(0);

        CardBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));
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
        MeaningTextBlock.MaxLines = compactLayout ? 1 : 2;
        ExampleTextBlock.MaxLines = compactLayout ? 1 : 2;
        ExampleTranslationTextBlock.IsVisible = !compactLayout;
        ExampleTranslationTextBlock.MaxLines = 1;

        var contentHeight = Math.Max(52, totalHeight - RootBorder.Padding.Top - RootBorder.Padding.Bottom - CardBorder.Padding.Top - CardBorder.Padding.Bottom);
        var wordHeightBudget = Math.Max(18, contentHeight * 0.24);
        var pronunciationHeightBudget = Math.Max(14, contentHeight * 0.16);
        var meaningHeightBudget = Math.Max(16, contentHeight * (compactLayout ? 0.26 : 0.30));
        var exampleHeightBudget = Math.Max(16, contentHeight - wordHeightBudget - pronunciationHeightBudget - meaningHeightBudget - Math.Clamp(16 * scale, 8, 24));
        if (!ExampleTranslationTextBlock.IsVisible)
        {
            exampleHeightBudget += Math.Clamp(11 * scale, 5, 18);
        }

        var wordBase = Math.Clamp(56 * scale, 18, 72);
        WordTextBlock.FontSize = FitFontSize(
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
        PronunciationTextBlock.FontSize = FitFontSize(
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
        MeaningTextBlock.FontSize = FitFontSize(
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
        ExampleTextBlock.FontSize = FitFontSize(
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
        ExampleTranslationTextBlock.FontSize = FitFontSize(
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
        RefreshButton.IsEnabled = !_isRefreshing;
        RefreshButton.Opacity = _isAttached ? 1.0 : 0.85;
        RefreshIcon.Opacity = _isRefreshing ? 0.56 : 1.0;
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

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
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

    private string BuildPronunciationText(DailyWordSnapshot snapshot)
    {
        var uk = NormalizeCompactText(snapshot.UkPronunciation);
        var us = NormalizeCompactText(snapshot.UsPronunciation);
        var isZh = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);

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
        var normalized = NormalizeCompactText(rawMeaning);
        return string.IsNullOrWhiteSpace(normalized)
            ? "Meaning unavailable"
            : normalized;
    }

    private static string BuildExampleText(string? sentence)
    {
        var normalized = NormalizeCompactText(sentence);
        return string.IsNullOrWhiteSpace(normalized)
            ? "No example sentence."
            : normalized;
    }

    private static string BuildExampleTranslation(string? translation)
    {
        var normalized = NormalizeCompactText(translation);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized;
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
