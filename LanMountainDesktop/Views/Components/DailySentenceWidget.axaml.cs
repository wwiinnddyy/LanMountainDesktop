using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class DailySentenceWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly FontWeight[] HeadlineWeightCandidates = [FontWeight.Bold, FontWeight.SemiBold, FontWeight.Medium];
    private static readonly FontWeight[] BodyWeightCandidates = [FontWeight.Medium, FontWeight.Normal];
    private static readonly FontWeight[] MetaWeightCandidates = [FontWeight.Medium, FontWeight.Normal, FontWeight.Light];
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private static readonly HttpClient ImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;
    private static readonly int[] SupportedAutoRotateIntervalsMinutes = [5, 10, 40, 60, 720, 1440];

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private Bitmap? _backgroundBitmap;
    private string? _currentSourceUrl;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRotateEnabled = true;

    public DailySentenceWidget()
    {
        InitializeComponent();

        DayTextBlock.FontFamily = MiSansFontFamily;
        MonthYearTextBlock.FontFamily = MiSansFontFamily;
        SentenceTextBlock.FontFamily = MiSansFontFamily;
        TranslationTextBlock.FontFamily = MiSansFontFamily;
        SourceTextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshButton.Click += OnRefreshButtonClick;
        SourceTextBlock.PointerPressed += OnSourceTextBlockPointerPressed;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        UpdateLanguageCode();
        ApplyAutoRotateSettings();
        UpdateDateText();
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
            _ = RefreshSentenceAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        _recommendationService.ClearCache();
        ApplyAutoRotateSettings();
        if (_isAttached)
        {
            _ = RefreshSentenceAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRotateSettings();
        UpdateRefreshButtonState();
        _ = RefreshSentenceAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        DisposeBackgroundBitmap();
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

        await RefreshSentenceAsync(forceRefresh: true);
        e.Handled = true;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshSentenceAsync(forceRefresh: true);
    }

    private void OnSourceTextBlockPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenSourceUrl();
        e.Handled = true;
    }

    private async Task RefreshSentenceAsync(bool forceRefresh)
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateRefreshButtonState();
        UpdateLanguageCode();
        UpdateDateText();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var sentenceQuery = new DailyWordQuery(
                Locale: _languageCode,
                ForceRefresh: forceRefresh);
            var sentenceResult = await _recommendationService.GetDailyWordAsync(sentenceQuery, cts.Token);
            if (!_isAttached || cts.IsCancellationRequested)
            {
                return;
            }

            if (!sentenceResult.Success || sentenceResult.Data is null)
            {
                ApplyFailedState();
            }
            else
            {
                ApplySentenceSnapshot(sentenceResult.Data);
            }

            var artworkQuery = new DailyArtworkQuery(
                Locale: _languageCode,
                ForceRefresh: forceRefresh);
            var artworkResult = await _recommendationService.GetDailyArtworkAsync(artworkQuery, cts.Token);
            if (!_isAttached || cts.IsCancellationRequested)
            {
                return;
            }

            if (artworkResult.Success && artworkResult.Data is not null)
            {
                await ApplyBackgroundSnapshotAsync(artworkResult.Data, cts.Token);
            }
            else if (_backgroundBitmap is null)
            {
                BackgroundImage.Source = null;
            }
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

    private void ApplySentenceSnapshot(DailyWordSnapshot snapshot)
    {
        var sentence = NormalizeCompactText(snapshot.ExampleSentence);
        if (string.IsNullOrWhiteSpace(sentence))
        {
            sentence = NormalizeCompactText(snapshot.Meaning);
        }

        if (string.IsNullOrWhiteSpace(sentence))
        {
            sentence = L("dailysentence.widget.fallback_sentence", "No sentence available.");
        }

        var translation = NormalizeCompactText(snapshot.ExampleTranslation);
        if (string.IsNullOrWhiteSpace(translation))
        {
            translation = NormalizeCompactText(snapshot.Meaning);
        }

        if (string.IsNullOrWhiteSpace(translation))
        {
            translation = L("dailysentence.widget.fallback_translation", "Tap refresh and try again.");
        }

        var sourceWord = NormalizeCompactText(snapshot.Word);
        if (string.IsNullOrWhiteSpace(sourceWord))
        {
            sourceWord = L("dailysentence.widget.source_default", "Youdao Dictionary");
        }

        SentenceTextBlock.Text = sentence;
        TranslationTextBlock.Text = translation;
        SourceTextBlock.Text = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? $"有道词典 · {sourceWord}"
            : $"Youdao Dictionary · {sourceWord}";
        _currentSourceUrl = NormalizeHttpUrl(snapshot.SourceUrl);

        StatusTextBlock.IsVisible = false;
        UpdateSourceInteractionState();
        UpdateAdaptiveLayout();
    }

    private async Task ApplyBackgroundSnapshotAsync(DailyArtworkSnapshot snapshot, CancellationToken cancellationToken)
    {
        var bitmap = await TryLoadBackgroundBitmapAsync(snapshot.ImageUrl, snapshot.ThumbnailDataUrl, cancellationToken);
        if (cancellationToken.IsCancellationRequested || !_isAttached)
        {
            bitmap?.Dispose();
            return;
        }

        SetBackgroundBitmap(bitmap);
    }

    private static async Task<Bitmap?> TryLoadBackgroundBitmapAsync(
        string? imageUrl,
        string? thumbnailDataUrl,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizeHttpUrl(imageUrl);
        if (!string.IsNullOrWhiteSpace(normalizedUrl))
        {
            var remote = await TryDownloadBitmapAsync(normalizedUrl, cancellationToken);
            if (remote is not null)
            {
                return remote;
            }
        }

        return TryDecodeBitmapFromDataUrl(thumbnailDataUrl);
    }

    private static async Task<Bitmap?> TryDownloadBitmapAsync(string imageUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");

            using var response = await ImageHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            return new Bitmap(memory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryDecodeBitmapFromDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return null;
        }

        var trimmed = dataUrl.Trim();
        var markerIndex = trimmed.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || markerIndex + 7 >= trimmed.Length)
        {
            return null;
        }

        var payload = trimmed[(markerIndex + 7)..];
        try
        {
            var bytes = Convert.FromBase64String(payload);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private void ApplyLoadingState()
    {
        _currentSourceUrl = null;
        SentenceTextBlock.Text = L("dailysentence.widget.loading_sentence", "Loading sentence...");
        TranslationTextBlock.Text = L("dailysentence.widget.loading_translation", "Loading translation...");
        SourceTextBlock.Text = L("dailysentence.widget.loading_source", "Youdao Dictionary");
        StatusTextBlock.Text = L("dailysentence.widget.loading", "Loading...");
        StatusTextBlock.IsVisible = true;
        UpdateSourceInteractionState();
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        _currentSourceUrl = null;
        SentenceTextBlock.Text = L("dailysentence.widget.fallback_sentence", "No sentence available.");
        TranslationTextBlock.Text = L("dailysentence.widget.fallback_translation", "Tap refresh and try again.");
        SourceTextBlock.Text = L("dailysentence.widget.source_default", "Youdao Dictionary");
        StatusTextBlock.Text = L("dailysentence.widget.fetch_failed", "Sentence fetch failed");
        StatusTextBlock.IsVisible = true;
        UpdateSourceInteractionState();
        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));
        ContentGrid.Margin = new Thickness(
            Math.Clamp(16 * scale, 8, 28),
            Math.Clamp(14 * scale, 7, 24),
            Math.Clamp(16 * scale, 8, 28),
            Math.Clamp(14 * scale, 7, 24));
        ContentGrid.RowSpacing = Math.Clamp(8 * scale, 4, 12);
        HeaderGrid.ColumnSpacing = Math.Clamp(8 * scale, 4, 14);

        var refreshSize = Math.Clamp(42 * scale, 24, 54);
        RefreshButton.Width = refreshSize;
        RefreshButton.Height = refreshSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshSize / 2d);
        RefreshIcon.FontSize = Math.Clamp(21 * scale, 12, 28);

        var innerWidth = Math.Max(100, totalWidth - ContentGrid.Margin.Left - ContentGrid.Margin.Right);
        var innerHeight = Math.Max(56, totalHeight - ContentGrid.Margin.Top - ContentGrid.Margin.Bottom);
        var rowSpacingTotal = ContentGrid.RowSpacing * 2d;
        var availableRowsHeight = Math.Max(1, innerHeight - rowSpacingTotal);

        var topRowHeight = Math.Clamp(innerHeight * 0.24, 18, 98);
        var bottomRowHeight = Math.Clamp(innerHeight * 0.15, 11, 54);
        var minTopRowHeight = Math.Max(Math.Clamp(14 * scale, 10, 28), refreshSize * 0.60);
        var minBottomRowHeight = Math.Clamp(10 * scale, 7, 22);
        var minMiddleRowHeight = Math.Clamp(22 * scale, 14, 46);

        if (topRowHeight + bottomRowHeight + minMiddleRowHeight > availableRowsHeight)
        {
            var scaling = availableRowsHeight / Math.Max(1, topRowHeight + bottomRowHeight + minMiddleRowHeight);
            topRowHeight = Math.Max(minTopRowHeight, topRowHeight * scaling);
            bottomRowHeight = Math.Max(minBottomRowHeight, bottomRowHeight * scaling);
        }

        var middleHeight = Math.Max(
            minMiddleRowHeight,
            availableRowsHeight - topRowHeight - bottomRowHeight);

        if (ContentGrid.RowDefinitions.Count >= 3)
        {
            ContentGrid.RowDefinitions[0].Height = new GridLength(topRowHeight, GridUnitType.Pixel);
            ContentGrid.RowDefinitions[1].Height = new GridLength(middleHeight, GridUnitType.Pixel);
            ContentGrid.RowDefinitions[2].Height = new GridLength(bottomRowHeight, GridUnitType.Pixel);
        }

        var topTextWidth = Math.Max(76, innerWidth - refreshSize - ContentGrid.RowSpacing);
        var dayWidth = Math.Clamp(topTextWidth * 0.18, 20, Math.Max(24, topTextWidth * 0.32));
        var monthYearWidth = Math.Max(48, topTextWidth - dayWidth - Math.Clamp(6 * scale, 2, 12));
        DayTextBlock.MaxWidth = dayWidth;
        MonthYearTextBlock.MaxWidth = monthYearWidth;

        var dayLayout = FitAdaptiveTextLayout(
            DayTextBlock.Text,
            dayWidth,
            topRowHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: Math.Clamp(26 * scale, 12, 44),
            maxFontSize: Math.Clamp(72 * scale, 20, 96),
            weightCandidates: HeadlineWeightCandidates,
            lineHeightFactor: 0.94);
        DayTextBlock.FontSize = dayLayout.FontSize;
        DayTextBlock.FontWeight = dayLayout.Weight;
        DayTextBlock.LineHeight = dayLayout.LineHeight;

        var monthLayout = FitAdaptiveTextLayout(
            MonthYearTextBlock.Text,
            monthYearWidth,
            topRowHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: Math.Clamp(18 * scale, 9, 32),
            maxFontSize: Math.Clamp(44 * scale, 14, 62),
            weightCandidates: BodyWeightCandidates,
            lineHeightFactor: 1.00);
        MonthYearTextBlock.FontSize = monthLayout.FontSize;
        MonthYearTextBlock.FontWeight = monthLayout.Weight;
        MonthYearTextBlock.LineHeight = monthLayout.LineHeight;

        var sentenceTextDemand = Math.Clamp(NormalizeCompactText(SentenceTextBlock.Text).Length, 12, 360);
        var translationTextDemand = Math.Clamp(NormalizeCompactText(TranslationTextBlock.Text).Length, 8, 260);

        var sentenceMinLines = innerHeight >= _currentCellSize * 1.78 ? 2 : 1;
        var sentenceMaxLines = innerHeight >= _currentCellSize * 2.7
            ? 4
            : innerHeight >= _currentCellSize * 1.92
                ? 3
                : 2;
        var translationMaxLines = innerHeight >= _currentCellSize * 2.35 ? 3 : 2;

        var sentenceMinFont = Math.Clamp(23 * scale, 10, 42);
        var translationMinFont = Math.Clamp(16 * scale, 8.5, 30);
        var sentenceMinHeight = sentenceMinFont * 1.06 * sentenceMinLines;
        var translationMinHeight = translationMinFont * 1.06;
        var bodyGap = Math.Clamp(8 * scale, 3, 12);
        SentenceStack.Spacing = bodyGap;
        var minBodyHeight = sentenceMinHeight + translationMinHeight + bodyGap;

        double sentenceHeight;
        double translationHeight;
        if (middleHeight <= minBodyHeight + 0.6)
        {
            var compression = middleHeight / Math.Max(1, minBodyHeight);
            sentenceHeight = Math.Max(10, sentenceMinHeight * compression);
            translationHeight = Math.Max(8, translationMinHeight * compression);
        }
        else
        {
            var extraHeight = middleHeight - minBodyHeight;
            var sentenceWeight = sentenceTextDemand + 16d;
            var translationWeight = translationTextDemand + 8d;
            var totalWeight = Math.Max(1d, sentenceWeight + translationWeight);
            sentenceHeight = sentenceMinHeight + extraHeight * (sentenceWeight / totalWeight);
            translationHeight = translationMinHeight + extraHeight * (translationWeight / totalWeight);
        }

        var sentenceLayout = FitAdaptiveTextLayout(
            SentenceTextBlock.Text,
            innerWidth,
            sentenceHeight,
            minLines: sentenceMinLines,
            maxLines: sentenceMaxLines,
            minFontSize: sentenceMinFont,
            maxFontSize: Math.Clamp(58 * scale, 18, 80),
            weightCandidates: HeadlineWeightCandidates,
            lineHeightFactor: 1.06);
        SentenceTextBlock.MaxWidth = innerWidth;
        SentenceTextBlock.MaxLines = sentenceLayout.MaxLines;
        SentenceTextBlock.FontSize = sentenceLayout.FontSize;
        SentenceTextBlock.FontWeight = sentenceLayout.Weight;
        SentenceTextBlock.LineHeight = sentenceLayout.LineHeight;

        var translationLayout = FitAdaptiveTextLayout(
            TranslationTextBlock.Text,
            innerWidth,
            translationHeight,
            minLines: 1,
            maxLines: translationMaxLines,
            minFontSize: translationMinFont,
            maxFontSize: Math.Clamp(40 * scale, 12, 54),
            weightCandidates: BodyWeightCandidates,
            lineHeightFactor: 1.06);
        TranslationTextBlock.MaxWidth = innerWidth;
        TranslationTextBlock.MaxLines = translationLayout.MaxLines;
        TranslationTextBlock.FontSize = translationLayout.FontSize;
        TranslationTextBlock.FontWeight = translationLayout.Weight;
        TranslationTextBlock.LineHeight = translationLayout.LineHeight;
        SentenceTextBlock.MinHeight = Math.Max(0, sentenceLayout.LineHeight * Math.Min(sentenceLayout.MaxLines, Math.Max(1, sentenceMinLines)));
        TranslationTextBlock.MinHeight = Math.Max(0, translationLayout.LineHeight);

        var sourceLayout = FitAdaptiveTextLayout(
            SourceTextBlock.Text,
            innerWidth,
            bottomRowHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: Math.Clamp(14 * scale, 8, 26),
            maxFontSize: Math.Clamp(30 * scale, 10, 40),
            weightCandidates: MetaWeightCandidates,
            lineHeightFactor: 1.02);
        SourceTextBlock.MaxWidth = innerWidth;
        SourceTextBlock.FontSize = sourceLayout.FontSize;
        SourceTextBlock.FontWeight = sourceLayout.Weight;
        SourceTextBlock.LineHeight = sourceLayout.LineHeight;

        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 9, 24);
    }

    private void UpdateRefreshButtonState()
    {
        RefreshButton.IsEnabled = !_isRefreshing;
        RefreshButton.Opacity = _isAttached ? 1.0 : 0.85;
        RefreshIcon.Opacity = _isRefreshing ? 0.56 : 1.0;
    }

    private void UpdateSourceInteractionState()
    {
        var enabled = !string.IsNullOrWhiteSpace(_currentSourceUrl);
        SourceTextBlock.IsHitTestVisible = enabled;
        SourceTextBlock.Cursor = enabled
            ? new Cursor(StandardCursorType.Hand)
            : new Cursor(StandardCursorType.Arrow);
        SourceTextBlock.Opacity = enabled ? 1.0 : 0.86;
    }

    private void UpdateLanguageCode()
    {
        try
        {
            var snapshot = _settingsService.Load();
            _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
        }
    }

    private void ApplyAutoRotateSettings()
    {
        var enabled = true;
        var intervalMinutes = 60;

        try
        {
            var snapshot = _settingsService.Load();
            enabled = snapshot.DailySentenceAutoRotateEnabled;
            intervalMinutes = NormalizeAutoRotateIntervalMinutes(snapshot.DailySentenceAutoRotateIntervalMinutes);
        }
        catch
        {
            // Keep fallback defaults.
        }

        _autoRotateEnabled = enabled;
        _refreshTimer.Interval = TimeSpan.FromMinutes(intervalMinutes);

        if (!_isAttached)
        {
            return;
        }

        if (_autoRotateEnabled)
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

    private static int NormalizeAutoRotateIntervalMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return 60;
        }

        if (SupportedAutoRotateIntervalsMinutes.Contains(minutes))
        {
            return minutes;
        }

        return SupportedAutoRotateIntervalsMinutes
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(60);
    }

    private void UpdateDateText()
    {
        var now = DateTime.Now;
        var culture = ResolveCulture();
        DayTextBlock.Text = now.Day.ToString(CultureInfo.InvariantCulture);
        MonthYearTextBlock.Text = now.ToString("MMMM yyyy", culture);
    }

    private void SetBackgroundBitmap(Bitmap? bitmap)
    {
        if (ReferenceEquals(BackgroundImage.Source, _backgroundBitmap))
        {
            BackgroundImage.Source = null;
        }

        _backgroundBitmap?.Dispose();
        _backgroundBitmap = bitmap;
        BackgroundImage.Source = bitmap;
    }

    private void DisposeBackgroundBitmap()
    {
        SetBackgroundBitmap(null);
    }

    private void TryOpenSourceUrl()
    {
        var normalized = NormalizeHttpUrl(_currentSourceUrl);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = normalized,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch
        {
            // Ignore malformed URLs or shell launch failures.
        }
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

    private CultureInfo ResolveCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(_languageCode);
        }
        catch
        {
            return CultureInfo.InvariantCulture;
        }
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

    private static string NormalizeCompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(text.Trim(), " ");
    }

    private static string? NormalizeHttpUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var candidate = rawUrl.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
    }

    private static AdaptiveTextLayout FitAdaptiveTextLayout(
        string? text,
        double maxWidth,
        double maxHeight,
        int minLines,
        int maxLines,
        double minFontSize,
        double maxFontSize,
        FontWeight[] weightCandidates,
        double lineHeightFactor)
    {
        var content = string.IsNullOrWhiteSpace(text) ? " " : text.Trim();
        var safeMinLines = Math.Max(1, minLines);
        var safeMaxLines = Math.Max(safeMinLines, maxLines);
        var linesByHeight = ResolveMaxLinesByHeight(maxHeight, minFontSize, lineHeightFactor, safeMinLines, safeMaxLines);

        var candidates = weightCandidates is { Length: > 0 }
            ? weightCandidates
            : [FontWeight.Normal];

        AdaptiveTextLayout? best = null;
        foreach (var weight in candidates)
        {
            for (var lineLimit = linesByHeight; lineLimit >= safeMinLines; lineLimit--)
            {
                var fontSize = FitFontSize(
                    content,
                    maxWidth,
                    maxHeight,
                    lineLimit,
                    minFontSize,
                    maxFontSize,
                    weight,
                    lineHeightFactor);
                var lineHeight = fontSize * lineHeightFactor;
                var measuredSize = MeasureTextSize(content, fontSize, weight, Math.Max(1, maxWidth), lineHeight);
                var measuredLineCount = Math.Max(1, (int)Math.Ceiling(measuredSize.Height / Math.Max(1, lineHeight)));
                var overflowLines = Math.Max(0, measuredLineCount - lineLimit);
                var overflowHeight = Math.Max(0, measuredSize.Height - maxHeight);
                var overflowScore = overflowLines * 1000d + overflowHeight;
                var fitsCompletely = overflowLines == 0 && overflowHeight <= 0.6;
                var candidate = new AdaptiveTextLayout(fontSize, weight, lineLimit, lineHeight, overflowScore, fitsCompletely);

                if (best is null || IsBetterAdaptiveTextCandidate(candidate, best.Value))
                {
                    best = candidate;
                }
            }
        }

        if (best is not null)
        {
            return best.Value;
        }

        var fallbackFontSize = Math.Max(6, minFontSize);
        return new AdaptiveTextLayout(
            fallbackFontSize,
            FontWeight.Normal,
            safeMinLines,
            fallbackFontSize * lineHeightFactor,
            double.MaxValue,
            fitsCompletely: false);
    }

    private static bool IsBetterAdaptiveTextCandidate(AdaptiveTextLayout candidate, AdaptiveTextLayout best)
    {
        if (candidate.FitsCompletely && !best.FitsCompletely)
        {
            return true;
        }

        if (!candidate.FitsCompletely && best.FitsCompletely)
        {
            return false;
        }

        if (candidate.FitsCompletely && best.FitsCompletely)
        {
            if (candidate.FontSize > best.FontSize + 0.12)
            {
                return true;
            }

            if (Math.Abs(candidate.FontSize - best.FontSize) <= 0.12 && candidate.MaxLines < best.MaxLines)
            {
                return true;
            }

            return false;
        }

        if (candidate.OverflowScore < best.OverflowScore - 0.2)
        {
            return true;
        }

        if (Math.Abs(candidate.OverflowScore - best.OverflowScore) <= 0.2 &&
            candidate.FontSize > best.FontSize + 0.12)
        {
            return true;
        }

        if (Math.Abs(candidate.OverflowScore - best.OverflowScore) <= 0.2 &&
            Math.Abs(candidate.FontSize - best.FontSize) <= 0.12 &&
            candidate.MaxLines > best.MaxLines)
        {
            return true;
        }

        return false;
    }

    private static int ResolveMaxLinesByHeight(
        double maxHeight,
        double minFontSize,
        double lineHeightFactor,
        int minLines,
        int maxLines)
    {
        var safeMinLines = Math.Max(1, minLines);
        var safeMaxLines = Math.Max(safeMinLines, maxLines);
        var lineHeight = Math.Max(1, Math.Max(6, minFontSize) * lineHeightFactor);
        var maxHeightWithTolerance = Math.Max(1, maxHeight + 0.6);
        var linesByHeight = (int)Math.Floor(maxHeightWithTolerance / lineHeight);
        return Math.Clamp(linesByHeight, safeMinLines, safeMaxLines);
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

    private readonly struct AdaptiveTextLayout
    {
        public AdaptiveTextLayout(
            double fontSize,
            FontWeight weight,
            int maxLines,
            double lineHeight,
            double overflowScore,
            bool fitsCompletely)
        {
            FontSize = fontSize;
            Weight = weight;
            MaxLines = Math.Max(1, maxLines);
            LineHeight = lineHeight;
            OverflowScore = overflowScore;
            FitsCompletely = fitsCompletely;
        }

        public double FontSize { get; }

        public FontWeight Weight { get; }

        public int MaxLines { get; }

        public double LineHeight { get; }

        public double OverflowScore { get; }

        public bool FitsCompletely { get; }
    }
}
