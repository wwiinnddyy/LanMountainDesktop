using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

public partial class DailyArtworkWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget, IComponentPlacementContextAware
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
    private static readonly FontWeight[] TitleWeightCandidates = new[] { FontWeight.Bold, FontWeight.SemiBold, FontWeight.Medium, FontWeight.Normal };
    private static readonly FontWeight[] ArtistWeightCandidates = new[] { FontWeight.SemiBold, FontWeight.Medium, FontWeight.Normal };
    private static readonly FontWeight[] SecondaryWeightCandidates = new[] { FontWeight.Medium, FontWeight.Normal, FontWeight.Light };

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

    private ISettingsService _settingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private Bitmap? _currentArtworkBitmap;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private string _componentId = BuiltInComponentIds.DesktopDailyArtwork;
    private string _placementId = string.Empty;
    private string? _currentArtworkSourceUrl;
    private string? _currentArtworkImageUrl;

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

        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(34 * scale, 16, 52);
        RootBorder.Padding = ComponentChromeCornerRadiusHelper.SafeThickness(
            12 * scale,
            10 * scale,
            null,
            0.45d);

        InfoPanel.Padding = ComponentChromeCornerRadiusHelper.SafeThickness(
            18 * scale,
            14 * scale,
            null,
            0.52d);

        DateInfoStack.Margin = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(18 * scale, 8, 30),
            0,
            0,
            ComponentChromeCornerRadiusHelper.SafeValue(16 * scale, 8, 26));
        DateInfoStack.Spacing = Math.Clamp(4 * scale, 2, 10);

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

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopDailyArtwork
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        RefreshFromSettings();
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

    private void OnArtworkPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ = RefreshArtworkAsync(forceRefresh: true);
        e.Handled = true;
    }

    private void OnInfoPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenArtworkSourceUrl();
        e.Handled = true;
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
                MirrorSource: ResolveMirrorSource(),
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
        _currentArtworkSourceUrl = snapshot.ArtworkUrl;
        _currentArtworkImageUrl = snapshot.ImageUrl;
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
        _currentArtworkSourceUrl = null;
        _currentArtworkImageUrl = null;
        StatusTextBlock.IsVisible = true;
        StatusTextBlock.Text = L("artwork.widget.loading", "Loading...");
        PaintingTitleTextBlock.Text = BuildQuotedTitle(L("artwork.widget.loading_title", "Daily Artwork"));
        ArtistTextBlock.Text = L("artwork.widget.loading_subtitle", "Fetching today's masterpiece");
        YearTextBlock.Text = "--";
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        _currentArtworkSourceUrl = null;
        _currentArtworkImageUrl = null;
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
        var rootPadding = RootBorder.Padding;

        var leftStar = totalWidth < _currentCellSize * 4.2 ? 2.0 : 2.08;
        MainLayoutGrid.ColumnDefinitions[0].Width = new GridLength(leftStar, GridUnitType.Star);
        MainLayoutGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

        var availableWidth = Math.Max(84, totalWidth - rootPadding.Left - rootPadding.Right);
        var rightPanelWidth = Math.Max(84, availableWidth / (leftStar + 1));
        var rightContentWidth = Math.Max(58, rightPanelWidth - InfoPanel.Padding.Left - InfoPanel.Padding.Right);
        var leftPanelWidth = Math.Max(84, availableWidth - rightPanelWidth);
        var leftContentWidth = Math.Max(52, leftPanelWidth - DateInfoStack.Margin.Left - 10);
        var leftContentHeight = Math.Max(30, totalHeight - rootPadding.Top - rootPadding.Bottom - DateInfoStack.Margin.Bottom - 10);

        var dateStackSpacing = Math.Clamp(4 * scale, 2, 10);
        DateInfoStack.Spacing = dateStackSpacing;
        DateInfoStack.MaxWidth = leftContentWidth;
        var leftSingleLineHeight = Math.Max(12, (leftContentHeight - dateStackSpacing) / 2d);

        var dateBase = Math.Clamp(44 * scale, 16, 62);
        DateTextBlock.FontSize = FitFontSize(
            DateTextBlock.Text,
            leftContentWidth,
            leftSingleLineHeight,
            maxLines: 1,
            minFontSize: Math.Max(12, dateBase * 0.68),
            maxFontSize: dateBase,
            weight: FontWeight.Bold,
            lineHeightFactor: 1.10);
        DateTextBlock.LineHeight = DateTextBlock.FontSize * 1.10;

        WeekdayTextBlock.FontSize = FitFontSize(
            WeekdayTextBlock.Text,
            leftContentWidth,
            leftSingleLineHeight,
            maxLines: 1,
            minFontSize: Math.Max(12, dateBase * 0.68),
            maxFontSize: dateBase,
            weight: FontWeight.Bold,
            lineHeightFactor: 1.10);
        WeekdayTextBlock.LineHeight = WeekdayTextBlock.FontSize * 1.10;

        var rightContentHeight = Math.Max(42, totalHeight - rootPadding.Top - rootPadding.Bottom - InfoPanel.Padding.Top - InfoPanel.Padding.Bottom);
        var titleBottomMargin = Math.Clamp(8 * scale, 4, 14);
        var separatorBottomMargin = Math.Clamp(10 * scale, 4, 14);
        var bottomStackSpacing = Math.Clamp(3 * scale, 2, 8);
        var reservedHeight = titleBottomMargin + separatorBottomMargin + bottomStackSpacing + 3;
        var textHeightBudget = Math.Max(24, rightContentHeight - reservedHeight);
        var titleBase = Math.Clamp(44 * scale, 16, 58);
        var artistBase = Math.Clamp(26 * scale, 11, 34);
        var yearBase = Math.Clamp(22 * scale, 10, 30);
        var titleMin = Math.Max(9.2, titleBase * 0.42);
        var artistMin = Math.Max(8.4, artistBase * 0.50);
        var yearMin = Math.Max(8.0, yearBase * 0.54);

        var titleDemand = Math.Clamp(NormalizeCompactText(PaintingTitleTextBlock.Text).Length, 6, 96);
        var artistDemand = Math.Clamp(NormalizeCompactText(ArtistTextBlock.Text).Length, 4, 72);
        var yearDemand = Math.Clamp(NormalizeCompactText(YearTextBlock.Text).Length, 2, 48);

        var minTitleHeight = Math.Max(10, titleMin * 1.10 * 2);
        var minArtistHeight = Math.Max(8, artistMin * 1.14);
        var minYearHeight = Math.Max(8, yearMin * 1.08);
        var minTextHeightTotal = minTitleHeight + minArtistHeight + minYearHeight;

        double titleHeightBudget;
        double artistHeightBudget;
        double yearHeightBudget;
        if (textHeightBudget <= minTextHeightTotal + 0.6)
        {
            var compression = textHeightBudget / Math.Max(1, minTextHeightTotal);
            titleHeightBudget = Math.Max(9, minTitleHeight * compression);
            artistHeightBudget = Math.Max(7, minArtistHeight * compression);
            yearHeightBudget = Math.Max(7, minYearHeight * compression);
        }
        else
        {
            var extraHeight = textHeightBudget - minTextHeightTotal;
            var titleWeight = titleDemand + 8d;
            var artistWeight = artistDemand + 4d;
            var yearWeight = yearDemand + 2d;
            var weightSum = Math.Max(1d, titleWeight + artistWeight + yearWeight);

            titleHeightBudget = minTitleHeight + extraHeight * (titleWeight / weightSum);
            artistHeightBudget = minArtistHeight + extraHeight * (artistWeight / weightSum);
            yearHeightBudget = minYearHeight + extraHeight * (yearWeight / weightSum);
        }

        var titleLayout = FitAdaptiveTextLayout(
            PaintingTitleTextBlock.Text,
            rightContentWidth,
            titleHeightBudget,
            minLines: 2,
            maxLines: 5,
            minFontSize: titleMin,
            maxFontSize: titleBase,
            weightCandidates: TitleWeightCandidates,
            lineHeightFactor: 1.10);
        PaintingTitleTextBlock.MaxWidth = rightContentWidth;
        PaintingTitleTextBlock.Margin = new Thickness(0, 0, 0, titleBottomMargin);
        PaintingTitleTextBlock.MaxLines = titleLayout.MaxLines;
        PaintingTitleTextBlock.FontWeight = titleLayout.Weight;
        PaintingTitleTextBlock.FontSize = titleLayout.FontSize;
        PaintingTitleTextBlock.LineHeight = titleLayout.LineHeight;

        if (ArtistTextBlock.Parent is StackPanel artistInfoStack)
        {
            artistInfoStack.Spacing = bottomStackSpacing;
        }

        var artistLayout = FitAdaptiveTextLayout(
            ArtistTextBlock.Text,
            rightContentWidth,
            artistHeightBudget,
            minLines: 1,
            maxLines: 4,
            minFontSize: artistMin,
            maxFontSize: artistBase,
            weightCandidates: ArtistWeightCandidates,
            lineHeightFactor: 1.14);
        ArtistTextBlock.MaxWidth = rightContentWidth;
        ArtistTextBlock.MaxLines = artistLayout.MaxLines;
        ArtistTextBlock.FontWeight = artistLayout.Weight;
        ArtistTextBlock.FontSize = artistLayout.FontSize;
        ArtistTextBlock.LineHeight = artistLayout.LineHeight;

        var yearLayout = FitAdaptiveTextLayout(
            YearTextBlock.Text,
            rightContentWidth,
            yearHeightBudget,
            minLines: 1,
            maxLines: 3,
            minFontSize: yearMin,
            maxFontSize: yearBase,
            weightCandidates: SecondaryWeightCandidates,
            lineHeightFactor: 1.08);
        YearTextBlock.MaxWidth = rightContentWidth;
        YearTextBlock.MaxLines = yearLayout.MaxLines;
        YearTextBlock.FontWeight = yearLayout.Weight;
        YearTextBlock.FontSize = yearLayout.FontSize;
        YearTextBlock.LineHeight = yearLayout.LineHeight;

        RightPanelSeparator.Width = Math.Clamp(rightContentWidth * 0.58, 42, 136);
        RightPanelSeparator.Margin = new Thickness(0, 0, 0, separatorBottomMargin);

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

    private void TryOpenArtworkSourceUrl()
    {
        var candidate = _currentArtworkSourceUrl;
        if (!TryNormalizeHttpUrl(candidate, out var normalizedUrl) &&
            !TryNormalizeHttpUrl(_currentArtworkImageUrl, out normalizedUrl))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = normalizedUrl,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch
        {
            // Ignore malformed URLs or shell launch failures.
        }
    }

    private static bool TryNormalizeHttpUrl(string? rawUrl, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        var candidate = rawUrl.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUrl = uri.ToString();
        return true;
    }

    private void UpdateLanguageCode()
    {
        try
        {
            var snapshot = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
        }
    }

    private string ResolveMirrorSource()
    {
        try
        {
            var snapshot = _settingsService.LoadSnapshot<ComponentSettingsSnapshot>(
                SettingsScope.ComponentInstance,
                _componentId,
                _placementId);
            return DailyArtworkMirrorSources.Normalize(snapshot.DailyArtworkMirrorSource);
        }
        catch
        {
            return DailyArtworkMirrorSources.Overseas;
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
            : new[] { FontWeight.Normal };

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
                var measuredLineCount = ResolveLineCount(measuredSize.Height, lineHeight);
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

    private static int ResolveLineCount(double measuredHeight, double lineHeight)
    {
        return Math.Max(1, (int)Math.Ceiling(measuredHeight / Math.Max(1, lineHeight)));
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
