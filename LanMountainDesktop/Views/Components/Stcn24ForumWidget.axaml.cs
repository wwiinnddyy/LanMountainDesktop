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

public partial class Stcn24ForumWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private static readonly HttpClient AvatarHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private const string AvatarRequestUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 4;
    private const int BaseDisplayItemCount = 4;
    private const int MaxDisplayItemCount = 8;
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(20)
    };

    private LanMountainDesktop.PluginSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly List<Stcn24ForumPostItemSnapshot> _activeItems = [];
    private readonly List<ForumItemVisual> _itemVisuals = [];
    private readonly Bitmap?[] _avatarBitmaps = new Bitmap?[MaxDisplayItemCount];

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private string _sourceType = Stcn24ForumSourceTypes.LatestCreated;
    private double _currentCellSize = BaseCellSize;
    private int _visibleItemCount = BaseDisplayItemCount;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private bool _isNightVisual = true;

    private sealed record ForumItemVisual(
        Border Host,
        Grid RowGrid,
        Border AvatarHost,
        Image AvatarImage,
        TextBlock AvatarFallbackText,
        TextBlock TitleTextBlock);

    public Stcn24ForumWidget()
    {
        InitializeComponent();

        _itemVisuals.Add(new ForumItemVisual(
            PostItem1Host,
            PostItem1Grid,
            PostItem1AvatarHost,
            PostItem1AvatarImage,
            PostItem1AvatarFallbackText,
            PostItem1TitleTextBlock));
        _itemVisuals.Add(new ForumItemVisual(
            PostItem2Host,
            PostItem2Grid,
            PostItem2AvatarHost,
            PostItem2AvatarImage,
            PostItem2AvatarFallbackText,
            PostItem2TitleTextBlock));
        _itemVisuals.Add(new ForumItemVisual(
            PostItem3Host,
            PostItem3Grid,
            PostItem3AvatarHost,
            PostItem3AvatarImage,
            PostItem3AvatarFallbackText,
            PostItem3TitleTextBlock));
        _itemVisuals.Add(new ForumItemVisual(
            PostItem4Host,
            PostItem4Grid,
            PostItem4AvatarHost,
            PostItem4AvatarImage,
            PostItem4AvatarFallbackText,
            PostItem4TitleTextBlock));
        _itemVisuals.Add(new ForumItemVisual(
            PostItem5Host,
            PostItem5Grid,
            PostItem5AvatarHost,
            PostItem5AvatarImage,
            PostItem5AvatarFallbackText,
            PostItem5TitleTextBlock));
        _itemVisuals.Add(new ForumItemVisual(
            PostItem6Host,
            PostItem6Grid,
            PostItem6AvatarHost,
            PostItem6AvatarImage,
            PostItem6AvatarFallbackText,
            PostItem6TitleTextBlock));
        _itemVisuals.Add(new ForumItemVisual(
            PostItem7Host,
            PostItem7Grid,
            PostItem7AvatarHost,
            PostItem7AvatarImage,
            PostItem7AvatarFallbackText,
            PostItem7TitleTextBlock));
        _itemVisuals.Add(new ForumItemVisual(
            PostItem8Host,
            PostItem8Grid,
            PostItem8AvatarHost,
            PostItem8AvatarImage,
            PostItem8AvatarFallbackText,
            PostItem8TitleTextBlock));

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ApplyCellSize(_currentCellSize);
        UpdateLanguageCode();
        ApplyAutoRefreshSettings();
        ApplyLoadingState();
        UpdateInteractionState();
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
            _ = RefreshPostsAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        _recommendationService.ClearCache();
        ApplyAutoRefreshSettings();
        if (_isAttached)
        {
            _ = RefreshPostsAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRefreshSettings();

        _ = RefreshPostsAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        DisposeAvatarBitmaps();
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

        HeaderTitleTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));
        HeaderDot.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#FF6B6B") : Color.Parse("#FF4D4F"));

        RefreshButton.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#EFF1F5"));
        RefreshGlyphIcon.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5E6671"));

        foreach (var visual in _itemVisuals)
        {
            visual.Host.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#F7F8FA"));
            visual.AvatarHost.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#3D4451") : Color.Parse("#E7EBF4"));
            visual.AvatarFallbackText.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#4A5466"));
            visual.TitleTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));
        }

        StatusTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#6A6F77"));
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        await RefreshPostsAsync(forceRefresh: true);
        e.Handled = true;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshPostsAsync(forceRefresh: false);
    }

    private void OnPostItemPointerPressed(object? sender, PointerPressedEventArgs e)
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

    private async Task RefreshPostsAsync(bool forceRefresh)
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
            var query = new Stcn24ForumPostsQuery(
                Locale: _languageCode,
                ItemCount: _visibleItemCount,
                SourceType: _sourceType,
                ForceRefresh: forceRefresh);
            var result = await _recommendationService.GetStcn24ForumPostsAsync(query, cts.Token);
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

    private async Task ApplySnapshotAsync(Stcn24ForumPostsSnapshot snapshot, CancellationToken cancellationToken)
    {
        _activeItems.Clear();
        foreach (var item in snapshot.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Url))
            {
                continue;
            }

            _activeItems.Add(item);
            if (_activeItems.Count >= _visibleItemCount)
            {
                break;
            }
        }

        var fallbackItemText = L("stcn24.widget.fallback_item", "暂无帖子");
        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            var isRowVisible = i < _visibleItemCount;
            visual.Host.IsVisible = isRowVisible;
            if (!isRowVisible)
            {
                SetAvatarBitmap(i, null);
                continue;
            }

            if (i < _activeItems.Count)
            {
                var item = _activeItems[i];
                visual.TitleTextBlock.Text = NormalizeCompactText(item.Title);
                visual.AvatarFallbackText.Text = ResolveAvatarFallbackText(item.AuthorDisplayName);
            }
            else
            {
                visual.TitleTextBlock.Text = fallbackItemText;
                visual.AvatarFallbackText.Text = "?";
            }

            SetAvatarBitmap(i, null);
        }

        StatusTextBlock.IsVisible = false;
        UpdateInteractionState();
        UpdateAdaptiveLayout();

        var tasks = _activeItems
            .Take(_visibleItemCount)
            .Select(item => TryDownloadAvatarBitmapAsync(item.AuthorAvatarUrl, cancellationToken))
            .ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        var bitmaps = await Task.WhenAll(tasks);
        if (cancellationToken.IsCancellationRequested || !_isAttached)
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap?.Dispose();
            }

            return;
        }

        for (var i = 0; i < bitmaps.Length && i < _itemVisuals.Count; i++)
        {
            SetAvatarBitmap(i, bitmaps[i]);
        }
    }

    private void ApplyLoadingState()
    {
        _activeItems.Clear();
        StatusTextBlock.Text = L("stcn24.widget.loading", "加载中...");
        StatusTextBlock.IsVisible = true;

        var loadingText = L("stcn24.widget.loading_item", "加载中...");
        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            var isRowVisible = i < _visibleItemCount;
            visual.Host.IsVisible = isRowVisible;
            if (!isRowVisible)
            {
                SetAvatarBitmap(i, null);
                continue;
            }

            visual.TitleTextBlock.Text = loadingText;
            visual.AvatarFallbackText.Text = "?";
            SetAvatarBitmap(i, null);
        }

        UpdateInteractionState();
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        _activeItems.Clear();
        StatusTextBlock.Text = L("stcn24.widget.fetch_failed", "帖子获取失败");
        StatusTextBlock.IsVisible = true;

        var fallbackText = L("stcn24.widget.fallback_item", "暂无帖子");
        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            var isRowVisible = i < _visibleItemCount;
            visual.Host.IsVisible = isRowVisible;
            if (!isRowVisible)
            {
                SetAvatarBitmap(i, null);
                continue;
            }

            visual.TitleTextBlock.Text = fallbackText;
            visual.AvatarFallbackText.Text = "?";
            SetAvatarBitmap(i, null);
        }

        UpdateInteractionState();
        UpdateAdaptiveLayout();
    }

    private void UpdateInteractionState()
    {
        var enabledBackground = new SolidColorBrush(Color.Parse("#F7F8FA"));
        var disabledBackground = new SolidColorBrush(Color.Parse("#F2F3F5"));

        for (var i = 0; i < _itemVisuals.Count; i++)
        {
            var visual = _itemVisuals[i];
            var inVisibleRange = i < _visibleItemCount;
            visual.Host.IsVisible = inVisibleRange;
            var enabled = inVisibleRange &&
                          i < _activeItems.Count &&
                          !string.IsNullOrWhiteSpace(_activeItems[i].Url);
            visual.Host.IsHitTestVisible = enabled;
            visual.Host.Opacity = enabled ? 1.0 : 0.72;
            visual.Host.Cursor = enabled
                ? new Cursor(StandardCursorType.Hand)
                : new Cursor(StandardCursorType.Arrow);
            visual.Host.Background = enabled
                ? enabledBackground
                : disabledBackground;
        }
    }

    private void UpdateRefreshButtonState()
    {
        RefreshButton.IsEnabled = !_isRefreshing;
        RefreshButton.Opacity = _isRefreshing ? 0.58 : 1.0;
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

        try
        {
            var snapshot = _componentSettingsService.Load();
            _sourceType = Stcn24ForumSourceTypes.Normalize(snapshot.Stcn24ForumSourceType);
            enabled = snapshot.Stcn24ForumAutoRefreshEnabled;
            intervalMinutes = NormalizeAutoRefreshIntervalMinutes(snapshot.Stcn24ForumAutoRefreshIntervalMinutes);
        }
        catch
        {
            // Keep fallback defaults.
            _sourceType = Stcn24ForumSourceTypes.LatestCreated;
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

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var softScale = Math.Clamp(scale, 0.80, 1.40);
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var unifiedMainRectangle = ResolveUnifiedMainRectangle();
        RootBorder.CornerRadius = unifiedMainRectangle;
        CardBorder.CornerRadius = unifiedMainRectangle;
        CardBorder.Padding = new Thickness(
            Math.Clamp(12 * softScale, 8, 18),
            Math.Clamp(12 * softScale, 8, 18),
            Math.Clamp(12 * softScale, 8, 18),
            Math.Clamp(12 * softScale, 8, 18));

        var rowSpacing = Math.Clamp(6 * softScale, 3, 10);
        ContentGrid.RowSpacing = rowSpacing;
        HeaderGrid.ColumnSpacing = Math.Clamp(8 * softScale, 5, 12);

        HeaderDot.Width = Math.Clamp(8 * softScale, 5, 12);
        HeaderDot.Height = HeaderDot.Width;
        HeaderDot.CornerRadius = new CornerRadius(HeaderDot.Width / 2d);
        HeaderTitleTextBlock.FontSize = Math.Clamp(20 * softScale, 12, 28);

        var refreshSize = Math.Clamp(34 * softScale, 22, 42);
        RefreshButton.Width = refreshSize;
        RefreshButton.Height = refreshSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshSize / 2d);
        RefreshGlyphIcon.FontSize = Math.Clamp(16 * softScale, 10, 20);

        var innerWidth = Math.Max(100, totalWidth - CardBorder.Padding.Left - CardBorder.Padding.Right);
        var rowPaddingHorizontal = Math.Clamp(8 * softScale, 5, 14);
        var rowPaddingVertical = Math.Clamp(6 * softScale, 3, 10);
        var avatarSize = Math.Clamp(30 * softScale, 20, 40);
        var avatarFont = Math.Clamp(13 * softScale, 9, 18);
        var titleFont = Math.Clamp(14 * softScale, 10, 19);
        var titleMaxWidth = Math.Max(60, innerWidth - avatarSize - (rowPaddingHorizontal * 2d) - 18);

        var estimatedHeaderHeight = Math.Max(
            Math.Clamp(20 * softScale, 12, 28) + Math.Clamp(4 * softScale, 2, 8),
            Math.Clamp(34 * softScale, 22, 42));
        var estimatedRowHeight = avatarSize + (rowPaddingVertical * 2d);
        var availablePostsHeight = Math.Max(
            0d,
            totalHeight -
            CardBorder.Padding.Top -
            CardBorder.Padding.Bottom -
            estimatedHeaderHeight -
            rowSpacing);
        var rowFootprint = Math.Max(1d, estimatedRowHeight + rowSpacing);
        var capacityByHeight = (int)Math.Floor((availablePostsHeight + rowSpacing) / rowFootprint);
        var resolvedItemCount = Math.Clamp(capacityByHeight, BaseDisplayItemCount, MaxDisplayItemCount);
        if (scale < 1.08d)
        {
            resolvedItemCount = Math.Min(resolvedItemCount, BaseDisplayItemCount);
        }

        var previousVisibleItemCount = _visibleItemCount;
        _visibleItemCount = resolvedItemCount;

        foreach (var visual in _itemVisuals)
        {
            visual.Host.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(10 * softScale, 6, 14);
            visual.Host.Padding = new Thickness(rowPaddingHorizontal, rowPaddingVertical);
            visual.RowGrid.ColumnSpacing = Math.Clamp(8 * softScale, 4, 12);

            visual.AvatarHost.Width = avatarSize;
            visual.AvatarHost.Height = avatarSize;
            visual.AvatarHost.CornerRadius = new CornerRadius(avatarSize / 2d);

            visual.AvatarFallbackText.FontSize = avatarFont;
            visual.TitleTextBlock.FontSize = titleFont;
            visual.TitleTextBlock.MaxWidth = titleMaxWidth;
        }

        StatusTextBlock.FontSize = Math.Clamp(14 * softScale, 10, 18);

        ApplyNightModeVisual();

        if (_visibleItemCount != previousVisibleItemCount &&
            _isAttached &&
            !_isRefreshing &&
            _activeItems.Count < _visibleItemCount)
        {
            _ = RefreshPostsAsync(forceRefresh: false);
        }
    }

    private static string NormalizeCompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(text.Trim(), " ");
    }

    private static string ResolveAvatarFallbackText(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var compact = displayName.Trim();
        var first = compact[0];
        return first.ToString().ToUpperInvariant();
    }

    private static async Task<Bitmap?> TryDownloadAvatarBitmapAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizeHttpUrl(avatarUrl);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, normalizedUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", AvatarRequestUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            using var response = await AvatarHttpClient.SendAsync(
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

    private void SetAvatarBitmap(int index, Bitmap? bitmap)
    {
        if (index < 0 || index >= _avatarBitmaps.Length || index >= _itemVisuals.Count)
        {
            bitmap?.Dispose();
            return;
        }

        var visual = _itemVisuals[index];
        var oldBitmap = _avatarBitmaps[index];
        if (ReferenceEquals(visual.AvatarImage.Source, oldBitmap))
        {
            visual.AvatarImage.Source = null;
        }

        oldBitmap?.Dispose();
        _avatarBitmaps[index] = bitmap;
        visual.AvatarImage.Source = bitmap;
        visual.AvatarFallbackText.IsVisible = bitmap is null;
    }

    private void DisposeAvatarBitmaps()
    {
        for (var i = 0; i < _avatarBitmaps.Length; i++)
        {
            SetAvatarBitmap(i, null);
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
        return Math.Clamp(Math.Min(scaleX, scaleY), 0.62, 2.6);
    }

    private CornerRadius ResolveUnifiedMainRectangle() => new(ResolveUnifiedMainRadiusValue());

    private static double ResolveUnifiedMainRadiusValue() =>
        HostAppearanceThemeProvider.GetOrCreate().GetCurrent().CornerRadiusTokens.Lg.TopLeft;

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
