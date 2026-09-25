using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using LanMountainDesktop.Theme;
using LanMountainDesktop.Helpers;

namespace LanMountainDesktop.Views.Components;

public partial class Stcn24ForumWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget, ISettingsAwareComponentWidget
{
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private static readonly HttpClient AvatarHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private const string AvatarRequestUserAgent = HttpUserAgents.Browser;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 4;
    private const int BaseDisplayItemCount = 4;
    private const int MaxDisplayItemCount = 8;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(20)
    };

    private LanMountainDesktop.AirAppSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly List<Stcn24ForumPostItemSnapshot> _activeItems = [];
    private readonly List<ForumItemVisual> _itemVisuals = [];
    private readonly Bitmap?[] _avatarBitmaps = new Bitmap?[MaxDisplayItemCount];

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private readonly ComponentFeedRefresh _feed = new();
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private string _sourceType = Stcn24ForumSourceTypes.LatestCreated;
    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private int _visibleItemCount = BaseDisplayItemCount;
    private bool _isAttached;
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
        ComponentDesignMetrics.ApplyCellSize(
            ref _currentCellSize, cellSize, UpdateAdaptiveLayout);
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
        RecommendationServiceBinding.Attach(
            ref _recommendationService,
            recommendationInfoService,
            DefaultRecommendationService,
            () => _isAttached,
            () => RefreshPostsAsync(forceRefresh: false));
    }

    public void RefreshFromSettings()
    {
        RecommendationServiceBinding.AfterSettingsChange(
            _recommendationService.ClearCache,
            ApplyAutoRefreshSettings,
            () => _isAttached,
            () => RefreshPostsAsync(forceRefresh: true));
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ComponentRefreshLifetime.Attach(
            ref _isAttached, ApplyAutoRefreshSettings, null,
            () => RefreshPostsAsync(forceRefresh: false));
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ComponentRefreshLifetime.Detach(ref _isAttached, _refreshTimer, ref _feed.InFlight);
        DisposeAvatarBitmaps();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ComponentThemeMode.RefreshNightVisual(
            this, ref _isNightVisual, UpdateAdaptiveLayout, fallbackToNightWhenSurfaceUnknown: true);
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
        if (_feed.IsBusy)
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

        ExternalLinkLauncher.TryOpen(_activeItems[index].Url);
        e.Handled = true;
    }

    private async Task RefreshPostsAsync(bool forceRefresh) =>
        await _feed.RunAsync(
            () => _isAttached,
            () =>
            {
                UpdateRefreshButtonState();
                UpdateLanguageCode();
            },
            async token =>
            {
                var query = new Stcn24ForumPostsQuery(
                    Locale: _languageCode,
                    ItemCount: _visibleItemCount,
                    SourceType: _sourceType,
                    ForceRefresh: forceRefresh);
                var result = await _recommendationService.GetStcn24ForumPostsAsync(query, token);
                if (!result.Success || result.Data is null)
                {
                    return false;
                }

                await ApplySnapshotAsync(result.Data, token);
                return true;
            },
            ApplyFailedState,
            UpdateRefreshButtonState);

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
                visual.TitleTextBlock.Text = CompactText.Normalize(item.Title);
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
        RefreshButton.IsEnabled = !_feed.IsBusy;
        RefreshButton.Opacity = _feed.IsBusy ? 0.58 : 1.0;
    }

    private void UpdateLanguageCode() =>
        _languageCode = _localizationService.ResolveLanguageCode(() => _appSettingsService.Load().LanguageCode);

    private void ApplyAutoRefreshSettings()
    {
        var enabled = true;
        var intervalMinutes = 20;

        try
        {
            var snapshot = _componentSettingsService.Load();
            _sourceType = Stcn24ForumSourceTypes.Normalize(snapshot.Stcn24ForumSourceType);
            enabled = snapshot.Stcn24ForumAutoRefreshEnabled;
            intervalMinutes = RefreshIntervalCatalog.Normalize(snapshot.Stcn24ForumAutoRefreshIntervalMinutes, 20);
        }
        catch
        {
            // Keep fallback defaults.
            _sourceType = Stcn24ForumSourceTypes.LatestCreated;
        }

        ComponentRefreshLifetime.Reschedule(_refreshTimer, _isAttached, enabled, intervalMinutes);
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var softScale = Math.Clamp(scale, 0.80, 1.40);
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var unifiedMainRectangle = ComponentChromeCornerRadiusHelper.ResolveLgRectangle();
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
            visual.Host.CornerRadius = ComponentChromeCornerRadiusHelper.ScaleRadius(10 * softScale, 6, 14);
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
            !_feed.IsBusy &&
            _activeItems.Count < _visibleItemCount)
        {
            _ = RefreshPostsAsync(forceRefresh: false);
        }
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
        var normalizedUrl = ExternalLinkLauncher.NormalizeHttpUrl(avatarUrl);
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

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
