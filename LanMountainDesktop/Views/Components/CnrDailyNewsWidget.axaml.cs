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
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class CnrDailyNewsWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
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
    private static readonly IReadOnlyList<int> SupportedAutoRotateIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(30)
    };

    private readonly bool _isDesignModePreview = Design.IsDesignMode;
    private LanMountainDesktop.PluginSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
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
    // 删除 _isNightVisual 字段，不再需要手动管理主题

    public CnrDailyNewsWidget()
    {
        InitializeComponent();

        if (_isDesignModePreview)
        {
            ApplyDesignTimePreview();
            return;
        }

        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshButton.Click += OnRefreshButtonClick;
        NewsItem1Grid.PointerPressed += OnNewsItem1PointerPressed;
        NewsItem2Grid.PointerPressed += OnNewsItem2PointerPressed;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        UpdateLanguageCode();
        ApplyAutoRotateSettings();
        ApplyLoadingState();
        UpdateRefreshButtonState();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        // 不再需要复杂的自适应逻辑，使用固定标准尺寸
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

    // 删除 OnSizeChanged 和 OnActualThemeVariantChanged，不再需要

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

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
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenNewsUrl(0);
        e.Handled = true;
    }

    private void OnNewsItem2PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenNewsUrl(1);
        e.Handled = true;
    }

    private void OnExtraNewsItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

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
    }

    private void ApplyDesignTimePreview()
    {
        _activeNewsItems =
        [
            new DailyNewsItemSnapshot(
                "LanMountain preview mode now shows mocked widget content in Rider.",
                null,
                "https://example.com/news/preview-1",
                null,
                "09:30"),
            new DailyNewsItemSnapshot(
                "Weather, artwork, and plugin market cards render without live network calls.",
                null,
                "https://example.com/news/preview-2",
                null,
                "09:10"),
            new DailyNewsItemSnapshot(
                "Design-time mocks make isolated widget layout tuning much faster.",
                null,
                "https://example.com/news/preview-3",
                null,
                "08:55")
        ];

        _newsUrls.Clear();
        foreach (var item in _activeNewsItems)
        {
            _newsUrls.Add(item.Url);
        }

        UpdateHotHeadlineText(_activeNewsItems[0].Title);
        News2TitleTextBlock.Text = NormalizeCompactText(_activeNewsItems[1].Title);
        StatusTextBlock.Text = string.Empty;
        StatusTextBlock.IsVisible = false;

        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
        RenderExtraNewsRows(_activeNewsItems.Skip(2).ToArray());
        UpdateNewsInteractionState();

        RefreshButton.IsEnabled = false;
        RefreshButton.Opacity = 1.0;
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
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                LineHeight = 22,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                IsHitTestVisible = false
            };

            // 使用动态资源绑定文本颜色
            textBlock.Bind(TextBlock.ForegroundProperty,
                new Avalonia.Data.Binding("TextFillColorPrimaryBrush")
                {
                    Source = Application.Current!.Resources
                });

            var imageHost = new Border
            {
                Width = 140,
                Height = 80,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                IsHitTestVisible = false
            };

            // 使用动态资源绑定背景色
            imageHost.Bind(Border.BackgroundProperty,
                new Avalonia.Data.Binding("CardBackgroundSecondaryBrush")
                {
                    Source = Application.Current!.Resources
                });

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

    private void UpdateRefreshButtonState()
    {
        RefreshButton.IsEnabled = !_isRefreshing && _isAttached;
        RefreshButton.Opacity = _isAttached ? 1.0 : 0.6;
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

        if (bitmap != null)
        {
            InvalidateMeasure();
        }
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
            var snapshot = _appSettingsService.Load();
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
            var snapshot = _componentSettingsService.Load();
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

    private static string NormalizeCompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(text.Trim(), " ");
    }
}
