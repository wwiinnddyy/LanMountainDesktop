using System;
using System.Diagnostics;
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

public partial class CnrDailyNewsWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
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

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(30)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly Bitmap?[] _newsBitmaps = new Bitmap?[2];
    private readonly string?[] _newsUrls = new string?[2];

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;

    public CnrDailyNewsWidget()
    {
        InitializeComponent();

        BrandPrimaryTextBlock.FontFamily = MiSansFontFamily;
        BrandSecondaryTextBlock.FontFamily = MiSansFontFamily;
        RefreshGlyphTextBlock.FontFamily = MiSansFontFamily;
        RefreshLabelTextBlock.FontFamily = MiSansFontFamily;
        News1PrefixTextBlock.FontFamily = MiSansFontFamily;
        News1TitleTextBlock.FontFamily = MiSansFontFamily;
        News2TitleTextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshButton.Click += OnRefreshButtonClick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        UpdateLanguageCode();
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
            _ = RefreshNewsAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        _recommendationService.ClearCache();
        if (_isAttached)
        {
            _ = RefreshNewsAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        UpdateRefreshButtonState();
        _refreshTimer.Start();
        _ = RefreshNewsAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        DisposeNewsBitmaps();
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

        await RefreshNewsAsync(forceRefresh: true);
        e.Handled = true;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshNewsAsync(forceRefresh: false);
    }

    private void OnNewsItem1PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenNewsUrl(0);
        e.Handled = true;
    }

    private void OnNewsItem2PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenNewsUrl(1);
        e.Handled = true;
    }

    private async Task RefreshNewsAsync(bool forceRefresh)
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
            var query = new DailyNewsQuery(
                Locale: _languageCode,
                ForceRefresh: forceRefresh);
            var result = await _recommendationService.GetDailyNewsAsync(query, cts.Token);
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
            UpdateRefreshButtonState();
        }
    }

    private async Task ApplySnapshotAsync(DailyNewsSnapshot snapshot, CancellationToken cancellationToken)
    {
        var items = snapshot.Items is null
            ? []
            : snapshot.Items.Take(2).ToArray();

        var item1 = items.Length > 0 ? items[0] : null;
        var item2 = items.Length > 1 ? items[1] : null;

        News1PrefixTextBlock.IsVisible = item1 is not null;
        News1TitleTextBlock.Text = NormalizeCompactText(item1?.Title);
        News2TitleTextBlock.Text = NormalizeCompactText(item2?.Title);

        _newsUrls[0] = NormalizeHttpUrl(item1?.Url);
        _newsUrls[1] = NormalizeHttpUrl(item2?.Url);
        UpdateNewsInteractionState();

        StatusTextBlock.IsVisible = false;
        UpdateAdaptiveLayout();

        var loadTasks = new[]
        {
            TryDownloadBitmapAsync(item1?.ImageUrl, cancellationToken),
            TryDownloadBitmapAsync(item2?.ImageUrl, cancellationToken)
        };
        var bitmaps = await Task.WhenAll(loadTasks);
        if (cancellationToken.IsCancellationRequested || !_isAttached)
        {
            bitmaps[0]?.Dispose();
            bitmaps[1]?.Dispose();
            return;
        }

        SetNewsBitmap(0, bitmaps[0]);
        SetNewsBitmap(1, bitmaps[1]);
    }

    private void ApplyLoadingState()
    {
        _newsUrls[0] = null;
        _newsUrls[1] = null;
        News1PrefixTextBlock.IsVisible = true;
        News1TitleTextBlock.Text = L("cnrnews.widget.loading_title", "正在获取新闻热点");
        News2TitleTextBlock.Text = L("cnrnews.widget.loading_subtitle", "请稍候");
        StatusTextBlock.Text = L("cnrnews.widget.loading", "加载中...");
        StatusTextBlock.IsVisible = true;
        UpdateNewsInteractionState();
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        _newsUrls[0] = null;
        _newsUrls[1] = null;
        News1PrefixTextBlock.IsVisible = false;
        News1TitleTextBlock.Text = L("cnrnews.widget.fallback_title", "央广网新闻暂不可用");
        News2TitleTextBlock.Text = L("cnrnews.widget.fallback_subtitle", "点击右上角稍后重试");
        StatusTextBlock.Text = L("cnrnews.widget.fetch_failed", "新闻获取失败");
        StatusTextBlock.IsVisible = true;
        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
        UpdateNewsInteractionState();
        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));
        RootBorder.Padding = new Thickness(
            Math.Clamp(16 * scale, 8, 28),
            Math.Clamp(12 * scale, 6, 20),
            Math.Clamp(16 * scale, 8, 28),
            Math.Clamp(12 * scale, 6, 20));

        CardBorder.CornerRadius = new CornerRadius(Math.Clamp(24 * scale, 12, 36));
        CardBorder.Padding = new Thickness(
            Math.Clamp(16 * scale, 8, 24),
            Math.Clamp(14 * scale, 7, 22),
            Math.Clamp(16 * scale, 8, 24),
            Math.Clamp(14 * scale, 7, 22));

        var headlineFont = Math.Clamp(28 * scale, 13, 36);
        BrandPrimaryTextBlock.FontSize = headlineFont;
        BrandSecondaryTextBlock.FontSize = headlineFont;

        var refreshHeight = Math.Clamp(42 * scale, 24, 52);
        var refreshWidth = Math.Clamp(116 * scale, 76, 152);
        RefreshButton.Height = refreshHeight;
        RefreshButton.Width = refreshWidth;
        RefreshButton.CornerRadius = new CornerRadius(refreshHeight / 2d);
        RefreshGlyphTextBlock.FontSize = Math.Clamp(19 * scale, 11, 26);
        RefreshLabelTextBlock.FontSize = Math.Clamp(25 * scale, 12, 32);

        var imageWidth = Math.Clamp(totalWidth * 0.23, 68, 170);
        var imageHeight = Math.Clamp(imageWidth * 0.56, 38, 94);
        News1ImageHost.Width = imageWidth;
        News1ImageHost.Height = imageHeight;
        News2ImageHost.Width = imageWidth;
        News2ImageHost.Height = imageHeight;
        News1ImageHost.CornerRadius = new CornerRadius(Math.Clamp(16 * scale, 8, 22));
        News2ImageHost.CornerRadius = new CornerRadius(Math.Clamp(16 * scale, 8, 22));

        var columnGap = Math.Clamp(12 * scale, 6, 18);
        NewsItem1Grid.ColumnSpacing = columnGap;
        NewsItem2Grid.ColumnSpacing = columnGap;
        NewsItem1Grid.ColumnDefinitions[1].Width = new GridLength(imageWidth);
        NewsItem2Grid.ColumnDefinitions[1].Width = new GridLength(imageWidth);

        var availableTextWidth = Math.Max(72, totalWidth - RootBorder.Padding.Left - RootBorder.Padding.Right - imageWidth - columnGap - Math.Clamp(24 * scale, 12, 36));
        News1TitleTextBlock.MaxWidth = availableTextWidth;
        News2TitleTextBlock.MaxWidth = availableTextWidth;

        var newsFont = Math.Clamp(25 * scale, 11, 32);
        News1PrefixTextBlock.FontSize = newsFont;
        News1TitleTextBlock.FontSize = newsFont;
        News2TitleTextBlock.FontSize = newsFont;
        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 9, 24);

        var compactLayout = totalHeight < _currentCellSize * 1.7;
        News1TitleTextBlock.MaxLines = compactLayout ? 1 : 2;
        News2TitleTextBlock.MaxLines = compactLayout ? 1 : 2;
    }

    private void UpdateRefreshButtonState()
    {
        RefreshButton.IsEnabled = !_isRefreshing;
        RefreshButton.Opacity = _isAttached ? 1.0 : 0.85;
        RefreshGlyphTextBlock.Opacity = _isRefreshing ? 0.56 : 1.0;
        RefreshLabelTextBlock.Opacity = _isRefreshing ? 0.56 : 1.0;
    }

    private void UpdateNewsInteractionState()
    {
        var item1Enabled = !string.IsNullOrWhiteSpace(_newsUrls[0]);
        var item2Enabled = !string.IsNullOrWhiteSpace(_newsUrls[1]);

        NewsItem1Grid.IsHitTestVisible = item1Enabled;
        NewsItem2Grid.IsHitTestVisible = item2Enabled;
        NewsItem1Grid.Opacity = item1Enabled ? 1.0 : 0.72;
        NewsItem2Grid.Opacity = item2Enabled ? 1.0 : 0.72;
    }

    private static async Task<Bitmap?> TryDownloadBitmapAsync(string? imageUrl, CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizeHttpUrl(imageUrl);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, normalizedUrl);
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

    private void TryOpenNewsUrl(int index)
    {
        if (index < 0 || index >= _newsUrls.Length)
        {
            return;
        }

        var normalizedUrl = NormalizeHttpUrl(_newsUrls[index]);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
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

    private void SetNewsBitmap(int index, Bitmap? bitmap)
    {
        if (index < 0 || index >= _newsBitmaps.Length)
        {
            bitmap?.Dispose();
            return;
        }

        var imageControl = index == 0 ? News1Image : News2Image;
        var oldBitmap = _newsBitmaps[index];
        if (ReferenceEquals(imageControl.Source, oldBitmap))
        {
            imageControl.Source = null;
        }

        oldBitmap?.Dispose();
        _newsBitmaps[index] = bitmap;
        imageControl.Source = bitmap;
    }

    private void DisposeNewsBitmaps()
    {
        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
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

    private static string NormalizeCompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(text.Trim(), " ");
    }
}
