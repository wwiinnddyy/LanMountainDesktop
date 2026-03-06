using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class BaiduHotSearchWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();

    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;
    private const int MaxDisplayItemCount = 4;
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(15)
    };

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly List<BaiduHotSearchItemSnapshot> _activeItems = [];
    private readonly List<HotItemVisual> _hotItemVisuals = [];

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private string _sourceType = BaiduHotSearchSourceTypes.Official;

    private sealed record HotItemVisual(
        Border Host,
        Grid RowGrid,
        TextBlock IndexTextBlock,
        TextBlock TitleTextBlock);

    public BaiduHotSearchWidget()
    {
        InitializeComponent();

        BrandTextBlock.FontFamily = MiSansFontFamily;
        HotItem1IndexTextBlock.FontFamily = MiSansFontFamily;
        HotItem2IndexTextBlock.FontFamily = MiSansFontFamily;
        HotItem3IndexTextBlock.FontFamily = MiSansFontFamily;
        HotItem4IndexTextBlock.FontFamily = MiSansFontFamily;
        HotItem1TextBlock.FontFamily = MiSansFontFamily;
        HotItem2TextBlock.FontFamily = MiSansFontFamily;
        HotItem3TextBlock.FontFamily = MiSansFontFamily;
        HotItem4TextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

        _hotItemVisuals.Add(new HotItemVisual(HotItem1Host, HotItem1Grid, HotItem1IndexTextBlock, HotItem1TextBlock));
        _hotItemVisuals.Add(new HotItemVisual(HotItem2Host, HotItem2Grid, HotItem2IndexTextBlock, HotItem2TextBlock));
        _hotItemVisuals.Add(new HotItemVisual(HotItem3Host, HotItem3Grid, HotItem3IndexTextBlock, HotItem3TextBlock));
        _hotItemVisuals.Add(new HotItemVisual(HotItem4Host, HotItem4Grid, HotItem4IndexTextBlock, HotItem4TextBlock));

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

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
            _ = RefreshHotSearchAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        _recommendationService.ClearCache();
        ApplyAutoRefreshSettings();
        if (_isAttached)
        {
            _ = RefreshHotSearchAsync(forceRefresh: true);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRefreshSettings();
        UpdateRefreshButtonState();
        _ = RefreshHotSearchAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        UpdateRefreshButtonState();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshHotSearchAsync(forceRefresh: true);
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        await RefreshHotSearchAsync(forceRefresh: true);
        e.Handled = true;
    }

    private async Task RefreshHotSearchAsync(bool forceRefresh)
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
            var query = new BaiduHotSearchQuery(
                Locale: _languageCode,
                ItemCount: MaxDisplayItemCount,
                SourceType: _sourceType,
                ForceRefresh: forceRefresh);
            var result = await _recommendationService.GetBaiduHotSearchAsync(query, cts.Token);
            if (!_isAttached || cts.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyFailedState();
                return;
            }

            ApplySnapshot(result.Data);
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

    private void ApplySnapshot(BaiduHotSearchSnapshot snapshot)
    {
        BrandTextBlock.Text = L("baiduhot.widget.brand", "百度热搜");
        ToolTip.SetTip(RefreshButton, L("baiduhot.widget.refresh_tooltip", "刷新"));

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

        var fallbackText = L("baiduhot.widget.fallback_item", "暂无热搜");
        for (var i = 0; i < _hotItemVisuals.Count; i++)
        {
            var visual = _hotItemVisuals[i];
            visual.Host.IsVisible = true;
            visual.IndexTextBlock.Text = (i + 1).ToString();
            visual.TitleTextBlock.Text = i < _activeItems.Count
                ? NormalizeCompactText(_activeItems[i].Title)
                : fallbackText;
        }

        StatusTextBlock.IsVisible = false;
        UpdateInteractionState();
        UpdateAdaptiveLayout();
    }

    private void ApplyLoadingState()
    {
        BrandTextBlock.Text = L("baiduhot.widget.brand", "百度热搜");
        ToolTip.SetTip(RefreshButton, L("baiduhot.widget.refresh_tooltip", "刷新"));
        _activeItems.Clear();

        var loadingText = L("baiduhot.widget.loading_item", "加载中...");
        for (var i = 0; i < _hotItemVisuals.Count; i++)
        {
            var visual = _hotItemVisuals[i];
            visual.Host.IsVisible = true;
            visual.IndexTextBlock.Text = (i + 1).ToString();
            visual.TitleTextBlock.Text = loadingText;
        }

        StatusTextBlock.Text = L("baiduhot.widget.loading", "加载中...");
        StatusTextBlock.IsVisible = true;
        UpdateInteractionState();
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        BrandTextBlock.Text = L("baiduhot.widget.brand", "百度热搜");
        ToolTip.SetTip(RefreshButton, L("baiduhot.widget.refresh_tooltip", "刷新"));
        _activeItems.Clear();

        var fallbackText = L("baiduhot.widget.fallback_item", "暂无热搜");
        for (var i = 0; i < _hotItemVisuals.Count; i++)
        {
            var visual = _hotItemVisuals[i];
            visual.Host.IsVisible = true;
            visual.IndexTextBlock.Text = (i + 1).ToString();
            visual.TitleTextBlock.Text = fallbackText;
        }

        StatusTextBlock.Text = L("baiduhot.widget.fetch_failed", "热搜获取失败");
        StatusTextBlock.IsVisible = true;
        UpdateInteractionState();
        UpdateAdaptiveLayout();
    }

    private void OnHotItemPointerPressed(object? sender, PointerPressedEventArgs e)
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

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var softScale = Math.Clamp(scale, 0.84, 1.26);
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * softScale, 16, 52));
        RootBorder.Padding = new Thickness(0);

        var horizontalPadding = Math.Clamp(16 * softScale, 8, 24);
        var verticalPadding = Math.Clamp(14 * softScale, 7, 20);
        CardBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * softScale, 16, 52));
        CardBorder.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

        var innerWidth = Math.Max(120, totalWidth - (horizontalPadding * 2d));
        var innerHeight = Math.Max(72, totalHeight - (verticalPadding * 2d));
        var rowSpacing = Math.Clamp(6 * softScale, 2, 9);
        ContentGrid.RowSpacing = rowSpacing;
        HeaderGrid.ColumnSpacing = Math.Clamp(10 * softScale, 6, 16);

        var availableRowsHeight = Math.Max(40, innerHeight - rowSpacing * 4d);
        var minTopRowHeight = Math.Clamp(22 * softScale, 18, 34);
        var topRowHeight = Math.Clamp(availableRowsHeight * 0.30, minTopRowHeight, 54);
        var lineRowHeight = Math.Max(10, (availableRowsHeight - topRowHeight) / 4d);
        var minLineRowHeight = Math.Clamp(13 * softScale, 11, 24);
        if (lineRowHeight < minLineRowHeight)
        {
            lineRowHeight = minLineRowHeight;
            topRowHeight = Math.Max(minTopRowHeight, availableRowsHeight - lineRowHeight * 4d);
            lineRowHeight = Math.Max(10, (availableRowsHeight - topRowHeight) / 4d);
        }

        if (ContentGrid.RowDefinitions.Count >= 5)
        {
            ContentGrid.RowDefinitions[0].Height = new GridLength(topRowHeight);
            for (var i = 1; i <= 4; i++)
            {
                ContentGrid.RowDefinitions[i].Height = new GridLength(lineRowHeight);
            }
        }

        BrandTextBlock.FontSize = Math.Clamp(topRowHeight * 0.48, 12, 24);
        BrandTextBlock.MaxWidth = Math.Max(80, innerWidth - Math.Clamp(topRowHeight * 0.84, 20, 46));

        var refreshButtonSize = Math.Clamp(topRowHeight * 0.84, 20, 46);
        RefreshButton.Width = refreshButtonSize;
        RefreshButton.Height = refreshButtonSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshButtonSize / 2d);
        RefreshGlyphIcon.FontSize = Math.Clamp(refreshButtonSize * 0.46, 10, 20);

        var lineColumnGap = Math.Clamp(lineRowHeight * 0.34, 5, 12);
        var indexWidth = Math.Clamp(lineRowHeight * 1.02, 16, 28);
        var indexFont = Math.Clamp(lineRowHeight * 0.50, 10, 16);
        var itemFont = Math.Clamp(lineRowHeight * 0.62, 12, 24);
        var rowPadding = Math.Clamp(lineRowHeight * 0.08, 1, 4);
        var itemTextWidth = Math.Max(56, innerWidth - indexWidth - lineColumnGap);

        foreach (var visual in _hotItemVisuals)
        {
            visual.RowGrid.ColumnSpacing = lineColumnGap;
            if (visual.RowGrid.ColumnDefinitions.Count > 0)
            {
                visual.RowGrid.ColumnDefinitions[0].Width = new GridLength(indexWidth, GridUnitType.Pixel);
            }

            visual.Host.Padding = new Thickness(0, rowPadding, 0, rowPadding);
            visual.IndexTextBlock.FontSize = indexFont;
            visual.IndexTextBlock.MaxWidth = indexWidth;
            visual.TitleTextBlock.FontSize = itemFont;
            visual.TitleTextBlock.MaxWidth = itemTextWidth;
            visual.TitleTextBlock.TextAlignment = TextAlignment.Left;
        }

        StatusTextBlock.FontSize = Math.Clamp(itemFont, 10, 20);
    }

    private void UpdateInteractionState()
    {
        for (var i = 0; i < _hotItemVisuals.Count; i++)
        {
            var visual = _hotItemVisuals[i];
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
        var intervalMinutes = 15;
        var sourceType = BaiduHotSearchSourceTypes.Official;

        try
        {
            var snapshot = _componentSettingsService.Load();
            enabled = snapshot.BaiduHotSearchAutoRefreshEnabled;
            intervalMinutes = NormalizeAutoRefreshIntervalMinutes(snapshot.BaiduHotSearchAutoRefreshIntervalMinutes);
            sourceType = BaiduHotSearchSourceTypes.Normalize(snapshot.BaiduHotSearchSourceType);
        }
        catch
        {
            // Keep fallback defaults.
        }

        _autoRefreshEnabled = enabled;
        _sourceType = sourceType;
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
            return 15;
        }

        if (SupportedAutoRefreshIntervalsMinutes.Contains(minutes))
        {
            return minutes;
        }

        return SupportedAutoRefreshIntervalsMinutes
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(15);
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
        return Math.Clamp(Math.Min(scaleX, scaleY), 0.72, 2.8);
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
