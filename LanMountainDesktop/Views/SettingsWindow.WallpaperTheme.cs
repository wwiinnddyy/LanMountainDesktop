using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LibVLCSharp.Shared;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private void OnNightModeChecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressThemeToggleEvents)
        {
            return;
        }

        ApplyNightModeState(true, refreshPalettes: true);
    }

    private void OnNightModeUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressThemeToggleEvents)
        {
            return;
        }

        ApplyNightModeState(false, refreshPalettes: true);
    }

    private async void OnPickWallpaperClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _wallpaperStatus = L("settings.wallpaper.storage_unavailable", "Storage provider is unavailable.");
            UpdateWallpaperDisplay();
            return;
        }

        var options = new FilePickerOpenOptions
        {
            Title = L("filepicker.title", "Select wallpaper"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(L("filepicker.image_files", "Image files")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"] },
                new FilePickerFileType(L("filepicker.video_files", "Video files")) { Patterns = ["*.mp4", "*.mkv", "*.webm", "*.avi", "*.mov", "*.m4v"] }
            ]
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var importedPath = await ImportWallpaperAssetAsync(files[0]);
            if (string.IsNullOrWhiteSpace(importedPath))
            {
                _wallpaperStatus = L("settings.wallpaper.import_failed", "Failed to import wallpaper file.");
                UpdateWallpaperDisplay();
                return;
            }

            _wallpaperPath = importedPath;
            var mediaType = DetectWallpaperMediaType(importedPath);
            switch (mediaType)
            {
                case WallpaperMediaType.Image:
                    _wallpaperBitmap?.Dispose();
                    _wallpaperBitmap = new Bitmap(importedPath);
                    _wallpaperVideoPath = null;
                    _wallpaperMediaType = WallpaperMediaType.Image;
                    _wallpaperStatus = L("settings.wallpaper.image_applied", "Image wallpaper applied.");
                    break;
                case WallpaperMediaType.Video:
                    _wallpaperBitmap?.Dispose();
                    _wallpaperBitmap = null;
                    _wallpaperVideoPath = importedPath;
                    _wallpaperMediaType = WallpaperMediaType.Video;
                    _wallpaperStatus = L("settings.wallpaper.video_applied", "Video wallpaper applied.");
                    break;
                default:
                    _wallpaperStatus = L("settings.wallpaper.unsupported_file", "Selected file type is not supported.");
                    UpdateWallpaperDisplay();
                    return;
            }

            ApplyWallpaperBrush();
            UpdateWallpaperDisplay();
            RefreshColorPalettes();
            EnsureSelectedThemeColor();
            UpdateThemeColorSelectionState();
            ThemeColorStatusTextBlock.Text = _wallpaperMediaType == WallpaperMediaType.Video
                ? L("settings.color.theme_updated_video", "Video wallpaper updated. Theme colors refreshed.")
                : L("settings.color.theme_updated_wallpaper", "Wallpaper updated. Monet colors refreshed.");
            PersistSettings();
        }
        catch (Exception ex)
        {
            _wallpaperStatus = Lf("settings.wallpaper.apply_failed_format", "Failed to apply wallpaper: {0}", ex.Message);
            UpdateWallpaperDisplay();
        }
    }

    private void OnClearWallpaperClick(object? sender, RoutedEventArgs e)
    {
        StopVideoWallpaper();
        _wallpaperVideoPath = null;
        _wallpaperMediaType = WallpaperMediaType.None;
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        _wallpaperPath = null;
        _wallpaperStatus = L("settings.wallpaper.cleared", "Background reset to solid color.");
        ApplyWallpaperBrush();
        UpdateWallpaperDisplay();
        RefreshColorPalettes();
        EnsureSelectedThemeColor();
        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = L("settings.color.theme_cleared_wallpaper", "Wallpaper cleared. Monet colors refreshed.");
        PersistSettings();
    }

    private void OnWallpaperPlacementSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyWallpaperBrush();
        if (_wallpaperMediaType == WallpaperMediaType.Image && _wallpaperBitmap is not null)
        {
            _wallpaperStatus = Lf("settings.wallpaper.mode_format", "Wallpaper mode: {0}.", GetLocalizedPlacementDisplayName(GetSelectedWallpaperPlacement()));
        }
        else if (_wallpaperMediaType == WallpaperMediaType.Video)
        {
            _wallpaperStatus = L("settings.wallpaper.video_mode", "Video wallpaper mode uses automatic fill mode.");
        }

        UpdateWallpaperDisplay();
        PersistSettings();
    }

    private void ApplyWallpaperBrush()
    {
        if (_wallpaperMediaType == WallpaperMediaType.Video && !string.IsNullOrWhiteSpace(_wallpaperVideoPath))
        {
            DesktopWallpaperLayer.Background = Brushes.Transparent;
            WallpaperPreviewViewport.Background = GetThemeDefaultDesktopBackground();
            SyncVideoWallpaperPreviewPlayback();
            return;
        }

        StopVideoWallpaper();
        if (_wallpaperBitmap is null)
        {
            var fallbackBackground = GetThemeDefaultDesktopBackground();
            DesktopWallpaperLayer.Background = fallbackBackground;
            WallpaperPreviewViewport.Background = fallbackBackground;
            return;
        }

        var placement = GetSelectedWallpaperPlacement();
        DesktopWallpaperLayer.Background = CreateWallpaperBrush(_wallpaperBitmap, placement, false);
        WallpaperPreviewViewport.Background = CreateWallpaperBrush(_wallpaperBitmap, placement, true);
    }

    private void SyncVideoWallpaperPreviewPlayback()
    {
        var shouldPlay =
            _wallpaperMediaType == WallpaperMediaType.Video &&
            !string.IsNullOrWhiteSpace(_wallpaperVideoPath) &&
            WallpaperSettingsPanel.IsVisible;

        if (!shouldPlay)
        {
            if (_previewVideoWallpaperPlayer?.IsPlaying == true)
            {
                StopPreviewVideoCapture(clearSnapshot: false);
            }

            WallpaperPreviewVideoImage.IsVisible = WallpaperPreviewVideoImage.Source is not null && WallpaperSettingsPanel.IsVisible;
            return;
        }

        if (WallpaperPreviewVideoImage.Source is not null)
        {
            WallpaperPreviewVideoImage.IsVisible = true;
            return;
        }

        if (_previewVideoWallpaperMedia is null || _previewVideoSnapshotPending)
        {
            PlayVideoWallpaper(_wallpaperVideoPath!);
        }
    }

    private void UpdateWallpaperDisplay()
    {
        WallpaperPathTextBlock.Text = string.IsNullOrWhiteSpace(_wallpaperPath)
            ? L("settings.wallpaper.no_selection", "No wallpaper selected.")
            : Path.GetFileName(_wallpaperPath);
        WallpaperStatusTextBlock.Text = _wallpaperStatus;
        WallpaperPlacementComboBox.IsEnabled = _wallpaperMediaType != WallpaperMediaType.Video;

        if (_wallpaperMediaType == WallpaperMediaType.Video)
        {
            WallpaperPreviewViewport.Background = GetThemeDefaultDesktopBackground();
            return;
        }

        if (_wallpaperBitmap is null)
        {
            WallpaperPreviewViewport.Background = GetThemeDefaultDesktopBackground();
            return;
        }

        WallpaperPreviewViewport.Background = CreateWallpaperBrush(_wallpaperBitmap, GetSelectedWallpaperPlacement(), true);
    }

    private ImageBrush CreateWallpaperBrush(Bitmap bitmap, WallpaperPlacement placement, bool forPreview)
    {
        var brush = new ImageBrush
        {
            Source = bitmap,
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TileMode = TileMode.None
        };

        if (forPreview && placement == WallpaperPlacement.Center)
        {
            const double nominalScreenWidth = 1920.0;
            const double previewWidth = 480.0;
            brush.Transform = new ScaleTransform(previewWidth / nominalScreenWidth, previewWidth / nominalScreenWidth);
        }

        switch (placement)
        {
            case WallpaperPlacement.Fit:
                brush.Stretch = Stretch.Uniform;
                break;
            case WallpaperPlacement.Stretch:
                brush.Stretch = Stretch.Fill;
                break;
            case WallpaperPlacement.Center:
                brush.Stretch = Stretch.None;
                break;
            case WallpaperPlacement.Tile:
                brush.Stretch = Stretch.None;
                brush.TileMode = TileMode.Tile;
                var tileSize = forPreview ? 96d : 220d;
                brush.DestinationRect = new RelativeRect(0, 0, tileSize, tileSize, RelativeUnit.Absolute);
                break;
        }

        return brush;
    }

    private WallpaperPlacement GetSelectedWallpaperPlacement()
    {
        return WallpaperPlacementComboBox.SelectedIndex switch
        {
            1 => WallpaperPlacement.Fit,
            2 => WallpaperPlacement.Stretch,
            3 => WallpaperPlacement.Center,
            4 => WallpaperPlacement.Tile,
            _ => WallpaperPlacement.Fill
        };
    }

    private static string GetPlacementDisplayName(WallpaperPlacement placement)
    {
        return placement switch
        {
            WallpaperPlacement.Fill => "Fill",
            WallpaperPlacement.Fit => "Fit",
            WallpaperPlacement.Stretch => "Stretch",
            WallpaperPlacement.Center => "Center",
            WallpaperPlacement.Tile => "Tile",
            _ => "Fill"
        };
    }

    private IBrush GetThemeDefaultDesktopBackground()
    {
        if (Resources.TryGetResource("AdaptiveSurfaceBaseBrush", ActualThemeVariant, out var resource) &&
            resource is IBrush themedBrush)
        {
            return themedBrush;
        }

        return _defaultDesktopBackground ??
               (_isNightMode
                   ? new SolidColorBrush(Color.Parse("#FF0B1220"))
                   : new SolidColorBrush(Color.Parse("#FFF3F7FB")));
    }

    private static int GetPlacementIndexFromSetting(string? placement)
    {
        if (string.IsNullOrWhiteSpace(placement))
        {
            return 0;
        }

        return placement.Trim().ToLowerInvariant() switch
        {
            "fit" => 1,
            "stretch" => 2,
            "center" => 3,
            "tile" => 4,
            _ => 0
        };
    }

    private void TryRestoreWallpaper(string? savedWallpaperPath)
    {
        StopVideoWallpaper();
        _wallpaperMediaType = WallpaperMediaType.None;
        _wallpaperVideoPath = null;
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        _wallpaperPath = null;

        if (string.IsNullOrWhiteSpace(savedWallpaperPath))
        {
            _wallpaperStatus = L("settings.wallpaper.default_status", "Current background uses solid color.");
            return;
        }

        if (!Path.IsPathRooted(savedWallpaperPath) || !File.Exists(savedWallpaperPath))
        {
            _wallpaperStatus = L("settings.wallpaper.saved_not_found", "Saved wallpaper file was not found. Using solid color background.");
            return;
        }

        try
        {
            var mediaType = DetectWallpaperMediaType(savedWallpaperPath);
            switch (mediaType)
            {
                case WallpaperMediaType.Image:
                    _wallpaperBitmap = new Bitmap(savedWallpaperPath);
                    _wallpaperPath = savedWallpaperPath;
                    _wallpaperMediaType = WallpaperMediaType.Image;
                    _wallpaperStatus = L("settings.wallpaper.restored", "Wallpaper restored from saved settings.");
                    break;
                case WallpaperMediaType.Video:
                    _wallpaperVideoPath = savedWallpaperPath;
                    _wallpaperPath = savedWallpaperPath;
                    _wallpaperMediaType = WallpaperMediaType.Video;
                    _wallpaperStatus = L("settings.wallpaper.video_restored", "Video wallpaper restored from saved settings.");
                    break;
                default:
                    _wallpaperStatus = L("settings.wallpaper.unsupported_file", "Saved wallpaper type is not supported. Using solid color background.");
                    break;
            }
        }
        catch
        {
            _wallpaperStatus = L("settings.wallpaper.restore_failed", "Failed to restore saved wallpaper. Using solid color background.");
            _wallpaperBitmap?.Dispose();
            _wallpaperBitmap = null;
            _wallpaperMediaType = WallpaperMediaType.None;
            _wallpaperVideoPath = null;
            _wallpaperPath = null;
        }
    }

    private static bool TryParseColor(string? colorText, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return false;
        }

        try
        {
            color = Color.Parse(colorText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static WallpaperMediaType DetectWallpaperMediaType(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return WallpaperMediaType.None;
        }

        if (SupportedImageExtensions.Contains(extension))
        {
            return WallpaperMediaType.Image;
        }

        if (SupportedVideoExtensions.Contains(extension))
        {
            return WallpaperMediaType.Video;
        }

        return WallpaperMediaType.None;
    }

    private static async Task<string?> ImportWallpaperAssetAsync(IStorageFile file)
    {
        try
        {
            var extension = Path.GetExtension(file.Name);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".bin";
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wallpaperDirectory = Path.Combine(appData, "LanMountainDesktop", "Wallpapers");
            Directory.CreateDirectory(wallpaperDirectory);

            var destinationPath = Path.Combine(wallpaperDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");
            await using var sourceStream = await file.OpenReadAsync();
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream);
            return destinationPath;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureVideoWallpaperPlayers()
    {
        Core.Initialize();
        _libVlc ??= new LibVLC("--quiet");
        if (_previewVideoWallpaperPlayer is null)
        {
            _previewVideoWallpaperPlayer = new MediaPlayer(_libVlc)
            {
                EnableHardwareDecoding = false
            };
        }
    }

    private void EnsurePreviewVideoFrameRefreshTimer()
    {
        if (_previewVideoFrameRefreshTimer is not null)
        {
            return;
        }

        _previewVideoFrameRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _previewVideoFrameRefreshTimer.Tick += OnPreviewVideoFrameRefreshTimerTick;
    }

    private void StartPreviewVideoFrameRefreshTimer()
    {
        EnsurePreviewVideoFrameRefreshTimer();
        if (_previewVideoFrameRefreshTimer?.IsEnabled == false)
        {
            _previewVideoFrameRefreshTimer.Start();
        }
    }

    private void StopPreviewVideoFrameRefreshTimer()
    {
        if (_previewVideoFrameRefreshTimer?.IsEnabled == true)
        {
            _previewVideoFrameRefreshTimer.Stop();
        }
    }

    private void OnPreviewVideoFrameRefreshTimerTick(object? sender, EventArgs e)
    {
        PushPreviewVideoFrameToWallpaperImage();
    }

    private void StopPreviewVideoCapture(bool clearSnapshot)
    {
        WallpaperPreviewVideoImage.IsVisible = false;
        _previewVideoWallpaperPlayer?.Stop();
        StopPreviewVideoFrameRefreshTimer();
        _previewVideoWallpaperMedia?.Dispose();
        _previewVideoWallpaperMedia = null;
        _previewVideoSnapshotPending = false;

        if (clearSnapshot)
        {
            ReleasePreviewVideoRendererResources();
        }
    }

    private bool ConfigurePreviewVideoRenderer()
    {
        if (_previewVideoWallpaperPlayer is null)
        {
            return false;
        }

        var hostWidth = Math.Max(1, WallpaperPreviewViewport.Bounds.Width);
        var hostHeight = Math.Max(1, WallpaperPreviewViewport.Bounds.Height);
        var pixelWidth = Math.Max(1, (int)Math.Round(hostWidth * RenderScaling));
        var pixelHeight = Math.Max(1, (int)Math.Round(hostHeight * RenderScaling));
        const int maxPixelCount = 1280 * 720;
        var pixelCount = (long)pixelWidth * pixelHeight;
        if (pixelCount > maxPixelCount)
        {
            var scale = Math.Sqrt((double)maxPixelCount / pixelCount);
            pixelWidth = Math.Max(1, (int)Math.Round(pixelWidth * scale));
            pixelHeight = Math.Max(1, (int)Math.Round(pixelHeight * scale));
        }

        var pitch = pixelWidth * 4;
        var bufferSize = pitch * pixelHeight;
        if (bufferSize <= 0)
        {
            return false;
        }

        if (pixelWidth == _previewVideoFrameWidth &&
            pixelHeight == _previewVideoFrameHeight &&
            _previewVideoFrameBufferPtr != IntPtr.Zero &&
            _previewVideoBitmap is not null)
        {
            return true;
        }

        ReleasePreviewVideoRendererResources();

        try
        {
            _previewVideoFrameWidth = pixelWidth;
            _previewVideoFrameHeight = pixelHeight;
            _previewVideoFramePitch = pitch;
            _previewVideoFrameBufferSize = bufferSize;
            _previewVideoFrameBufferPtr = Marshal.AllocHGlobal(_previewVideoFrameBufferSize);
            _previewVideoStagingBuffer = new byte[_previewVideoFrameBufferSize];
            _previewVideoBitmap = new WriteableBitmap(
                new PixelSize(_previewVideoFrameWidth, _previewVideoFrameHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            EnsurePreviewVideoCallbacks();
            _previewVideoWallpaperPlayer.SetVideoCallbacks(
                _previewVideoLockCallback!,
                _previewVideoUnlockCallback!,
                _previewVideoDisplayCallback!);
            _previewVideoWallpaperPlayer.SetVideoFormat(
                "RV32",
                (uint)_previewVideoFrameWidth,
                (uint)_previewVideoFrameHeight,
                (uint)_previewVideoFramePitch);
            WallpaperPreviewVideoImage.Source = _previewVideoBitmap;
            return true;
        }
        catch
        {
            ReleasePreviewVideoRendererResources();
            return false;
        }
    }

    private void EnsurePreviewVideoCallbacks()
    {
        _previewVideoLockCallback ??= OnPreviewVideoFrameLock;
        _previewVideoUnlockCallback ??= OnPreviewVideoFrameUnlock;
        _previewVideoDisplayCallback ??= OnPreviewVideoFrameDisplay;
    }

    private IntPtr OnPreviewVideoFrameLock(IntPtr opaque, IntPtr planes)
    {
        Monitor.Enter(_previewVideoFrameSync);
        if (_previewVideoFrameBufferPtr == IntPtr.Zero)
        {
            Marshal.WriteIntPtr(planes, IntPtr.Zero);
            Monitor.Exit(_previewVideoFrameSync);
            return IntPtr.Zero;
        }

        Marshal.WriteIntPtr(planes, _previewVideoFrameBufferPtr);
        return IntPtr.Zero;
    }

    private void OnPreviewVideoFrameUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        if (Monitor.IsEntered(_previewVideoFrameSync))
        {
            Monitor.Exit(_previewVideoFrameSync);
        }
    }

    private void OnPreviewVideoFrameDisplay(IntPtr opaque, IntPtr picture)
    {
        Interlocked.Exchange(ref _previewVideoFrameDirtyFlag, 1);
    }

    private void PushPreviewVideoFrameToWallpaperImage()
    {
        if (Interlocked.Exchange(ref _previewVideoFrameDirtyFlag, 0) == 0)
        {
            return;
        }

        if (_previewVideoBitmap is null ||
            _previewVideoStagingBuffer is null ||
            _previewVideoFrameBufferPtr == IntPtr.Zero ||
            _previewVideoFrameBufferSize <= 0)
        {
            return;
        }

        lock (_previewVideoFrameSync)
        {
            if (_previewVideoFrameBufferPtr == IntPtr.Zero)
            {
                return;
            }

            Marshal.Copy(_previewVideoFrameBufferPtr, _previewVideoStagingBuffer, 0, _previewVideoFrameBufferSize);
        }

        using var framebuffer = _previewVideoBitmap.Lock();
        var rows = Math.Min(framebuffer.Size.Height, _previewVideoFrameHeight);
        var bytesPerRow = Math.Min(framebuffer.RowBytes, _previewVideoFramePitch);
        for (var row = 0; row < rows; row++)
        {
            var sourceOffset = row * _previewVideoFramePitch;
            var destinationPtr = IntPtr.Add(framebuffer.Address, row * framebuffer.RowBytes);
            Marshal.Copy(_previewVideoStagingBuffer, sourceOffset, destinationPtr, bytesPerRow);
        }

        if (!ReferenceEquals(WallpaperPreviewVideoImage.Source, _previewVideoBitmap))
        {
            WallpaperPreviewVideoImage.Source = _previewVideoBitmap;
        }

        if (_previewVideoSnapshotPending)
        {
            _previewVideoSnapshotPending = false;
            WallpaperPreviewVideoImage.IsVisible = WallpaperSettingsPanel.IsVisible;
            StopPreviewVideoCapture(clearSnapshot: false);
            WallpaperPreviewVideoImage.IsVisible = WallpaperSettingsPanel.IsVisible;
        }
    }

    private void ReleasePreviewVideoRendererResources()
    {
        Interlocked.Exchange(ref _previewVideoFrameDirtyFlag, 0);
        WallpaperPreviewVideoImage.Source = null;
        _previewVideoBitmap?.Dispose();
        _previewVideoBitmap = null;
        _previewVideoStagingBuffer = null;
        _previewVideoFrameWidth = 0;
        _previewVideoFrameHeight = 0;
        _previewVideoFramePitch = 0;
        _previewVideoFrameBufferSize = 0;

        lock (_previewVideoFrameSync)
        {
            if (_previewVideoFrameBufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_previewVideoFrameBufferPtr);
                _previewVideoFrameBufferPtr = IntPtr.Zero;
            }
        }
    }

    private void PlayVideoWallpaper(string videoPath)
    {
        if (!File.Exists(videoPath))
        {
            _wallpaperStatus = L("settings.wallpaper.video_not_found", "Video wallpaper file not found.");
            StopVideoWallpaper();
            return;
        }

        try
        {
            EnsureVideoWallpaperPlayers();
            if (_previewVideoWallpaperPlayer is null || _libVlc is null)
            {
                _wallpaperStatus = L("settings.wallpaper.video_player_unavailable", "Video player is unavailable.");
                StopVideoWallpaper();
                return;
            }

            if (!ConfigurePreviewVideoRenderer())
            {
                _wallpaperStatus = L("settings.wallpaper.video_player_unavailable", "Video player is unavailable.");
                StopVideoWallpaper();
                return;
            }

            _previewVideoWallpaperMedia?.Dispose();
            _previewVideoWallpaperMedia = new Media(_libVlc, new Uri(videoPath));
            _previewVideoWallpaperMedia.AddOption(":input-repeat=65535");
            _previewVideoSnapshotPending = true;
            WallpaperPreviewVideoImage.IsVisible = false;
            _previewVideoWallpaperPlayer.Play(_previewVideoWallpaperMedia);
            StartPreviewVideoFrameRefreshTimer();
        }
        catch (Exception ex)
        {
            _wallpaperStatus = Lf("settings.wallpaper.video_play_failed_format", "Failed to play video wallpaper: {0}", ex.Message);
            StopVideoWallpaper();
        }
    }

    private void StopVideoWallpaper()
    {
        StopPreviewVideoCapture(clearSnapshot: true);
    }

    private void OnRecommendedColorClick(object? sender, RoutedEventArgs e)
    {
        ApplyThemeColorFromButton(sender as Button, L("common.recommended", "Recommended"));
    }

    private void OnMonetColorClick(object? sender, RoutedEventArgs e)
    {
        ApplyThemeColorFromButton(sender as Button, L("common.monet", "Monet"));
    }

    private void OnRefreshMonetColorsClick(object? sender, RoutedEventArgs e)
    {
        RefreshColorPalettes();
        EnsureSelectedThemeColor();
        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = L("settings.color.monet_refreshed", "Monet colors refreshed.");
        UpdateAdaptiveTextSystem();
        PersistSettings();
    }

    private void ApplyNightModeState(bool enabled, bool refreshPalettes)
    {
        _isNightMode = enabled;
        RequestedThemeVariant = enabled ? ThemeVariant.Dark : ThemeVariant.Light;
        UpdateThemeModeIcon();

        _suppressThemeToggleEvents = true;
        NightModeToggleSwitch.IsChecked = enabled;
        _suppressThemeToggleEvents = false;

        if (refreshPalettes)
        {
            RefreshColorPalettes();
            EnsureSelectedThemeColor();
        }

        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = Lf("settings.color.mode_status_format", "Theme mode: {0}.", enabled ? L("common.night", "Night") : L("common.day", "Day"));
        UpdateAdaptiveTextSystem();
        ApplyWallpaperBrush();
        PersistSettings();
    }

    private void RefreshColorPalettes()
    {
        var palette = _monetColorService.BuildPalette(_wallpaperBitmap, _isNightMode);
        _recommendedColors = palette.RecommendedColors;
        _monetColors = palette.MonetColors;
        ApplyColorPaletteToButtons(_recommendedColors, GetRecommendedColorTargets());
        ApplyColorPaletteToButtons(_monetColors, GetMonetColorTargets());
    }

    private void ApplyColorPaletteToButtons(IReadOnlyList<Color> colors, IReadOnlyList<(Button Button, Border Swatch)> targets)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var color = i < colors.Count ? colors[i] : Color.Parse("#00000000");
            var (button, swatch) = targets[i];
            button.Tag = color.ToString();
            button.IsEnabled = i < colors.Count;
            swatch.Background = i < colors.Count ? new SolidColorBrush(color) : new SolidColorBrush(Color.Parse("#00000000"));
        }
    }

    private IReadOnlyList<(Button Button, Border Swatch)> GetRecommendedColorTargets()
    {
        return
        [
            (RecommendedColorButton1, RecommendedColorSwatch1),
            (RecommendedColorButton2, RecommendedColorSwatch2),
            (RecommendedColorButton3, RecommendedColorSwatch3),
            (RecommendedColorButton4, RecommendedColorSwatch4),
            (RecommendedColorButton5, RecommendedColorSwatch5),
            (RecommendedColorButton6, RecommendedColorSwatch6)
        ];
    }

    private IReadOnlyList<(Button Button, Border Swatch)> GetMonetColorTargets()
    {
        return
        [
            (MonetColorButton1, MonetColorSwatch1),
            (MonetColorButton2, MonetColorSwatch2),
            (MonetColorButton3, MonetColorSwatch3),
            (MonetColorButton4, MonetColorSwatch4),
            (MonetColorButton5, MonetColorSwatch5),
            (MonetColorButton6, MonetColorSwatch6)
        ];
    }

    private void EnsureSelectedThemeColor()
    {
        if (ContainsColor(_recommendedColors, _selectedThemeColor) || ContainsColor(_monetColors, _selectedThemeColor))
        {
            return;
        }

        if (_recommendedColors.Count > 0)
        {
            _selectedThemeColor = _recommendedColors[0];
            return;
        }

        if (_monetColors.Count > 0)
        {
            _selectedThemeColor = _monetColors[0];
        }
    }

    private void ApplyThemeColorFromButton(Button? button, string sourceLabel)
    {
        if (!TryGetButtonColor(button, out var color))
        {
            return;
        }

        _selectedThemeColor = color;
        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = Lf("settings.color.theme_applied_format", "{0} color applied: {1}.", sourceLabel, _selectedThemeColor);
        UpdateAdaptiveTextSystem();
        PersistSettings();
    }

    private void UpdateThemeColorSelectionState()
    {
        UpdateColorSelectionVisuals(GetRecommendedColorTargets());
        UpdateColorSelectionVisuals(GetMonetColorTargets());
    }

    private void UpdateColorSelectionVisuals(IReadOnlyList<(Button Button, Border Swatch)> targets)
    {
        foreach (var (button, swatch) in targets)
        {
            var isSelected = TryGetButtonColor(button, out var color) && AreSameColor(color, _selectedThemeColor);
            button.Classes.Set("swatch-button", true);
            button.Classes.Set("swatch-selected", isSelected);
            swatch.BorderThickness = new Thickness(0);
            swatch.Opacity = isSelected ? 1 : 0.9;
        }
    }

    private static bool TryGetButtonColor(Button? button, out Color color)
    {
        color = default;
        if (button?.Tag is not string colorText || string.IsNullOrWhiteSpace(colorText))
        {
            return false;
        }

        try
        {
            color = Color.Parse(colorText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsColor(IReadOnlyList<Color> colors, Color target)
    {
        for (var i = 0; i < colors.Count; i++)
        {
            if (AreSameColor(colors[i], target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreSameColor(Color left, Color right)
    {
        return left.R == right.R && left.G == right.G && left.B == right.B;
    }

    private void UpdateAdaptiveTextSystem()
    {
        var context = new ThemeColorContext(_selectedThemeColor, !_isNightMode, !_isNightMode, _isNightMode);
        ThemeColorSystemService.ApplyThemeResources(Resources, context);
        GlassEffectService.ApplyGlassResources(Resources, context);
        if (_fluentAvaloniaTheme is not null)
        {
            _fluentAvaloniaTheme.CustomAccentColor = _selectedThemeColor;
        }
    }

    private double CalculateCurrentBackgroundLuminance()
    {
        if (_wallpaperMediaType == WallpaperMediaType.Video)
        {
            return CalculateRelativeLuminance(Color.Parse("#FF0B1220"));
        }

        if (_wallpaperBitmap is not null)
        {
            return CalculateBitmapAverageLuminance(_wallpaperBitmap);
        }

        return CalculateBrushLuminance(DesktopWallpaperLayer.Background ?? _defaultDesktopBackground);
    }

    private static double CalculateBrushLuminance(IBrush? brush)
    {
        if (brush is ISolidColorBrush solidBrush)
        {
            return CalculateRelativeLuminance(solidBrush.Color);
        }

        return CalculateRelativeLuminance(Color.Parse("#FF020617"));
    }

    private static double CalculateBitmapAverageLuminance(Bitmap bitmap)
    {
        try
        {
            var sampleWidth = Math.Clamp(bitmap.PixelSize.Width, 1, 48);
            var sampleHeight = Math.Clamp(bitmap.PixelSize.Height, 1, 48);
            using var scaledBitmap = bitmap.CreateScaledBitmap(new PixelSize(sampleWidth, sampleHeight), BitmapInterpolationMode.MediumQuality);
            using var writeable = new WriteableBitmap(scaledBitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var framebuffer = writeable.Lock();
            scaledBitmap.CopyPixels(framebuffer, AlphaFormat.Premul);

            var rowBytes = framebuffer.RowBytes;
            var byteCount = rowBytes * framebuffer.Size.Height;
            if (byteCount <= 0 || framebuffer.Address == IntPtr.Zero)
            {
                return CalculateRelativeLuminance(Color.Parse("#FF020617"));
            }

            var pixelBuffer = new byte[byteCount];
            Marshal.Copy(framebuffer.Address, pixelBuffer, 0, byteCount);

            double luminanceSum = 0;
            var pixelCount = 0;
            for (var y = 0; y < framebuffer.Size.Height; y++)
            {
                var rowOffset = y * rowBytes;
                for (var x = 0; x < framebuffer.Size.Width; x++)
                {
                    var index = rowOffset + (x * 4);
                    var alpha = pixelBuffer[index + 3] / 255d;
                    if (alpha <= 0.01)
                    {
                        continue;
                    }

                    var blue = Math.Clamp((pixelBuffer[index] / 255d) / alpha, 0, 1);
                    var green = Math.Clamp((pixelBuffer[index + 1] / 255d) / alpha, 0, 1);
                    var red = Math.Clamp((pixelBuffer[index + 2] / 255d) / alpha, 0, 1);
                    luminanceSum += CalculateRelativeLuminance(red, green, blue);
                    pixelCount++;
                }
            }

            return pixelCount > 0 ? luminanceSum / pixelCount : CalculateRelativeLuminance(Color.Parse("#FF020617"));
        }
        catch
        {
            return CalculateRelativeLuminance(Color.Parse("#FF020617"));
        }
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        return CalculateRelativeLuminance(color.R / 255d, color.G / 255d, color.B / 255d);
    }

    private static double CalculateRelativeLuminance(double red, double green, double blue)
    {
        var linearRed = ToLinearRgb(red);
        var linearGreen = ToLinearRgb(green);
        var linearBlue = ToLinearRgb(blue);
        return (0.2126 * linearRed) + (0.7152 * linearGreen) + (0.0722 * linearBlue);
    }

    private static double ToLinearRgb(double value)
    {
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
