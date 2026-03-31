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
    private const int MaxDisplayItemCount = 12;
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(20)
    };

    private LanMountainDesktop.PluginSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly Dictionary<string, DailyNewsItemSnapshot> _newsByUrl = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NewsItemControl> _itemControls = [];
    private readonly Dictionary<string, Bitmap> _imageCache = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private string _channelType = IfengNewsChannelTypes.Comprehensive;
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private bool _isNightVisual = true;

    public IfengNewsWidget()
    {
        InitializeComponent();

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
        DisposeImageCache();
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

        BrandTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#FF6B5A") : Color.Parse("#E24B2D"));
        NewsBadge.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#FF6B5A") : Color.Parse("#E24B2D"));

        RefreshButton.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#EFF1F5"));
        RefreshGlyphIcon.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5E6671"));

        StatusTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#6A6F77"));
        LoadingTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#6A6F77"));

        foreach (var control in _itemControls)
        {
            control.ApplyNightMode(_isNightVisual);
        }
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
        ToolTip.SetTip(RefreshButton, L("ifeng.widget.refresh_tooltip", "刷新"));

        var newItems = snapshot.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Url) && !_newsByUrl.ContainsKey(item.Url))
            .ToList();

        if (newItems.Count == 0 && _itemControls.Count == 0)
        {
            ApplyEmptyState();
            return;
        }

        foreach (var item in newItems)
        {
            _newsByUrl[item.Url] = item;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isAttached) return;

            LoadingTextBlock.IsVisible = false;
            StatusTextBlock.IsVisible = false;

            foreach (var item in newItems)
            {
                var control = new NewsItemControl(item, _isNightVisual);
                control.Clicked += (s, url) => TryOpenUrl(url);
                NewsStackPanel.Children.Insert(NewsStackPanel.Children.Count - 1, control);
                _itemControls.Add(control);
            }

            UpdateAdaptiveLayout();
        });

        var imageTasks = newItems.Select(async item =>
        {
            var bitmap = await TryDownloadBitmapAsync(item.ImageUrl, cancellationToken);
            if (bitmap != null && !cancellationToken.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_imageCache.TryGetValue(item.Url, out var oldBitmap))
                    {
                        oldBitmap.Dispose();
                    }
                    _imageCache[item.Url] = bitmap;
                    
                    var control = _itemControls.FirstOrDefault(c => c.NewsUrl == item.Url);
                    control?.SetImage(bitmap);
                });
            }
        });

        await Task.WhenAll(imageTasks);
    }

    private void ApplyLoadingState()
    {
        ToolTip.SetTip(RefreshButton, L("ifeng.widget.refresh_tooltip", "刷新"));

        LoadingTextBlock.Text = L("ifeng.widget.loading", "加载中...");
        LoadingTextBlock.IsVisible = true;
        StatusTextBlock.IsVisible = false;
        UpdateAdaptiveLayout();
    }

    private void ApplyFailedState()
    {
        ToolTip.SetTip(RefreshButton, L("ifeng.widget.refresh_tooltip", "刷新"));

        LoadingTextBlock.IsVisible = false;
        StatusTextBlock.Text = L("ifeng.widget.fetch_failed", "新闻获取失败");
        StatusTextBlock.IsVisible = true;
        UpdateAdaptiveLayout();
    }

    private void ApplyEmptyState()
    {
        ToolTip.SetTip(RefreshButton, L("ifeng.widget.refresh_tooltip", "刷新"));

        LoadingTextBlock.IsVisible = false;
        StatusTextBlock.Text = L("ifeng.widget.fallback_item", "暂无新闻");
        StatusTextBlock.IsVisible = true;
        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var softScale = Math.Clamp(scale, 0.80, 1.32);
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var unifiedMainRectangle = ResolveUnifiedMainRectangle();
        RootBorder.CornerRadius = unifiedMainRectangle;
        CardBorder.CornerRadius = unifiedMainRectangle;

        var horizontalPadding = Math.Clamp(14 * softScale, 8, 20);
        var verticalPadding = Math.Clamp(14 * softScale, 8, 20);
        CardBorder.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

        var headerHeight = Math.Clamp(totalHeight * 0.10, 28, 54);
        HeaderGrid.Height = headerHeight;
        HeaderGrid.Margin = new Thickness(0, 0, 0, Math.Clamp(8 * softScale, 4, 12));

        var brandFontSize = Math.Clamp(headerHeight * 0.62, 14, 30);
        BrandTextBlock.FontSize = brandFontSize;
        NewsBadgeText.FontSize = brandFontSize;

        var refreshSize = Math.Clamp(headerHeight * 0.84, 22, 44);
        RefreshButton.Width = refreshSize;
        RefreshButton.Height = refreshSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshSize / 2d);
        RefreshGlyphIcon.FontSize = Math.Clamp(refreshSize * 0.44, 10, 20);

        var innerWidth = Math.Max(150, totalWidth - horizontalPadding * 2d);
        var imageWidth = Math.Clamp(innerWidth * 0.27, 82, 176);
        var imageHeight = Math.Clamp(imageWidth * 0.56, 46, 98);
        
        var baseTitleFont = 14;
        var areaFactor = (totalWidth * totalHeight) / (BaseWidthCells * BaseCellSize * BaseHeightCells * BaseCellSize);
        var adaptiveTitleFont = baseTitleFont * Math.Sqrt(Math.Clamp(areaFactor, 0.6, 2.5));
        var titleFont = Math.Clamp(adaptiveTitleFont, 11, 26);

        foreach (var control in _itemControls)
        {
            control.UpdateLayout(softScale, innerWidth, imageWidth, imageHeight, titleFont);
        }

        StatusTextBlock.FontSize = Math.Clamp(titleFont, 10, 24);
        LoadingTextBlock.FontSize = Math.Clamp(titleFont, 10, 24);
        ApplyNightModeVisual();
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

    private void DisposeImageCache()
    {
        foreach (var bitmap in _imageCache.Values)
        {
            bitmap.Dispose();
        }
        _imageCache.Clear();
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

    private CornerRadius ResolveUnifiedMainRectangle() => new(ResolveUnifiedMainRadiusValue());

    private static double ResolveUnifiedMainRadiusValue() =>
        HostAppearanceThemeProvider.GetOrCreate().GetCurrent().CornerRadiusTokens.Lg.TopLeft;

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

    private sealed class NewsItemControl : Border
    {
        private readonly DailyNewsItemSnapshot _item;
        private readonly Grid _grid;
        private readonly TextBlock _titleTextBlock;
        private readonly Border _imageHost;
        private readonly Image _imageControl;
        private bool _isNightVisual;
        private Point _pointerPressedPosition;
        private bool _isPointerPressed;

        public string NewsUrl => _item.Url;

        public NewsItemControl(DailyNewsItemSnapshot item, bool isNightVisual)
        {
            _item = item;
            _isNightVisual = isNightVisual;

            Padding = new Thickness(0, 4);
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);

            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            PointerCaptureLost += OnPointerCaptureLost;

            _titleTextBlock = new TextBlock
            {
                Text = NormalizeCompactText(item.Title),
                Foreground = new SolidColorBrush(isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327")),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            _imageControl = new Image
            {
                Stretch = Stretch.UniformToFill
            };

            _imageHost = new Border
            {
                Width = 148,
                Height = 84,
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Background = new SolidColorBrush(isNightVisual ? Color.Parse("#3D4250") : Color.Parse("#E6E8EC")),
                Child = _imageControl
            };

            _grid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                ColumnSpacing = 10
            };

            Grid.SetColumn(_imageHost, 1);
            _grid.Children.Add(_titleTextBlock);
            _grid.Children.Add(_imageHost);

            Child = _grid;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isPointerPressed = true;
                _pointerPressedPosition = e.GetPosition(this);
                e.Handled = true;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isPointerPressed)
            {
                return;
            }

            _isPointerPressed = false;
            var releasePosition = e.GetPosition(this);
            var distance = Math.Sqrt(
                Math.Pow(releasePosition.X - _pointerPressedPosition.X, 2) +
                Math.Pow(releasePosition.Y - _pointerPressedPosition.Y, 2));

            if (distance < 5)
            {
                Clicked?.Invoke(this, _item.Url);
            }

            e.Handled = true;
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _isPointerPressed = false;
        }

        public void ApplyNightMode(bool isNightVisual)
        {
            _isNightVisual = isNightVisual;
            _titleTextBlock.Foreground = new SolidColorBrush(isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"));
            _imageHost.Background = new SolidColorBrush(isNightVisual ? Color.Parse("#3D4250") : Color.Parse("#E6E8EC"));
        }

        public void UpdateLayout(double scale, double innerWidth, double imageWidth, double imageHeight, double titleFont)
        {
            var columnGap = Math.Clamp(imageHeight * 0.20, 6, 14);
            _grid.ColumnSpacing = columnGap;

            if (_grid.ColumnDefinitions.Count > 1)
            {
                _grid.ColumnDefinitions[1] = new ColumnDefinition(new GridLength(imageWidth));
            }

            _imageHost.Width = imageWidth;
            _imageHost.Height = imageHeight;
            _imageHost.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(imageHeight * 0.15, 8, 16);

            var textWidth = Math.Max(84, innerWidth - imageWidth - columnGap);
            _titleTextBlock.MaxWidth = textWidth;
            _titleTextBlock.FontSize = titleFont;
            _titleTextBlock.LineHeight = titleFont * 1.12;
            _titleTextBlock.MinHeight = _titleTextBlock.LineHeight * 2;
        }

        public void SetImage(Bitmap bitmap)
        {
            _imageControl.Source = bitmap;
        }

        public event EventHandler<string>? Clicked;
    }
}
