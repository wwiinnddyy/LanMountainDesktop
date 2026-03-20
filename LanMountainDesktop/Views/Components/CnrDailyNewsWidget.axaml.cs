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
using LanMountainDesktop.DesktopComponents.Runtime;
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
    private static readonly IReadOnlyList<int> SupportedAutoRotateIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(30)
    };

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
    private bool _isNightVisual = true;

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
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

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

        BrandPrimaryTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));
        BrandSecondaryTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#6A6F77"));

        RefreshButton.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#EFF1F5"));
        RefreshGlyphIcon.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5E6671"));
        RefreshLabelTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5E6671"));

        News1TitleTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));
        News2TitleTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));

        StatusTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#6A6F77"));
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
        var primaryForeground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));
        if (News1TitleTextBlock.Inlines is null)
        {
            News1TitleTextBlock.Text = $"{hotLabel} | {normalizedTitle}";
            News1TitleTextBlock.Foreground = primaryForeground;
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
            Foreground = primaryForeground,
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
                Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327")),
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
                CornerRadius = ComponentChromeCornerRadiusHelper.Scale(16, 8, 22),
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

        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(34 * scale, 16, 52);
        RootBorder.Padding = ComponentChromeCornerRadiusHelper.SafeThickness(
            10 * scale,
            8 * scale,
            null,
            0.45d);

        CardBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(34 * scale, 16, 52);
        CardBorder.Padding = ComponentChromeCornerRadiusHelper.SafeThickness(
            Math.Clamp(16 * scale, 8, 24),
            Math.Clamp(14 * scale, 7, 22),
            null,
            0.55d);

        var rootPadding = RootBorder.Padding;
        var cardPadding = CardBorder.Padding;
        var contentWidth = Math.Max(
            150,
            totalWidth - rootPadding.Left - rootPadding.Right - cardPadding.Left - cardPadding.Right);

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

        var imageWidth = Math.Clamp(contentWidth * 0.20, 60, 170);
        var imageHeight = Math.Clamp(imageWidth * 0.56, 38, 94);
        News1ImageHost.Width = imageWidth;
        News1ImageHost.Height = imageHeight;
        News2ImageHost.Width = imageWidth;
        News2ImageHost.Height = imageHeight;
        News1ImageHost.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(16 * scale, 8, 22);
        News2ImageHost.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(16 * scale, 8, 22);

        var columnGap = Math.Clamp(12 * scale, 6, 18);
        NewsItem1Grid.ColumnSpacing = columnGap;
        NewsItem2Grid.ColumnSpacing = columnGap;
        NewsItem1Grid.ColumnDefinitions[1].Width = new GridLength(imageWidth);
        NewsItem2Grid.ColumnDefinitions[1].Width = new GridLength(imageWidth);

        var availableTextWidth = Math.Max(
            84,
            contentWidth - imageWidth - columnGap - Math.Clamp(20 * scale, 10, 32));
        News1TitleTextBlock.MaxWidth = availableTextWidth;
        News2TitleTextBlock.MaxWidth = availableTextWidth;

        var newsFont = Math.Clamp(21 * scale, 10.5, 28);
        var newsHeightBudget = Math.Max(28, imageHeight + columnGap * 2d);
        var news1Layout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            News1TitleTextBlock.Text,
            availableTextWidth,
            newsHeightBudget,
            minLines: 1,
            maxLines: ComponentTypographyLayoutService.CountTextDisplayUnits(News1TitleTextBlock.Text) > 30 ? 2 : 1,
            minFontSize: Math.Clamp(newsFont * 0.72, 10.5, 18),
            maxFontSize: newsFont,
            weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Bold },
            lineHeightFactor: 1.14d,
            fontFamily: MiSansFontFamily);
        var news2Layout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            News2TitleTextBlock.Text,
            availableTextWidth,
            newsHeightBudget,
            minLines: 1,
            maxLines: ComponentTypographyLayoutService.CountTextDisplayUnits(News2TitleTextBlock.Text) > 30 ? 2 : 1,
            minFontSize: Math.Clamp(newsFont * 0.72, 10.5, 18),
            maxFontSize: newsFont,
            weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Bold },
            lineHeightFactor: 1.14d,
            fontFamily: MiSansFontFamily);
        News1TitleTextBlock.FontSize = news1Layout.FontSize;
        News1TitleTextBlock.LineHeight = news1Layout.LineHeight;
        News1TitleTextBlock.MinHeight = news1Layout.LineHeight * news1Layout.MaxLines;
        News1TitleTextBlock.MaxLines = news1Layout.MaxLines;
        News1TitleTextBlock.FontWeight = news1Layout.Weight;
        News2TitleTextBlock.FontSize = news2Layout.FontSize;
        News2TitleTextBlock.LineHeight = news2Layout.LineHeight;
        News2TitleTextBlock.MinHeight = news2Layout.LineHeight * news2Layout.MaxLines;
        News2TitleTextBlock.MaxLines = news2Layout.MaxLines;
        News2TitleTextBlock.FontWeight = news2Layout.Weight;
        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 9, 24);

        foreach (var row in _extraNewsRows)
        {
            row.RootGrid.ColumnSpacing = columnGap;
            if (row.RootGrid.ColumnDefinitions.Count > 1)
            {
                row.RootGrid.ColumnDefinitions[1].Width = new GridLength(imageWidth);
            }

            row.ImageHost.Width = imageWidth;
            row.ImageHost.Height = imageHeight;
            row.ImageHost.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(16 * scale, 8, 22);

            var rowTitleLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                row.TitleTextBlock.Text,
                availableTextWidth,
                Math.Max(32, imageHeight + columnGap),
                minLines: 1,
                maxLines: ComponentTypographyLayoutService.CountTextDisplayUnits(row.TitleTextBlock.Text) > 28 ? 2 : 1,
                minFontSize: Math.Clamp(19 * scale, 10, 16),
                maxFontSize: Math.Clamp(19 * scale, 10, 25),
                weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Bold },
                lineHeightFactor: 1.12d,
                fontFamily: MiSansFontFamily);
            row.TitleTextBlock.MaxWidth = availableTextWidth;
            row.TitleTextBlock.FontSize = rowTitleLayout.FontSize;
            row.TitleTextBlock.LineHeight = rowTitleLayout.LineHeight;
            row.TitleTextBlock.MinHeight = rowTitleLayout.LineHeight * rowTitleLayout.MaxLines;
            row.TitleTextBlock.MaxLines = rowTitleLayout.MaxLines;
            row.TitleTextBlock.FontWeight = rowTitleLayout.Weight;
        }

        ExtraNewsItemsPanel.Spacing = Math.Clamp(6 * scale, 3, 10);

        ApplyNightModeVisual();
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
