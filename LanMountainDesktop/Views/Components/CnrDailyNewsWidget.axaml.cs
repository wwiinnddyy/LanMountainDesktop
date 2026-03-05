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
using Avalonia.Controls.Documents;
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
    private static readonly int[] SupportedAutoRotateIntervalsMinutes = [5, 10, 40, 60, 720, 1440];

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(30)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly Bitmap?[] _newsBitmaps = new Bitmap?[2];
    private readonly List<string?> _newsUrls = [];
    private readonly List<ExtraNewsRowVisual> _extraNewsRows = [];
    private IReadOnlyList<DailyNewsItemSnapshot> _activeNewsItems = [];
    private int _renderedNewsCount = 2;

    private sealed class ExtraNewsRowVisual
    {
        public ExtraNewsRowVisual(
            Grid rootGrid,
            TextBlock titleTextBlock,
            Border imageHost,
            Image imageControl,
            int newsIndex)
        {
            RootGrid = rootGrid;
            TitleTextBlock = titleTextBlock;
            ImageHost = imageHost;
            ImageControl = imageControl;
            NewsIndex = newsIndex;
        }

        public Grid RootGrid { get; }

        public TextBlock TitleTextBlock { get; }

        public Border ImageHost { get; }

        public Image ImageControl { get; }

        public int NewsIndex { get; }

        public Bitmap? Bitmap { get; set; }
    }

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRotateEnabled = true;

    public CnrDailyNewsWidget()
    {
        InitializeComponent();

        BrandPrimaryTextBlock.FontFamily = MiSansFontFamily;
        BrandSecondaryTextBlock.FontFamily = MiSansFontFamily;
        RefreshLabelTextBlock.FontFamily = MiSansFontFamily;
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
        ApplyAutoRotateSettings();
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
        ApplyAutoRotateSettings();
        if (_isAttached)
        {
            _ = RefreshNewsAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRotateSettings();
        UpdateRefreshButtonState();
        _ = RefreshNewsAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        DisposeNewsBitmaps();
        ClearExtraNewsRows();
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
        await RefreshNewsAsync(forceRefresh: true);
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

    private void OnExtraNewsItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control control ||
            control.Tag is not int index)
        {
            return;
        }

        TryOpenNewsUrl(index);
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
                ItemCount: ResolveDesiredNewsItemCount(),
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
        _activeNewsItems = items;

        var item1 = items.Length > 0 ? items[0] : null;
        var item2 = items.Length > 1 ? items[1] : null;

        UpdateHotHeadlineText(item1?.Title);
        News2TitleTextBlock.Text = NormalizeCompactText(item2?.Title);

        _newsUrls.Clear();
        foreach (var item in items)
        {
            _newsUrls.Add(NormalizeHttpUrl(item.Url));
        }

        RenderExtraNewsRows([]);
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
        _activeNewsItems = [];
        _newsUrls.Clear();
        UpdateHotHeadlineText(L("cnrnews.widget.loading_title", "Loading headlines"));
        News2TitleTextBlock.Text = L("cnrnews.widget.loading_subtitle", "Please wait");
        StatusTextBlock.Text = L("cnrnews.widget.loading", "Loading...");
        StatusTextBlock.IsVisible = true;
        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
        RenderExtraNewsRows([]);
        UpdateNewsInteractionState();
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        _activeNewsItems = [];
        _newsUrls.Clear();
        News1TitleTextBlock.Inlines = null;
        News1TitleTextBlock.Text = L("cnrnews.widget.fallback_title", "CNR news is temporarily unavailable");
        News2TitleTextBlock.Text = L("cnrnews.widget.fallback_subtitle", "Tap refresh and try again");
        StatusTextBlock.Text = L("cnrnews.widget.fetch_failed", "News fetch failed");
        StatusTextBlock.IsVisible = true;
        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
        RenderExtraNewsRows([]);
        UpdateNewsInteractionState();
        UpdateAdaptiveLayout();
    }

    private int ResolveDesiredNewsItemCount()
    {
        return 2;
    }

    private void UpdateHotHeadlineText(string? title)
    {
        var normalizedTitle = NormalizeCompactText(title);
        var hotLabel = L("cnrnews.widget.hot_label", "Hot");
        if (News1TitleTextBlock.Inlines is null)
        {
            News1TitleTextBlock.Text = $"{hotLabel} | {normalizedTitle}";
            return;
        }

        News1TitleTextBlock.Inlines.Clear();
        News1TitleTextBlock.Inlines.Add(new Run($"{hotLabel} | ")
        {
            Foreground = new SolidColorBrush(Color.Parse("#D6272E")),
            FontWeight = FontWeight.SemiBold
        });
        News1TitleTextBlock.Inlines.Add(new Run(normalizedTitle)
        {
            Foreground = new SolidColorBrush(Color.Parse("#202327")),
            FontWeight = FontWeight.SemiBold
        });
    }

    private void RenderExtraNewsRows(IReadOnlyList<DailyNewsItemSnapshot> extraItems)
    {
        ClearExtraNewsRows();
        if (extraItems.Count == 0)
        {
            ExtraNewsItemsPanel.IsVisible = false;
            _renderedNewsCount = 2;
            return;
        }

        for (var i = 0; i < extraItems.Count; i++)
        {
            var item = extraItems[i];
            var itemIndex = i + 2;
            var rowGrid = new Grid
            {
                ColumnSpacing = 12,
                Tag = itemIndex,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsHitTestVisible = true
            };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            rowGrid.PointerPressed += OnExtraNewsItemPointerPressed;

            var textBlock = new TextBlock
            {
                Text = NormalizeCompactText(item.Title),
                Foreground = new SolidColorBrush(Color.Parse("#202327")),
                FontFamily = MiSansFontFamily,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                IsHitTestVisible = false
            };

            var imageHost = new Border
            {
                Width = 160,
                Height = 90,
                CornerRadius = new CornerRadius(16),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.Parse("#E6E6E6")),
                IsHitTestVisible = false
            };
            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false
            };
            imageHost.Child = image;
            Grid.SetColumn(imageHost, 1);

            rowGrid.Children.Add(textBlock);
            rowGrid.Children.Add(imageHost);
            ExtraNewsItemsPanel.Children.Add(rowGrid);
            _extraNewsRows.Add(new ExtraNewsRowVisual(rowGrid, textBlock, imageHost, image, itemIndex));
        }

        ExtraNewsItemsPanel.IsVisible = true;
        _renderedNewsCount = 2 + extraItems.Count;
    }

    private void ClearExtraNewsRows()
    {
        foreach (var row in _extraNewsRows)
        {
            row.RootGrid.PointerPressed -= OnExtraNewsItemPointerPressed;
            if (ReferenceEquals(row.ImageControl.Source, row.Bitmap))
            {
                row.ImageControl.Source = null;
            }

            row.Bitmap?.Dispose();
            row.Bitmap = null;
        }

        _extraNewsRows.Clear();
        ExtraNewsItemsPanel.Children.Clear();
    }

    private void SetExtraNewsBitmap(int rowIndex, Bitmap? bitmap)
    {
        if (rowIndex < 0 || rowIndex >= _extraNewsRows.Count)
        {
            bitmap?.Dispose();
            return;
        }

        var row = _extraNewsRows[rowIndex];
        if (ReferenceEquals(row.ImageControl.Source, row.Bitmap))
        {
            row.ImageControl.Source = null;
        }

        row.Bitmap?.Dispose();
        row.Bitmap = bitmap;
        row.ImageControl.Source = bitmap;
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));
        RootBorder.Padding = new Thickness(0);

        CardBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));
        CardBorder.Padding = new Thickness(
            Math.Clamp(16 * scale, 8, 24),
            Math.Clamp(14 * scale, 7, 22),
            Math.Clamp(16 * scale, 8, 24),
            Math.Clamp(14 * scale, 7, 22));

        var headlineFont = Math.Clamp(24 * scale, 12, 34);
        BrandPrimaryTextBlock.FontSize = headlineFont;
        BrandSecondaryTextBlock.FontSize = headlineFont;

        var refreshHeight = Math.Clamp(42 * scale, 24, 52);
        var refreshWidth = Math.Clamp(116 * scale, 76, 152);
        RefreshButton.Height = refreshHeight;
        RefreshButton.Width = refreshWidth;
        RefreshButton.CornerRadius = new CornerRadius(refreshHeight / 2d);
        RefreshGlyphIcon.FontSize = Math.Clamp(19 * scale, 11, 24);
        RefreshLabelTextBlock.FontSize = Math.Clamp(22 * scale, 11, 29);

        var imageWidth = Math.Clamp(totalWidth * 0.20, 60, 170);
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

        var availableTextWidth = Math.Max(
            84,
            totalWidth - imageWidth - columnGap - Math.Clamp(20 * scale, 10, 32));
        News1TitleTextBlock.MaxWidth = availableTextWidth;
        News2TitleTextBlock.MaxWidth = availableTextWidth;

        var newsFont = Math.Clamp(21 * scale, 10.5, 28);
        News1TitleTextBlock.FontSize = newsFont;
        News2TitleTextBlock.FontSize = newsFont;
        var mainNewsLineHeight = newsFont * 1.14;
        News1TitleTextBlock.LineHeight = mainNewsLineHeight;
        News2TitleTextBlock.LineHeight = mainNewsLineHeight;
        var mainNewsMinHeight = mainNewsLineHeight * 2;
        News1TitleTextBlock.MinHeight = mainNewsMinHeight;
        News2TitleTextBlock.MinHeight = mainNewsMinHeight;
        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 9, 24);
        News1TitleTextBlock.MaxLines = 2;
        News2TitleTextBlock.MaxLines = 2;

        foreach (var row in _extraNewsRows)
        {
            row.RootGrid.ColumnSpacing = columnGap;
            if (row.RootGrid.ColumnDefinitions.Count > 1)
            {
                row.RootGrid.ColumnDefinitions[1].Width = new GridLength(imageWidth);
            }

            row.ImageHost.Width = imageWidth;
            row.ImageHost.Height = imageHeight;
            row.ImageHost.CornerRadius = new CornerRadius(Math.Clamp(16 * scale, 8, 22));

            row.TitleTextBlock.MaxWidth = availableTextWidth;
            row.TitleTextBlock.FontSize = Math.Clamp(19 * scale, 10, 25);
            row.TitleTextBlock.LineHeight = row.TitleTextBlock.FontSize * 1.12;
            row.TitleTextBlock.MinHeight = row.TitleTextBlock.LineHeight * 2;
            row.TitleTextBlock.MaxLines = 2;
        }

        ExtraNewsItemsPanel.Spacing = Math.Clamp(6 * scale, 3, 10);
    }

    private void UpdateRefreshButtonState()
    {
        RefreshButton.IsEnabled = !_isRefreshing;
        RefreshButton.Opacity = _isAttached ? 1.0 : 0.85;
        RefreshGlyphIcon.Opacity = _isRefreshing ? 0.56 : 1.0;
        RefreshLabelTextBlock.Opacity = _isRefreshing ? 0.56 : 1.0;
    }

    private void UpdateNewsInteractionState()
    {
        var item1Enabled = _newsUrls.Count > 0 && !string.IsNullOrWhiteSpace(_newsUrls[0]);
        var item2Enabled = _newsUrls.Count > 1 && !string.IsNullOrWhiteSpace(_newsUrls[1]);

        NewsItem1Grid.IsHitTestVisible = item1Enabled;
        NewsItem2Grid.IsHitTestVisible = item2Enabled;
        NewsItem1Grid.Opacity = item1Enabled ? 1.0 : 0.72;
        NewsItem2Grid.Opacity = item2Enabled ? 1.0 : 0.72;

        foreach (var row in _extraNewsRows)
        {
            var index = row.NewsIndex;
            var enabled = index >= 0 && index < _newsUrls.Count && !string.IsNullOrWhiteSpace(_newsUrls[index]);
            row.RootGrid.IsHitTestVisible = enabled;
            row.RootGrid.Opacity = enabled ? 1.0 : 0.72;
            row.RootGrid.Cursor = enabled
                ? new Cursor(StandardCursorType.Hand)
                : new Cursor(StandardCursorType.Arrow);
        }
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
        if (index < 0 || index >= _newsUrls.Count)
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

    private void ApplyAutoRotateSettings()
    {
        var enabled = true;
        var intervalMinutes = 60;

        try
        {
            var snapshot = _settingsService.Load();
            enabled = snapshot.CnrDailyNewsAutoRotateEnabled;
            intervalMinutes = NormalizeAutoRotateIntervalMinutes(snapshot.CnrDailyNewsAutoRotateIntervalMinutes);
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

