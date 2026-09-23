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
using LanMountainDesktop.Shared.Threading;

namespace LanMountainDesktop.Views.Components;

public partial class IfengNewsWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget, ISettingsAwareComponentWidget
{
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private static readonly HttpClient ImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 4;
    private const int MaxDisplayItemCount = 12;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(20)
    };

    private LanMountainDesktop.AirAppSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly Dictionary<string, DailyNewsItemSnapshot> _newsByUrl = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NewsItemControl> _itemControls = [];
    private readonly Dictionary<string, Bitmap> _imageCache = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private string _channelType = IfengNewsChannelTypes.Comprehensive;
    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
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
            () => RefreshNewsAsync(forceRefresh: false));
    }

    public void RefreshFromSettings()
    {
        RecommendationServiceBinding.AfterSettingsChange(
            _recommendationService.ClearCache,
            ApplyAutoRefreshSettings,
            () => _isAttached,
            () => RefreshNewsAsync(forceRefresh: true));
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
        ComponentRefreshLifetime.Detach(ref _isAttached, _refreshTimer, ref _refreshCts);
        DisposeImageCache();
        UpdateRefreshButtonState();
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
        CancellationHelper.CancelAndDispose(previous);

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
                control.Clicked += (s, url) => ExternalLinkLauncher.TryOpen(url);
                NewsStackPanel.Children.Insert(NewsStackPanel.Children.Count - 1, control);
                _itemControls.Add(control);
            }

            UpdateAdaptiveLayout();
        });

        var imageTasks = newItems.Select(async item =>
        {
            var bitmap = await RemoteImageBitmap.GetAsync(ImageHttpClient, item.ImageUrl, cancellationToken);
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

        var unifiedMainRectangle = ComponentChromeCornerRadiusHelper.ResolveLgRectangle();
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
        var areaFactor = (totalWidth * totalHeight) / (BaseWidthCells * ComponentDesignMetrics.BaseCellSize * BaseHeightCells * ComponentDesignMetrics.BaseCellSize);
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

    private void UpdateLanguageCode() =>
        _languageCode = _localizationService.ResolveLanguageCode(() => _appSettingsService.Load().LanguageCode);

    private void ApplyAutoRefreshSettings()
    {
        var enabled = true;
        var intervalMinutes = 20;
        var channelType = IfengNewsChannelTypes.Comprehensive;

        try
        {
            var snapshot = _componentSettingsService.Load();
            enabled = snapshot.IfengNewsAutoRefreshEnabled;
            intervalMinutes = RefreshIntervalCatalog.Normalize(snapshot.IfengNewsAutoRefreshIntervalMinutes, 20);
            channelType = IfengNewsChannelTypes.Normalize(snapshot.IfengNewsChannelType);
        }
        catch
        {
        }

        _autoRefreshEnabled = enabled;
        _channelType = channelType;
        ComponentRefreshLifetime.Reschedule(_refreshTimer, _isAttached, enabled, intervalMinutes);
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

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private sealed class NewsItemControl : Border
    {
        private readonly DailyNewsItemSnapshot _item;
        private readonly Grid _grid;
        private readonly TextBlock _titleTextBlock;
        private readonly Border _imageHost;
        private readonly Image _imageControl;
        private Point _pointerPressedPosition;
        private bool _isPointerPressed;

        public string NewsUrl => _item.Url;

        public NewsItemControl(DailyNewsItemSnapshot item, bool isNightVisual)
        {
            _item = item;

            Padding = new Thickness(0, 4);
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);

            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            PointerCaptureLost += OnPointerCaptureLost;

            _titleTextBlock = new TextBlock
            {
                Text = CompactText.Normalize(item.Title),
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
            _imageHost.CornerRadius = ComponentChromeCornerRadiusHelper.ScaleRadius(imageHeight * 0.15, 8, 16);

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
