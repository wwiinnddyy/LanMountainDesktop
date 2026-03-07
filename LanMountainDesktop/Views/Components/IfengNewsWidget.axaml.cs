using System;
using System.Collections.Generic;
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
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class IfengNewsWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
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
    private const int BaseHeightCells = 4;
    private const int MaxDisplayItemCount = 4;
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(20)
    };

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly List<DailyNewsItemSnapshot> _activeItems = [];
    private readonly List<NewsItemVisual> _itemVisuals = [];
    private readonly Bitmap?[] _newsBitmaps = new Bitmap?[MaxDisplayItemCount];

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private string _channelType = IfengNewsChannelTypes.Comprehensive;
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private bool _isNightVisual = true;

    private sealed record NewsItemVisual(
        Border Host,
        Grid RowGrid,
        TextBlock TitleTextBlock,
        Border ImageHost,
        Image ImageControl);

    public IfengNewsWidget()
    {
        InitializeComponent();

        BrandTextBlock.FontFamily = MiSansFontFamily;
        NewsItem1TextBlock.FontFamily = MiSansFontFamily;
        NewsItem2TextBlock.FontFamily = MiSansFontFamily;
        NewsItem3TextBlock.FontFamily = MiSansFontFamily;
        NewsItem4TextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

        _itemVisuals.Add(new NewsItemVisual(NewsItem1Host, NewsItem1Grid, NewsItem1TextBlock, NewsItem1ImageHost, NewsItem1Image));
        _itemVisuals.Add(new NewsItemVisual(NewsItem2Host, NewsItem2Grid, NewsItem2TextBlock, NewsItem2ImageHost, NewsItem2Image));
        _itemVisuals.Add(new NewsItemVisual(NewsItem3Host, NewsItem3Grid, NewsItem3TextBlock, NewsItem3ImageHost, NewsItem3Image));
        _itemVisuals.Add(new NewsItemVisual(NewsItem4Host, NewsItem4Grid, NewsItem4TextBlock, NewsItem4ImageHost, NewsItem4Image));

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
            _ = RefreshNewsAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        _recommendationService.ClearCache();
        ApplyAutoRefreshSettings();
        if (_isAttached)
        {
            _ = RefreshNewsAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRefreshSettings();
        UpdateRefreshButtonState();
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

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _isNightVisual = ResolveNightMode();
        UpdateAdaptiveLayout();
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
        CardBorder.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#1B2129") : Color.Parse("#FCFCFD"));
        RootBorder.BorderBrush = new SolidColorBrush(_isNightVisual ? Color.Parse("#33FFFFFF") : Color.Parse("#00000000"));

        BrandTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));

        RefreshButton.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#EFF1F5"));
        RefreshGlyphIcon.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5E6671"));

        foreach (var visual in _itemVisuals)
        {
            visual.Host.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#F7F8FA"));
            visual.TitleTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));
        }

        StatusTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#6A6F77"));
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshNewsAsync(forceRefresh: true);
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        await RefreshNewsAsync(forceRefresh: true);
        e.Handled = true;
    }

    private void OnNewsItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Border host ||
            host.Tag is null ||
            !int.TryParse(host.Tag.ToString(), out var index) ||
            index < 0 ||
            index >= _activeItems.Count)
        {
            return;
        }

        TryOpenUrl(_activeItems[index].Url);
        e.Handled = true;
    }

    private async Task RefreshNewsAsync(bool forceRefresh)
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateLanguageCode();
        UpdateRefreshButtonState();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var query = new IfengNewsQuery(
                Locale: _languageCode,
                ItemCount: MaxDisplayItemCount,
                ChannelType: _channelType,
                ForceRefresh: forceRefresh);
            var result = await _recommendationService.GetIfengNewsAsync(query, cts.Token);
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
        BrandTextBlock.Text = L("ifeng.widget.brand", "凤凰网新闻");
        ToolTip.SetTip(RefreshButton, L("ifeng.widget.refresh_tooltip", "刷新"));

        _activeItems.Clear();
        foreach (var item in snapshot.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Url))
            {
                continue;
            }

            _activeItems.Add(item);
            if (_activeItems.Count >= MaxDisplayItemCount)
            {
                break;
            }
        }

        var fallbackText = L("ifeng.widget.fallback_item", "暂无新闻");
        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            visual.Host.IsVisible = true;
            visual.TitleTextBlock.Text = i < _activeItems.Count
                ? NormalizeCompactText(_activeItems[i].Title)
                : fallbackText;
            SetNewsBitmap(i, null);
        }

        StatusTextBlock.IsVisible = false;
        UpdateInteractionState();
        UpdateAdaptiveLayout();

        var tasks = Enumerable.Range(0, MaxDisplayItemCount)
            .Select(index => TryDownloadBitmapAsync(
                index < _activeItems.Count ? _activeItems[index].ImageUrl : null,
                cancellationToken))
            .ToArray();
        var bitmaps = await Task.WhenAll(tasks);
        if (cancellationToken.IsCancellationRequested || !_isAttached)
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap?.Dispose();
            }

            return;
        }

        for (var i = 0; i < bitmaps.Length; i++)
        {
            SetNewsBitmap(i, bitmaps[i]);
        }
    }

    private void ApplyLoadingState()
    {
        BrandTextBlock.Text = L("ifeng.widget.brand", "凤凰网新闻");
        ToolTip.SetTip(RefreshButton, L("ifeng.widget.refresh_tooltip", "刷新"));

        _activeItems.Clear();
        var loadingText = L("ifeng.widget.loading_item", "加载中...");
        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            visual.Host.IsVisible = true;
            visual.TitleTextBlock.Text = loadingText;
            SetNewsBitmap(i, null);
        }

        StatusTextBlock.Text = L("ifeng.widget.loading", "加载中...");
        StatusTextBlock.IsVisible = true;
        UpdateInteractionState();
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        BrandTextBlock.Text = L("ifeng.widget.brand", "凤凰网新闻");
        ToolTip.SetTip(RefreshButton, L("ifeng.widget.refresh_tooltip", "刷新"));

        _activeItems.Clear();
        var fallbackText = L("ifeng.widget.fallback_item", "暂无新闻");
        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            visual.Host.IsVisible = true;
            visual.TitleTextBlock.Text = fallbackText;
            SetNewsBitmap(i, null);
        }

        StatusTextBlock.Text = L("ifeng.widget.fetch_failed", "新闻获取失败");
        StatusTextBlock.IsVisible = true;
        UpdateInteractionState();
        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var softScale = Math.Clamp(scale, 0.80, 1.32);
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(32 * softScale, 16, 46));
        CardBorder.CornerRadius = new CornerRadius(Math.Clamp(32 * softScale, 16, 46));

        var horizontalPadding = Math.Clamp(14 * softScale, 8, 20);
        var verticalPadding = Math.Clamp(14 * softScale, 8, 20);
        CardBorder.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

        var rowSpacing = Math.Clamp(8 * softScale, 4, 12);
        ContentGrid.RowSpacing = rowSpacing;
        HeaderGrid.ColumnSpacing = Math.Clamp(10 * softScale, 6, 16);

        var innerWidth = Math.Max(150, totalWidth - horizontalPadding * 2d);
        var innerHeight = Math.Max(160, totalHeight - verticalPadding * 2d);
        var availableRowsHeight = Math.Max(120, innerHeight - rowSpacing * 4d);
        var headerHeight = Math.Clamp(availableRowsHeight * 0.16, 24, 54);
        var itemHeight = Math.Max(32, (availableRowsHeight - headerHeight) / 4d);

        if (ContentGrid.RowDefinitions.Count >= 5)
        {
            ContentGrid.RowDefinitions[0].Height = new GridLength(headerHeight);
            for (var i = 1; i <= 4; i++)
            {
                ContentGrid.RowDefinitions[i].Height = new GridLength(itemHeight);
            }
        }

        BrandTextBlock.FontSize = Math.Clamp(headerHeight * 0.62, 14, 30);

        var refreshSize = Math.Clamp(headerHeight * 0.84, 22, 44);
        RefreshButton.Width = refreshSize;
        RefreshButton.Height = refreshSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshSize / 2d);
        RefreshGlyphIcon.FontSize = Math.Clamp(refreshSize * 0.44, 10, 20);

        var imageWidth = Math.Clamp(innerWidth * 0.27, 82, 176);
        var imageHeight = Math.Clamp(imageWidth * 0.56, 46, 98);
        var columnGap = Math.Clamp(itemHeight * 0.20, 6, 14);
        var rowPadding = Math.Clamp(itemHeight * 0.08, 1, 5);
        var textWidth = Math.Max(84, innerWidth - imageWidth - columnGap);
        var titleFont = Math.Clamp(itemHeight * 0.32, 12, 24);

        foreach (var visual in _itemVisuals)
        {
            visual.Host.Padding = new Thickness(0, rowPadding, 0, rowPadding);
            visual.RowGrid.ColumnSpacing = columnGap;
            if (visual.RowGrid.ColumnDefinitions.Count > 1)
            {
                visual.RowGrid.ColumnDefinitions[1].Width = new GridLength(imageWidth);
            }

            visual.ImageHost.Width = imageWidth;
            visual.ImageHost.Height = imageHeight;
            visual.ImageHost.CornerRadius = new CornerRadius(Math.Clamp(imageHeight * 0.15, 8, 16));

            visual.TitleTextBlock.MaxWidth = textWidth;
            visual.TitleTextBlock.FontSize = titleFont;
            visual.TitleTextBlock.LineHeight = titleFont * 1.12;
            visual.TitleTextBlock.MinHeight = visual.TitleTextBlock.LineHeight * 2;
            visual.TitleTextBlock.MaxLines = 2;
        }

        StatusTextBlock.FontSize = Math.Clamp(titleFont, 10, 20);
        ApplyNightModeVisual();
    }

    private void UpdateInteractionState()
    {
        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            var enabled = i < _activeItems.Count && !string.IsNullOrWhiteSpace(_activeItems[i].Url);
            visual.Host.IsHitTestVisible = enabled;
            visual.Host.Opacity = enabled ? 1.0 : 0.68;
            visual.Host.Cursor = enabled
                ? new Cursor(StandardCursorType.Hand)
                : new Cursor(StandardCursorType.Arrow);
        }
    }

    private void UpdateRefreshButtonState()
    {
        var enabled = _isAttached && !_isRefreshing;
        RefreshButton.IsEnabled = enabled;
        RefreshButton.Opacity = enabled ? 1.0 : 0.65;
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
        var intervalMinutes = 20;
        var channelType = IfengNewsChannelTypes.Comprehensive;

        try
        {
            var snapshot = _componentSettingsService.Load();
            enabled = snapshot.IfengNewsAutoRefreshEnabled;
            intervalMinutes = NormalizeAutoRefreshIntervalMinutes(snapshot.IfengNewsAutoRefreshIntervalMinutes);
            channelType = IfengNewsChannelTypes.Normalize(snapshot.IfengNewsChannelType);
        }
        catch
        {
            // Keep fallback defaults.
        }

        _autoRefreshEnabled = enabled;
        _channelType = channelType;
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
            return 20;
        }

        if (SupportedAutoRefreshIntervalsMinutes.Contains(minutes))
        {
            return minutes;
        }

        return SupportedAutoRefreshIntervalsMinutes
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(20);
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

    private void TryOpenUrl(string? rawUrl)
    {
        var normalizedUrl = NormalizeHttpUrl(rawUrl);
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

        var visual = _itemVisuals[index];
        var oldBitmap = _newsBitmaps[index];
        if (ReferenceEquals(visual.ImageControl.Source, oldBitmap))
        {
            visual.ImageControl.Source = null;
        }

        oldBitmap?.Dispose();
        _newsBitmaps[index] = bitmap;
        visual.ImageControl.Source = bitmap;
    }

    private void DisposeNewsBitmaps()
    {
        for (var i = 0; i < _newsBitmaps.Length; i++)
        {
            SetNewsBitmap(i, null);
        }
    }

    private double ResolveScale()
    {
        var expectedWidth = _currentCellSize * BaseWidthCells;
        var expectedHeight = _currentCellSize * BaseHeightCells;
        if (expectedWidth <= 0 || expectedHeight <= 0)
        {
            return 1d;
        }

        var actualWidth = Bounds.Width > 1 ? Bounds.Width : expectedWidth;
        var actualHeight = Bounds.Height > 1 ? Bounds.Height : expectedHeight;
        var scaleX = actualWidth / expectedWidth;
        var scaleY = actualHeight / expectedHeight;
        return Math.Clamp(Math.Min(scaleX, scaleY), 0.72, 2.4);
    }

    private static string NormalizeCompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(text.Trim(), " ");
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
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
}
