using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Views.Components;

public partial class ZhiJiaoHubWidget : UserControl,
    IDesktopComponentWidget,
    IRecommendationInfoAwareComponentWidget,
    IComponentSettingsContextAware,
    IComponentPlacementContextAware
{
    private const double BaseCellSize = 48d;
    private const double SwipeThreshold = 50;

    private readonly DispatcherTimer _refreshTimer = new();

    private IRecommendationInfoService _recommendationService;
    private IComponentSettingsAccessor? _componentSettingsAccessor;
    private ISettingsService _appSettingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;

    private CancellationTokenSource? _refreshCts;

    private string _source = ZhiJiaoHubSources.ClassIsland;
    private string _mirrorSource = ZhiJiaoHubMirrorSources.Direct;
    private string _componentId = BuiltInComponentIds.DesktopZhiJiaoHub;
    private string _placementId = string.Empty;
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isSyncing;
    private bool _autoRefreshEnabled = true;
    private int _pendingImageIndex = 0;

    private IReadOnlyList<ZhiJiaoHubLocalImageItem> _localImages = [];
    private int _currentImageIndex = 0;

    private readonly Dictionary<int, Bitmap> _imageCache = new();
    private readonly object _cacheLock = new();
    private const int MaxCacheSize = 5;

    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragOffset;
    private int _lastSwipeDirection = 0;

    public ZhiJiaoHubWidget()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            ApplyCellSize(_currentCellSize);
            return;
        }

        _recommendationService = new RecommendationDataService();

        _refreshTimer.Tick += OnRefreshTimerTick;

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        ApplyLoadingState();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;

        LoadSettings();
        _ = InitializeOrSyncAsync();
        UpdateTimers();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        _refreshCts?.Cancel();

        lock (_cacheLock)
        {
            foreach (var bitmap in _imageCache.Values)
            {
                bitmap.Dispose();
            }
            _imageCache.Clear();
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = _currentCellSize / BaseCellSize;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(12 * scale, 4, 24));

        var fontSize = Math.Clamp(11 * scale, 9, 18);
        ImageNameTextBlock.FontSize = fontSize;
        LoadingTextBlock.FontSize = Math.Clamp(12 * scale, 10, 16);
        ErrorTextBlock.FontSize = Math.Clamp(10 * scale, 8, 14);

        GradientOverlay.Height = Math.Clamp(60 * scale, 30, 100);

        ImageNameTextBlock.Margin = new Thickness(
            Math.Clamp(10 * scale, 5, 20),
            0,
            Math.Clamp(10 * scale, 5, 20),
            Math.Clamp(8 * scale, 4, 16));

        IndicatorBorder.Margin = new Thickness(0, 0, Math.Clamp(6 * scale, 3, 12), 0);
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
    }

    public void SetComponentSettingsContext(DesktopComponentSettingsContext context)
    {
        _componentId = context.ComponentId;
        _placementId = context.PlacementId ?? string.Empty;
        _componentSettingsAccessor = context.ComponentSettingsAccessor;

        LoadSettings();

        if (_isAttached)
        {
            _ = InitializeOrSyncAsync();
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = componentId;
        _placementId = placementId ?? string.Empty;
    }

    public void RefreshFromSettings()
    {
        LoadSettings();
        UpdateTimers();
        if (_isAttached)
        {
            _ = InitializeOrSyncAsync();
        }
    }

    private void LoadSettings()
    {
        try
        {
            var snapshot = _componentSettingsAccessor?.LoadSnapshot<ComponentSettingsSnapshot>();
            if (snapshot is not null)
            {
                _source = ZhiJiaoHubSources.Normalize(snapshot.ZhiJiaoHubSource);
                _mirrorSource = ZhiJiaoHubMirrorSources.Normalize(snapshot.ZhiJiaoHubMirrorSource);
                _autoRefreshEnabled = snapshot.ZhiJiaoHubAutoRefreshEnabled;
                _pendingImageIndex = snapshot.ZhiJiaoHubCurrentImageIndex;

                var intervalMinutes = Math.Clamp(snapshot.ZhiJiaoHubAutoRefreshIntervalMinutes, 5, 1440);
                _refreshTimer.Interval = TimeSpan.FromMinutes(intervalMinutes);
            }
        }
        catch
        {
        }
    }

    private void SaveCurrentImageIndex()
    {
        try
        {
            var snapshot = _componentSettingsAccessor?.LoadSnapshot<ComponentSettingsSnapshot>()
                ?? new ComponentSettingsSnapshot();
            snapshot.ZhiJiaoHubCurrentImageIndex = _currentImageIndex;
            _componentSettingsAccessor?.SaveSnapshot(snapshot, [nameof(ComponentSettingsSnapshot.ZhiJiaoHubCurrentImageIndex)]);
        }
        catch
        {
        }
    }

    private void UpdateTimers()
    {
        if (_autoRefreshEnabled)
        {
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    private async Task InitializeOrSyncAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        _isSyncing = true;
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        try
        {
            var localSnapshot = _recommendationService.LoadZhiJiaoHubLocalSnapshot(_source);

            if (localSnapshot != null && localSnapshot.Images.Count > 0)
            {
                _localImages = localSnapshot.Images;
                _currentImageIndex = Math.Clamp(_pendingImageIndex, 0, Math.Max(0, _localImages.Count - 1));
                _pendingImageIndex = 0;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateIndicators();
                    _ = LoadAndDisplayCurrentImageAsync();
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoadingTextBlock.Text = "首次同步图片...";
                    ApplyLoadingState();
                });

                var progress = new Progress<(int Current, int Total, string Status)>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        LoadingTextBlock.Text = $"同步中 {p.Current}/{p.Total}";
                    });
                });

                var syncResult = await _recommendationService.SyncZhiJiaoHubImagesAsync(
                    _source,
                    _mirrorSource,
                    progress,
                    ct);

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (syncResult.Success && syncResult.Snapshot != null)
                {
                    _localImages = syncResult.Snapshot.Images;
                    _currentImageIndex = Math.Clamp(_pendingImageIndex, 0, Math.Max(0, _localImages.Count - 1));
                    _pendingImageIndex = 0;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateIndicators();
                        _ = LoadAndDisplayCurrentImageAsync();
                    });
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ApplyErrorState(syncResult.ErrorMessage ?? "同步失败");
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyErrorState($"初始化失败: {ex.Message}");
            });
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        _isSyncing = true;

        try
        {
            var progress = new Progress<(int Current, int Total, string Status)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LoadingTextBlock.Text = $"更新中 {p.Current}/{p.Total}";
                });
            });

            var syncResult = await _recommendationService.SyncZhiJiaoHubImagesAsync(
                _source,
                _mirrorSource,
                progress,
                CancellationToken.None);

            if (syncResult.Success && syncResult.Snapshot != null && syncResult.DownloadedCount > 0)
            {
                _localImages = syncResult.Snapshot.Images;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_currentImageIndex >= _localImages.Count)
                    {
                        _currentImageIndex = 0;
                        SaveCurrentImageIndex();
                    }

                    UpdateIndicators();
                    _ = LoadAndDisplayCurrentImageAsync();
                });
            }
        }
        catch
        {
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task LoadAndDisplayCurrentImageAsync(int direction = 0)
    {
        if (_localImages.Count == 0)
        {
            ApplyErrorState("暂无图片");
            return;
        }

        var imageItem = _localImages[_currentImageIndex];

        try
        {
            Bitmap? cachedBitmap = null;
            lock (_cacheLock)
            {
                _imageCache.TryGetValue(_currentImageIndex, out cachedBitmap);
            }

            if (cachedBitmap != null)
            {
                CurrentImage.Source = cachedBitmap;
                ImageNameTextBlock.Text = imageItem.Name;
                ApplyContentVisibleState();
                _ = Task.Run(async () => await PreloadAdjacentImagesAsync(direction));
                return;
            }

            if (!File.Exists(imageItem.LocalPath))
            {
                ApplyErrorState("图片文件不存在");
                return;
            }

            await using var fileStream = File.OpenRead(imageItem.LocalPath);
            var bitmap = new Bitmap(fileStream);

            lock (_cacheLock)
            {
                if (_imageCache.Count >= MaxCacheSize)
                {
                    CleanupFarthestCacheUnsafe();
                }
                _imageCache[_currentImageIndex] = bitmap;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentImage.Source = bitmap;
                ImageNameTextBlock.Text = imageItem.Name;
                ApplyContentVisibleState();
            });

            _ = Task.Run(async () => await PreloadAdjacentImagesAsync(direction));
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyErrorState($"图片加载失败: {ex.Message}");
            });
        }
    }

    private async Task PreloadAdjacentImagesAsync(int direction = 0)
    {
        if (_localImages.Count <= 1)
        {
            return;
        }

        var indicesToPreload = new List<int>();
        var currentIndex = _currentImageIndex;

        lock (_cacheLock)
        {
            if (direction <= 0)
            {
                var nextIndex = (currentIndex + 1) % _localImages.Count;
                if (!_imageCache.ContainsKey(nextIndex))
                {
                    indicesToPreload.Add(nextIndex);
                }

                var nextNextIndex = (currentIndex + 2) % _localImages.Count;
                if (!_imageCache.ContainsKey(nextNextIndex) && indicesToPreload.Count < 3)
                {
                    indicesToPreload.Add(nextNextIndex);
                }
            }

            if (direction >= 0)
            {
                var prevIndex = (currentIndex - 1 + _localImages.Count) % _localImages.Count;
                if (!_imageCache.ContainsKey(prevIndex))
                {
                    indicesToPreload.Add(prevIndex);
                }

                var prevPrevIndex = (currentIndex - 2 + _localImages.Count) % _localImages.Count;
                if (!_imageCache.ContainsKey(prevPrevIndex) && indicesToPreload.Count < 3)
                {
                    indicesToPreload.Add(prevPrevIndex);
                }
            }
        }

        if (indicesToPreload.Count == 0)
        {
            return;
        }

        var preloadTasks = indicesToPreload.Select(async index =>
        {
            try
            {
                lock (_cacheLock)
                {
                    if (_imageCache.ContainsKey(index))
                    {
                        return;
                    }

                    if (_imageCache.Count >= MaxCacheSize)
                    {
                        CleanupFarthestCacheUnsafe();
                    }
                }

                var imageItem = _localImages[index];
                if (!File.Exists(imageItem.LocalPath))
                {
                    return;
                }

                await using var fileStream = File.OpenRead(imageItem.LocalPath);
                var bitmap = new Bitmap(fileStream);

                lock (_cacheLock)
                {
                    if (!_imageCache.ContainsKey(index))
                    {
                        _imageCache[index] = bitmap;
                    }
                    else
                    {
                        bitmap.Dispose();
                    }
                }
            }
            catch
            {
            }
        }).ToList();

        await Task.WhenAll(preloadTasks);
    }

    private void CleanupFarthestCacheUnsafe()
    {
        if (_imageCache.Count == 0) return;

        var farthestKey = -1;
        var maxDistance = -1;
        var currentIndex = _currentImageIndex;
        var imageCount = _localImages.Count;

        foreach (var key in _imageCache.Keys)
        {
            if (key == currentIndex) continue;

            var forwardDistance = (key - currentIndex + imageCount) % imageCount;
            var backwardDistance = (currentIndex - key + imageCount) % imageCount;
            var distance = Math.Min(forwardDistance, backwardDistance);

            if (distance > maxDistance)
            {
                maxDistance = distance;
                farthestKey = key;
            }
        }

        if (farthestKey >= 0)
        {
            if (_imageCache.TryGetValue(farthestKey, out var bitmap))
            {
                bitmap.Dispose();
            }
            _imageCache.Remove(farthestKey);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_localImages.Count <= 1)
        {
            return;
        }

        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        _dragOffset = 0;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _localImages.Count <= 1)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        _dragOffset = currentPoint.Y - _dragStartPoint.Y;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;

        if (Math.Abs(_dragOffset) > SwipeThreshold)
        {
            if (_dragOffset > 0)
            {
                _lastSwipeDirection = 1;
                SwitchToPrevImage();
            }
            else
            {
                _lastSwipeDirection = -1;
                SwitchToNextImage();
            }
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_localImages.Count <= 1)
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            _lastSwipeDirection = 1;
            SwitchToPrevImage();
        }
        else if (e.Delta.Y < 0)
        {
            _lastSwipeDirection = -1;
            SwitchToNextImage();
        }

        e.Handled = true;
    }

    private void SwitchToPrevImage()
    {
        if (_localImages.Count <= 1)
        {
            return;
        }

        _currentImageIndex = (_currentImageIndex - 1 + _localImages.Count) % _localImages.Count;
        SaveCurrentImageIndex();
        UpdateIndicators();

        if (TryDisplayCachedImage(_currentImageIndex))
        {
            _ = Task.Run(async () => await PreloadAdjacentImagesAsync(_lastSwipeDirection));
            return;
        }

        _ = LoadAndDisplayCurrentImageAsync(_lastSwipeDirection);
    }

    private void SwitchToNextImage()
    {
        if (_localImages.Count <= 1)
        {
            return;
        }

        _currentImageIndex = (_currentImageIndex + 1) % _localImages.Count;
        SaveCurrentImageIndex();
        UpdateIndicators();

        if (TryDisplayCachedImage(_currentImageIndex))
        {
            _ = Task.Run(async () => await PreloadAdjacentImagesAsync(_lastSwipeDirection));
            return;
        }

        _ = LoadAndDisplayCurrentImageAsync(_lastSwipeDirection);
    }

    private bool TryDisplayCachedImage(int index)
    {
        if (_localImages.Count == 0 || index < 0 || index >= _localImages.Count)
        {
            return false;
        }

        Bitmap? cachedBitmap = null;
        lock (_cacheLock)
        {
            _imageCache.TryGetValue(index, out cachedBitmap);
        }

        if (cachedBitmap != null)
        {
            var imageItem = _localImages[index];
            CurrentImage.Source = cachedBitmap;
            ImageNameTextBlock.Text = imageItem.Name;
            ApplyContentVisibleState();
            return true;
        }

        return false;
    }

    private void ApplyLoadingState()
    {
        CurrentImage.IsVisible = false;
        ImageNameTextBlock.IsVisible = false;
        GradientOverlay.IsVisible = false;
        ErrorTextBlock.IsVisible = false;
        LoadingPanel.IsVisible = true;
    }

    private void ApplyContentVisibleState()
    {
        LoadingPanel.IsVisible = false;
        ErrorTextBlock.IsVisible = false;
        CurrentImage.IsVisible = true;
        ImageNameTextBlock.IsVisible = true;
        GradientOverlay.IsVisible = true;
    }

    private void ApplyErrorState(string message)
    {
        CurrentImage.IsVisible = false;
        ImageNameTextBlock.IsVisible = false;
        GradientOverlay.IsVisible = false;
        LoadingPanel.IsVisible = false;
        ErrorTextBlock.Text = message;
        ErrorTextBlock.IsVisible = true;
    }

    private void UpdateIndicators()
    {
        IndicatorPanel.Children.Clear();

        if (_localImages.Count <= 1)
        {
            return;
        }

        var maxIndicators = Math.Min(_localImages.Count, 7);
        for (int i = 0; i < maxIndicators; i++)
        {
            var isActive = i == _currentImageIndex % maxIndicators;
            var indicator = new Border
            {
                Width = isActive ? 6 : 4,
                Height = isActive ? 6 : 4,
                CornerRadius = new CornerRadius(3),
                Background = isActive
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Color.FromArgb(128, 255, 255, 255))
            };
            IndicatorPanel.Children.Add(indicator);
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_isAttached && _autoRefreshEnabled)
        {
            _ = CheckForUpdatesAsync();
        }
    }
}
