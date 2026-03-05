using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class DailyArtworkWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly IReadOnlyDictionary<DayOfWeek, string> ZhWeekdays =
        new Dictionary<DayOfWeek, string>
        {
            [DayOfWeek.Monday] = "星期一",
            [DayOfWeek.Tuesday] = "星期二",
            [DayOfWeek.Wednesday] = "星期三",
            [DayOfWeek.Thursday] = "星期四",
            [DayOfWeek.Friday] = "星期五",
            [DayOfWeek.Saturday] = "星期六",
            [DayOfWeek.Sunday] = "星期日"
        };

    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");

    private static readonly HttpClient ImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;

    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private Bitmap? _currentArtworkBitmap;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;

    public DailyArtworkWidget()
    {
        InitializeComponent();

        DateTextBlock.FontFamily = MiSansFontFamily;
        WeekdayTextBlock.FontFamily = MiSansFontFamily;
        PaintingTitleTextBlock.FontFamily = MiSansFontFamily;
        ArtistTextBlock.FontFamily = MiSansFontFamily;
        YearTextBlock.FontFamily = MiSansFontFamily;

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        UpdateLanguageCode();
        UpdateDateLabels();
        ApplyLoadingState();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));

        InfoPanel.Padding = new Thickness(
            Math.Clamp(18 * scale, 10, 28),
            Math.Clamp(14 * scale, 8, 22),
            Math.Clamp(18 * scale, 10, 28),
            Math.Clamp(14 * scale, 8, 22));

        DateInfoStack.Margin = new Thickness(
            Math.Clamp(22 * scale, 10, 36),
            0,
            0,
            Math.Clamp(20 * scale, 10, 34));
        DateInfoStack.Spacing = Math.Clamp(2 * scale, 1, 6);

        ImageBottomShade.Height = Math.Clamp(132 * scale, 64, 182);

        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 10, 24);

        BrickPatternCanvas.Opacity = Math.Clamp(0.44 * scale, 0.20, 0.50);

        UpdateAdaptiveLayout();
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
        _recommendationService = recommendationInfoService ?? DefaultRecommendationService;
        if (_isAttached)
        {
            _ = RefreshArtworkAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        _recommendationService.ClearCache();
        if (_isAttached)
        {
            _ = RefreshArtworkAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _refreshTimer.Start();
        _ = RefreshArtworkAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        DisposeArtworkBitmap();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshArtworkAsync(forceRefresh: false);
    }

    private async Task RefreshArtworkAsync(bool forceRefresh)
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateLanguageCode();
        UpdateDateLabels();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var query = new DailyArtworkQuery(
                Locale: _languageCode,
                ForceRefresh: forceRefresh);
            var result = await _recommendationService.GetDailyArtworkAsync(query, cts.Token);
            if (!_isAttached || cts.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyFailedState();
                return;
            }

            await ApplySnapshotAsync(result.Data, cts.Token);
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
        }
    }

    private async Task ApplySnapshotAsync(DailyArtworkSnapshot snapshot, CancellationToken cancellationToken)
    {
        PaintingTitleTextBlock.Text = BuildQuotedTitle(snapshot.Title);

        var artist = string.IsNullOrWhiteSpace(snapshot.Artist)
            ? L("artwork.widget.unknown_artist", "Unknown artist")
            : snapshot.Artist.Trim();
        ArtistTextBlock.Text = NormalizeCompactText(artist);

        YearTextBlock.Text = ResolveYearText(snapshot);
        StatusTextBlock.IsVisible = false;

        UpdateAdaptiveLayout();

        var bitmap = await TryLoadArtworkBitmapAsync(snapshot.ImageUrl, snapshot.ThumbnailDataUrl, cancellationToken);
        if (cancellationToken.IsCancellationRequested || !_isAttached)
        {
            bitmap?.Dispose();
            return;
        }

        SetArtworkBitmap(bitmap);
    }

    private static async Task<Bitmap?> TryLoadArtworkBitmapAsync(
        string? imageUrl,
        string? thumbnailDataUrl,
        CancellationToken cancellationToken)
    {
        foreach (var candidateUrl in BuildImageUrlCandidates(imageUrl))
        {
            var remoteBitmap = await TryDownloadBitmapAsync(candidateUrl, cancellationToken);
            if (remoteBitmap is not null)
            {
                return remoteBitmap;
            }
        }

        return TryDecodeBitmapFromDataUrl(thumbnailDataUrl);
    }

    private static IEnumerable<string> BuildImageUrlCandidates(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            yield break;
        }

        var normalizedUrl = imageUrl.Trim();
        yield return normalizedUrl;

        const string preferredSizeSegment = "/full/843,/0/default.jpg";
        if (normalizedUrl.Contains(preferredSizeSegment, StringComparison.OrdinalIgnoreCase))
        {
            yield return normalizedUrl.Replace(
                preferredSizeSegment,
                "/full/1024,/0/default.jpg",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<Bitmap?> TryDownloadBitmapAsync(string imageUrl, CancellationToken cancellationToken)
    {
        var withReferrer = await SendImageRequestAsync(imageUrl, includeReferrer: true, cancellationToken);
        if (withReferrer is not null)
        {
            return withReferrer;
        }

        return await SendImageRequestAsync(imageUrl, includeReferrer: false, cancellationToken);
    }

    private static async Task<Bitmap?> SendImageRequestAsync(
        string imageUrl,
        bool includeReferrer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            if (includeReferrer && Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
            {
                request.Headers.Referrer = new Uri($"{imageUri.Scheme}://{imageUri.Host}/", UriKind.Absolute);
            }

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

        var base64Payload = trimmed[(markerIndex + 7)..];
        try
        {
            var bytes = Convert.FromBase64String(base64Payload);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private void ApplyLoadingState()
    {
        StatusTextBlock.IsVisible = true;
        StatusTextBlock.Text = L("artwork.widget.loading", "Loading...");
        PaintingTitleTextBlock.Text = BuildQuotedTitle(L("artwork.widget.loading_title", "Daily Artwork"));
        ArtistTextBlock.Text = L("artwork.widget.loading_subtitle", "Fetching today's masterpiece");
        YearTextBlock.Text = "--";
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        StatusTextBlock.IsVisible = true;
        StatusTextBlock.Text = L("artwork.widget.fetch_failed", "Artwork fetch failed");
        PaintingTitleTextBlock.Text = BuildQuotedTitle(L("artwork.widget.fallback_title", "Daily Artwork"));
        ArtistTextBlock.Text = L("artwork.widget.fallback_artist", "Recommendation service unavailable");
        YearTextBlock.Text = L("artwork.widget.fallback_year", "Try again later");
        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var leftStar = totalWidth < _currentCellSize * 4.2 ? 2.0 : 2.08;
        MainLayoutGrid.ColumnDefinitions[0].Width = new GridLength(leftStar, GridUnitType.Star);
        MainLayoutGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

        var rightPanelWidth = Math.Max(84, totalWidth / (leftStar + 1));
        var rightContentWidth = Math.Max(58, rightPanelWidth - InfoPanel.Padding.Left - InfoPanel.Padding.Right);
        var leftPanelWidth = Math.Max(84, totalWidth - rightPanelWidth);
        var leftContentWidth = Math.Max(52, leftPanelWidth - DateInfoStack.Margin.Left - 10);

        var dateBase = Math.Clamp(52 * scale, 18, 72);
        DateTextBlock.FontSize = FitFontSize(
            DateTextBlock.Text,
            leftContentWidth,
            Math.Max(22, totalHeight * 0.22),
            maxLines: 1,
            minFontSize: Math.Max(14, dateBase * 0.70),
            maxFontSize: dateBase,
            weight: FontWeight.Bold,
            lineHeightFactor: 1.02);
        DateTextBlock.LineHeight = DateTextBlock.FontSize * 1.02;

        WeekdayTextBlock.FontSize = FitFontSize(
            WeekdayTextBlock.Text,
            leftContentWidth,
            Math.Max(22, totalHeight * 0.24),
            maxLines: 1,
            minFontSize: Math.Max(14, dateBase * 0.70),
            maxFontSize: dateBase,
            weight: FontWeight.Bold,
            lineHeightFactor: 1.03);
        WeekdayTextBlock.LineHeight = WeekdayTextBlock.FontSize * 1.03;

        var titleBase = Math.Clamp(44 * scale, 16, 58);
        PaintingTitleTextBlock.MaxWidth = rightContentWidth;
        PaintingTitleTextBlock.FontSize = FitFontSize(
            PaintingTitleTextBlock.Text,
            rightContentWidth,
            Math.Max(20, totalHeight * 0.34),
            maxLines: 2,
            minFontSize: Math.Max(12, titleBase * 0.62),
            maxFontSize: titleBase,
            weight: FontWeight.Bold,
            lineHeightFactor: 1.08);
        PaintingTitleTextBlock.LineHeight = PaintingTitleTextBlock.FontSize * 1.08;

        var artistBase = Math.Clamp(26 * scale, 11, 34);
        ArtistTextBlock.MaxWidth = rightContentWidth;
        ArtistTextBlock.FontSize = FitFontSize(
            ArtistTextBlock.Text,
            rightContentWidth,
            Math.Max(18, totalHeight * 0.24),
            maxLines: 2,
            minFontSize: Math.Max(10, artistBase * 0.72),
            maxFontSize: artistBase,
            weight: FontWeight.SemiBold,
            lineHeightFactor: 1.12);
        ArtistTextBlock.LineHeight = ArtistTextBlock.FontSize * 1.12;

        var yearBase = Math.Clamp(22 * scale, 10, 30);
        YearTextBlock.MaxWidth = rightContentWidth;
        YearTextBlock.FontSize = FitFontSize(
            YearTextBlock.Text,
            rightContentWidth,
            Math.Max(14, totalHeight * 0.12),
            maxLines: 1,
            minFontSize: Math.Max(9.5, yearBase * 0.78),
            maxFontSize: yearBase,
            weight: FontWeight.Medium,
            lineHeightFactor: 1.04);
        YearTextBlock.LineHeight = YearTextBlock.FontSize * 1.04;

        RightPanelSeparator.Width = Math.Clamp(rightContentWidth * 0.58, 42, 136);
        RightPanelSeparator.Margin = new Thickness(0, 0, 0, Math.Clamp(10 * scale, 4, 14));

        BrickPatternCanvas.Opacity = totalWidth < _currentCellSize * 4.2
            ? 0.34
            : Math.Clamp(0.44 * scale, 0.24, 0.50);
    }

    private void SetArtworkBitmap(Bitmap? bitmap)
    {
        DisposeArtworkBitmap();
        _currentArtworkBitmap = bitmap;
        ArtworkImage.Source = bitmap;
    }

    private void DisposeArtworkBitmap()
    {
        if (_currentArtworkBitmap is null)
        {
            return;
        }

        if (ReferenceEquals(ArtworkImage.Source, _currentArtworkBitmap))
        {
            ArtworkImage.Source = null;
        }

        _currentArtworkBitmap.Dispose();
        _currentArtworkBitmap = null;
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

    private void UpdateDateLabels()
    {
        var now = DateTime.Now;
        DateTextBlock.Text = now.ToString("MM/dd", CultureInfo.InvariantCulture);

        if (string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase) &&
            ZhWeekdays.TryGetValue(now.DayOfWeek, out var weekdayZh))
        {
            WeekdayTextBlock.Text = weekdayZh;
            return;
        }

        var culture = ResolveCulture();
        WeekdayTextBlock.Text = culture.DateTimeFormat.GetDayName(now.DayOfWeek);
    }

    private string ResolveYearText(DailyArtworkSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Year))
        {
            return snapshot.Year.Trim();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Museum))
        {
            return snapshot.Museum.Trim();
        }

        return "--";
    }

    private static string BuildQuotedTitle(string title)
    {
        var normalized = NormalizeCompactText(title);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "Untitled";
        }

        return $"“{normalized}”";
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
        var cellScale = Math.Clamp(_currentCellSize / BaseCellSize, 0.62, 2.0);
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
