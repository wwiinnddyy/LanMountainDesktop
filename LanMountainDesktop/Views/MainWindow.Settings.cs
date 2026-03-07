using System;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using LanMountainDesktop.Views.Components;

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LibVLCSharp.Shared;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_isComponentLibraryOpen)
        {
            CloseComponentLibraryWindow(reopenSettings: false);
        }

        if (_isSettingsOpen)
        {
            CloseSettingsPage();
            return;
        }

        OpenSettingsPage();
    }

    private void OnCloseSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseSettingsPage();
    }

    private void OnSettingsNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSettingsTabContent();
        PersistSettings();
    }

    private void UpdateSettingsTabContent()
    {
        // SelectionChanged can fire during XAML initialization before all named controls are assigned.
        if (SettingsNavListBox is null ||
            GridSettingsPanel is null ||
            WallpaperSettingsPanel is null ||
            ColorSettingsPanel is null ||
            StatusBarSettingsPanel is null ||
            WeatherSettingsPanel is null ||
            RegionSettingsPanel is null ||
            UpdateSettingsPanel is null ||
            LauncherSettingsPanel is null ||
            AboutSettingsPanel is null ||
            PluginSettingsPanel is null)
        {
            return;
        }

        var selectedIndex = SettingsNavListBox.SelectedIndex;
        WallpaperSettingsPanel.IsVisible = selectedIndex == 0;
        GridSettingsPanel.IsVisible = selectedIndex == 1;
        ColorSettingsPanel.IsVisible = selectedIndex == 2;
        StatusBarSettingsPanel.IsVisible = selectedIndex == 3;
        WeatherSettingsPanel.IsVisible = selectedIndex == 4;
        RegionSettingsPanel.IsVisible = selectedIndex == 5;
        UpdateSettingsPanel.IsVisible = selectedIndex == 6;
        AboutSettingsPanel.IsVisible = selectedIndex == 7;
        LauncherSettingsPanel.IsVisible = selectedIndex == 8;
        PluginSettingsPanel.IsVisible = selectedIndex == 9;

        if (selectedIndex == 8)
        {
            RenderLauncherHiddenItemsList();
        }

        if (selectedIndex == 1)
        {
            UpdateGridPreviewLayout();
        }

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

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
                new FilePickerFileType(L("filepicker.image_files", "Image files"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                },
                new FilePickerFileType(L("filepicker.video_files", "Video files"))
                {
                    Patterns = ["*.mp4", "*.mkv", "*.webm", "*.avi", "*.mov", "*.m4v"]
                }
            ]
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            return;
        }

        var file = files[0];
        try
        {
            var importedPath = await ImportWallpaperAssetAsync(file);
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
            _wallpaperStatus = Lf(
                "settings.wallpaper.mode_format",
                "Wallpaper mode: {0}.",
                GetLocalizedPlacementDisplayName(GetSelectedWallpaperPlacement()));
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
        if (_wallpaperMediaType == WallpaperMediaType.Video &&
            !string.IsNullOrWhiteSpace(_wallpaperVideoPath))
        {
            DesktopWallpaperLayer.Background = Brushes.Transparent;
            WallpaperPreviewViewport.Background = GetThemeDefaultDesktopBackground();
            PlayVideoWallpaper(_wallpaperVideoPath);
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

    private void UpdateWallpaperDisplay()
    {
        if (WallpaperPathTextBlock is null ||
            WallpaperStatusTextBlock is null ||
            WallpaperPreviewViewport is null ||
            WallpaperPlacementComboBox is null)
        {
            return;
        }

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

        WallpaperPreviewViewport.Background = CreateWallpaperBrush(
            _wallpaperBitmap,
            GetSelectedWallpaperPlacement(),
            true);
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

        if (forPreview)
        {
            // For preview, we want to simulate how the image looks on a real screen.
            // Assuming a nominal screen width of 1920 for calculation.
            // The preview width is 480, so the scale is 480/1920 = 0.25.
            const double nominalScreenWidth = 1920.0;
            const double previewWidth = 480.0;
            double scale = previewWidth / nominalScreenWidth;

            if (placement == WallpaperPlacement.Center)
            {
                brush.Transform = new ScaleTransform(scale, scale);
            }
        }

        switch (placement)
        {
            case WallpaperPlacement.Fill:
                brush.Stretch = Stretch.UniformToFill;
                break;
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
        return WallpaperPlacementComboBox?.SelectedIndex switch
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
            _wallpaperStatus = L(
                "settings.wallpaper.saved_not_found",
                "Saved wallpaper file was not found. Using solid color background.");
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
                    _wallpaperStatus = L(
                        "settings.wallpaper.unsupported_file",
                        "Saved wallpaper type is not supported. Using solid color background.");
                    break;
            }
        }
        catch
        {
            _wallpaperStatus = L(
                "settings.wallpaper.restore_failed",
                "Failed to restore saved wallpaper. Using solid color background.");
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

            var destinationPath = Path.Combine(
                wallpaperDirectory,
                $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");

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

        if (_videoWallpaperPlayer is null)
        {
            _videoWallpaperPlayer = new MediaPlayer(_libVlc)
            {
                EnableHardwareDecoding = false
            };
        }

        if (_previewVideoWallpaperPlayer is null && WallpaperPreviewVideoView is not null)
        {
            _previewVideoWallpaperPlayer = new MediaPlayer(_libVlc);
            WallpaperPreviewVideoView.MediaPlayer = _previewVideoWallpaperPlayer;
        }
    }

    private bool ConfigureDesktopVideoRenderer()
    {
        if (_videoWallpaperPlayer is null || DesktopVideoWallpaperImage is null)
        {
            return false;
        }

        var (targetWidth, targetHeight) = GetDesktopVideoRenderSize();
        var targetPitch = targetWidth * 4;
        var targetBufferSize = targetPitch * targetHeight;
        if (targetBufferSize <= 0)
        {
            return false;
        }

        if (targetWidth == _desktopVideoFrameWidth &&
            targetHeight == _desktopVideoFrameHeight &&
            _desktopVideoFrameBufferPtr != IntPtr.Zero &&
            _desktopVideoBitmap is not null)
        {
            return true;
        }

        ReleaseDesktopVideoRendererResources();

        try
        {
            _desktopVideoFrameWidth = targetWidth;
            _desktopVideoFrameHeight = targetHeight;
            _desktopVideoFramePitch = targetPitch;
            _desktopVideoFrameBufferSize = targetBufferSize;
            _desktopVideoFrameBufferPtr = Marshal.AllocHGlobal(_desktopVideoFrameBufferSize);
            _desktopVideoStagingBuffer = new byte[_desktopVideoFrameBufferSize];
            _desktopVideoBitmap = new WriteableBitmap(
                new PixelSize(_desktopVideoFrameWidth, _desktopVideoFrameHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            EnsureDesktopVideoCallbacks();
            _videoWallpaperPlayer.SetVideoCallbacks(
                _desktopVideoLockCallback!,
                _desktopVideoUnlockCallback!,
                _desktopVideoDisplayCallback!);
            _videoWallpaperPlayer.SetVideoFormat(
                "RV32",
                (uint)_desktopVideoFrameWidth,
                (uint)_desktopVideoFrameHeight,
                (uint)_desktopVideoFramePitch);
            DesktopVideoWallpaperImage.Source = _desktopVideoBitmap;
            return true;
        }
        catch
        {
            ReleaseDesktopVideoRendererResources();
            return false;
        }
    }

    private (int Width, int Height) GetDesktopVideoRenderSize()
    {
        var hostWidth = DesktopHost?.Bounds.Width ?? Bounds.Width;
        var hostHeight = DesktopHost?.Bounds.Height ?? Bounds.Height;
        var pixelWidth = Math.Max(1, (int)Math.Round(hostWidth * RenderScaling));
        var pixelHeight = Math.Max(1, (int)Math.Round(hostHeight * RenderScaling));

        const int maxPixelCount = 1920 * 1080;
        var pixelCount = (long)pixelWidth * pixelHeight;
        if (pixelCount > maxPixelCount)
        {
            var scale = Math.Sqrt((double)maxPixelCount / pixelCount);
            pixelWidth = Math.Max(1, (int)Math.Round(pixelWidth * scale));
            pixelHeight = Math.Max(1, (int)Math.Round(pixelHeight * scale));
        }

        return (pixelWidth, pixelHeight);
    }

    private void EnsureDesktopVideoCallbacks()
    {
        _desktopVideoLockCallback ??= OnDesktopVideoFrameLock;
        _desktopVideoUnlockCallback ??= OnDesktopVideoFrameUnlock;
        _desktopVideoDisplayCallback ??= OnDesktopVideoFrameDisplay;
    }

    private IntPtr OnDesktopVideoFrameLock(IntPtr opaque, IntPtr planes)
    {
        Monitor.Enter(_desktopVideoFrameSync);
        if (_desktopVideoFrameBufferPtr == IntPtr.Zero)
        {
            Marshal.WriteIntPtr(planes, IntPtr.Zero);
            Monitor.Exit(_desktopVideoFrameSync);
            return IntPtr.Zero;
        }

        Marshal.WriteIntPtr(planes, _desktopVideoFrameBufferPtr);
        return IntPtr.Zero;
    }

    private void OnDesktopVideoFrameUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        if (Monitor.IsEntered(_desktopVideoFrameSync))
        {
            Monitor.Exit(_desktopVideoFrameSync);
        }
    }

    private void OnDesktopVideoFrameDisplay(IntPtr opaque, IntPtr picture)
    {
        Interlocked.Exchange(ref _desktopVideoFrameDirtyFlag, 1);
        ScheduleDesktopVideoFrameUiRefresh();
    }

    private void ScheduleDesktopVideoFrameUiRefresh()
    {
        if (Interlocked.Exchange(ref _desktopVideoFrameUiRefreshScheduledFlag, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                PushDesktopVideoFrameToWallpaperImage();
            }
            finally
            {
                Interlocked.Exchange(ref _desktopVideoFrameUiRefreshScheduledFlag, 0);
                if (Volatile.Read(ref _desktopVideoFrameDirtyFlag) == 1)
                {
                    ScheduleDesktopVideoFrameUiRefresh();
                }
            }
        }, DispatcherPriority.Render);
    }

    private void PushDesktopVideoFrameToWallpaperImage()
    {
        if (Interlocked.Exchange(ref _desktopVideoFrameDirtyFlag, 0) == 0)
        {
            return;
        }

        if (_desktopVideoBitmap is null ||
            _desktopVideoStagingBuffer is null ||
            _desktopVideoFrameBufferPtr == IntPtr.Zero ||
            _desktopVideoFrameBufferSize <= 0)
        {
            return;
        }

        lock (_desktopVideoFrameSync)
        {
            if (_desktopVideoFrameBufferPtr == IntPtr.Zero)
            {
                return;
            }

            Marshal.Copy(_desktopVideoFrameBufferPtr, _desktopVideoStagingBuffer, 0, _desktopVideoFrameBufferSize);
        }

        using var framebuffer = _desktopVideoBitmap.Lock();
        var rows = Math.Min(framebuffer.Size.Height, _desktopVideoFrameHeight);
        var bytesPerRow = Math.Min(framebuffer.RowBytes, _desktopVideoFramePitch);
        for (var row = 0; row < rows; row++)
        {
            var sourceOffset = row * _desktopVideoFramePitch;
            var destinationPtr = IntPtr.Add(framebuffer.Address, row * framebuffer.RowBytes);
            Marshal.Copy(_desktopVideoStagingBuffer, sourceOffset, destinationPtr, bytesPerRow);
        }

        if (DesktopVideoWallpaperImage is not null &&
            !ReferenceEquals(DesktopVideoWallpaperImage.Source, _desktopVideoBitmap))
        {
            DesktopVideoWallpaperImage.Source = _desktopVideoBitmap;
        }
    }

    private void ReleaseDesktopVideoRendererResources()
    {
        Interlocked.Exchange(ref _desktopVideoFrameDirtyFlag, 0);
        Interlocked.Exchange(ref _desktopVideoFrameUiRefreshScheduledFlag, 0);

        if (DesktopVideoWallpaperImage is not null)
        {
            DesktopVideoWallpaperImage.Source = null;
        }

        _desktopVideoBitmap?.Dispose();
        _desktopVideoBitmap = null;
        _desktopVideoStagingBuffer = null;
        _desktopVideoFrameWidth = 0;
        _desktopVideoFrameHeight = 0;
        _desktopVideoFramePitch = 0;
        _desktopVideoFrameBufferSize = 0;

        lock (_desktopVideoFrameSync)
        {
            if (_desktopVideoFrameBufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_desktopVideoFrameBufferPtr);
                _desktopVideoFrameBufferPtr = IntPtr.Zero;
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
            if (_videoWallpaperPlayer is null ||
                _previewVideoWallpaperPlayer is null ||
                _libVlc is null ||
                DesktopVideoWallpaperImage is null ||
                WallpaperPreviewVideoView is null)
            {
                _wallpaperStatus = L("settings.wallpaper.video_player_unavailable", "Video player is unavailable.");
                StopVideoWallpaper();
                return;
            }

            if (!ConfigureDesktopVideoRenderer())
            {
                _wallpaperStatus = L("settings.wallpaper.video_player_unavailable", "Video player is unavailable.");
                StopVideoWallpaper();
                return;
            }

            _videoWallpaperMedia?.Dispose();
            _previewVideoWallpaperMedia?.Dispose();
            _videoWallpaperMedia = new Media(_libVlc, new Uri(videoPath));
            _previewVideoWallpaperMedia = new Media(_libVlc, new Uri(videoPath));
            _videoWallpaperMedia.AddOption(":input-repeat=65535");
            _previewVideoWallpaperMedia.AddOption(":input-repeat=65535");
            _videoWallpaperPlayer.Play(_videoWallpaperMedia);
            _previewVideoWallpaperPlayer.Play(_previewVideoWallpaperMedia);
            DesktopVideoWallpaperImage.IsVisible = true;
            WallpaperPreviewVideoView.IsVisible = true;
        }
        catch (Exception ex)
        {
            _wallpaperStatus = Lf("settings.wallpaper.video_play_failed_format", "Failed to play video wallpaper: {0}", ex.Message);
            StopVideoWallpaper();
        }
    }

    private void StopVideoWallpaper()
    {
        if (DesktopVideoWallpaperImage is not null)
        {
            DesktopVideoWallpaperImage.IsVisible = false;
        }

        if (WallpaperPreviewVideoView is not null)
        {
            WallpaperPreviewVideoView.IsVisible = false;
        }

        if (_videoWallpaperPlayer is not null)
        {
            _videoWallpaperPlayer.Stop();
        }

        if (_previewVideoWallpaperPlayer is not null)
        {
            _previewVideoWallpaperPlayer.Stop();
        }

        ReleaseDesktopVideoRendererResources();
        _videoWallpaperMedia?.Dispose();
        _videoWallpaperMedia = null;
        _previewVideoWallpaperMedia?.Dispose();
        _previewVideoWallpaperMedia = null;
    }

    private void PersistSettings()
    {
        if (_suppressSettingsPersistence)
        {
            return;
        }

        _appSettingsService.Save(BuildAppSettingsSnapshot());
        _desktopLayoutSettingsService.Save(BuildDesktopLayoutSettingsSnapshot());
        _launcherSettingsService.Save(BuildLauncherSettingsSnapshot());
    }

    private AppSettingsSnapshot BuildAppSettingsSnapshot()
    {
        return new AppSettingsSnapshot
        {
            GridShortSideCells = _targetShortSideCells,
            GridSpacingPreset = _gridSpacingPreset,
            DesktopEdgeInsetPercent = _desktopEdgeInsetPercent,
            IsNightMode = _isNightMode,
            ThemeColor = _selectedThemeColor.ToString(),
            WallpaperPath = _wallpaperPath,
            WallpaperPlacement = GetPlacementDisplayName(GetSelectedWallpaperPlacement()),
            SettingsTabIndex = Math.Max(0, SettingsNavListBox?.SelectedIndex ?? 0),
            LanguageCode = _languageCode,
            TimeZoneId = _timeZoneService.CurrentTimeZone.Id,
            WeatherLocationMode = ToWeatherLocationModeTag(_weatherLocationMode),
            WeatherLocationKey = _weatherLocationKey,
            WeatherLocationName = _weatherLocationName,
            WeatherLatitude = _weatherLatitude,
            WeatherLongitude = _weatherLongitude,
            WeatherAutoRefreshLocation = _weatherAutoRefreshLocation,
            WeatherLocationQuery = BuildLegacyWeatherLocationQuery(),
            WeatherExcludedAlerts = _weatherExcludedAlertsRaw,
            WeatherIconPackId = _weatherIconPackId,
            WeatherNoTlsRequests = _weatherNoTlsRequests,
            AutoStartWithWindows = _autoStartWithWindows,
            AutoCheckUpdates = _autoCheckUpdates,
            IncludePrereleaseUpdates = IncludePrereleaseUpdates,
            UpdateChannel = IncludePrereleaseUpdates ? "Preview" : "Stable",
            TopStatusComponentIds = _topStatusComponentIds.ToList(),
            PinnedTaskbarActions = _pinnedTaskbarActions.Select(action => action.ToString()).ToList(),
            EnableDynamicTaskbarActions = _enableDynamicTaskbarActions,
            TaskbarLayoutMode = _taskbarLayoutMode,
            ClockDisplayFormat = _clockDisplayFormat == ClockDisplayFormat.HourMinute ? "HourMinute" : "HourMinuteSecond",
            StatusBarSpacingMode = _statusBarSpacingMode,
            StatusBarCustomSpacingPercent = _statusBarCustomSpacingPercent
        };
    }

    private DesktopLayoutSettingsSnapshot BuildDesktopLayoutSettingsSnapshot()
    {
        return new DesktopLayoutSettingsSnapshot
        {
            DesktopPageCount = _desktopPageCount,
            CurrentDesktopSurfaceIndex = _currentDesktopSurfaceIndex,
            DesktopComponentPlacements = _desktopComponentPlacements.ToList()
        };
    }

    private LauncherSettingsSnapshot BuildLauncherSettingsSnapshot()
    {
        return new LauncherSettingsSnapshot
        {
            HiddenLauncherFolderPaths = _hiddenLauncherFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
            HiddenLauncherAppPaths = _hiddenLauncherAppPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private IDisposable? _persistSettingsDebounceTimer;

    private void SchedulePersistSettings(int delayMs = 200)
    {
        if (_suppressSettingsPersistence)
        {
            return;
        }

        _persistSettingsDebounceTimer?.Dispose();
        _persistSettingsDebounceTimer = DispatcherTimer.RunOnce(() =>
        {
            _persistSettingsDebounceTimer = null;
            PersistSettings();
        }, TimeSpan.FromMilliseconds(Math.Max(0, delayMs)));
    }

    private void InitializeWeatherSettings(AppSettingsSnapshot snapshot)
    {
        _suppressWeatherLocationEvents = true;
        try
        {
            _weatherLocationMode = ParseWeatherLocationMode(snapshot.WeatherLocationMode);
            _weatherLocationKey = snapshot.WeatherLocationKey?.Trim() ?? string.Empty;
            _weatherLocationName = snapshot.WeatherLocationName?.Trim() ?? string.Empty;
            _weatherLatitude = NormalizeLatitude(snapshot.WeatherLatitude);
            _weatherLongitude = NormalizeLongitude(snapshot.WeatherLongitude);
            _weatherAutoRefreshLocation = snapshot.WeatherAutoRefreshLocation;
            _weatherExcludedAlertsRaw = snapshot.WeatherExcludedAlerts?.Trim() ?? string.Empty;
            _weatherIconPackId = string.IsNullOrWhiteSpace(snapshot.WeatherIconPackId)
                ? "FluentRegular"
                : snapshot.WeatherIconPackId.Trim();
            _weatherNoTlsRequests = snapshot.WeatherNoTlsRequests;
            _weatherSearchKeyword = string.Empty;

            var legacyQuery = snapshot.WeatherLocationQuery?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_weatherLocationKey) && !string.IsNullOrWhiteSpace(legacyQuery))
            {
                _weatherLocationKey = legacyQuery;
            }

            if (string.IsNullOrWhiteSpace(_weatherLocationName) && !string.IsNullOrWhiteSpace(legacyQuery))
            {
                _weatherLocationName = legacyQuery;
            }

            SelectWeatherLocationModeInUi(_weatherLocationMode);
            if (WeatherAutoRefreshToggleSwitch is not null)
            {
                WeatherAutoRefreshToggleSwitch.IsChecked = _weatherAutoRefreshLocation;
            }

            if (WeatherNoTlsToggleSwitch is not null)
            {
                WeatherNoTlsToggleSwitch.IsChecked = _weatherNoTlsRequests;
            }

            if (WeatherCitySearchTextBox is not null)
            {
                WeatherCitySearchTextBox.Text = string.Empty;
            }

            if (WeatherCityResultsComboBox is not null)
            {
                WeatherCityResultsComboBox.Items.Clear();
            }

            if (WeatherLocationKeyTextBox is not null)
            {
                WeatherLocationKeyTextBox.Text = _weatherLocationKey;
            }

            if (WeatherLocationNameTextBox is not null)
            {
                WeatherLocationNameTextBox.Text = _weatherLocationName;
            }

            if (WeatherLatitudeNumberBox is not null)
            {
                WeatherLatitudeNumberBox.Value = _weatherLatitude;
            }

            if (WeatherLongitudeNumberBox is not null)
            {
                WeatherLongitudeNumberBox.Value = _weatherLongitude;
            }

            if (WeatherExcludedAlertsTextBox is not null)
            {
                WeatherExcludedAlertsTextBox.Text = _weatherExcludedAlertsRaw;
            }

            SelectWeatherIconPackInUi(_weatherIconPackId);

            if (WeatherSearchStatusTextBlock is not null)
            {
                WeatherSearchStatusTextBlock.Text = L(
                    "settings.weather.search_hint",
                    "Search by city name and apply one location.");
            }

            if (WeatherCoordinateStatusTextBlock is not null)
            {
                WeatherCoordinateStatusTextBlock.Text = string.Empty;
            }

            if (WeatherPreviewResultTextBlock is not null)
            {
                WeatherPreviewResultTextBlock.Text = L(
                    "settings.weather.preview_hint",
                    "Use test fetch to verify your weather configuration.");
            }

            UpdateWeatherPreviewSummary(
                weatherCode: null,
                temperatureText: "--",
                updatedAt: null);
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
    }

    private void InitializeAutoStartWithWindowsSetting(AppSettingsSnapshot snapshot)
    {
        _autoStartWithWindows = OperatingSystem.IsWindows()
            ? _windowsStartupService.IsEnabled()
            : snapshot.AutoStartWithWindows;

        if (AutoStartWithWindowsToggleSwitch is null)
        {
            return;
        }

        _suppressAutoStartToggleEvents = true;
        try
        {
            AutoStartWithWindowsToggleSwitch.IsEnabled = OperatingSystem.IsWindows();
            AutoStartWithWindowsToggleSwitch.IsChecked = _autoStartWithWindows;
        }
        finally
        {
            _suppressAutoStartToggleEvents = false;
        }
    }

    private static WeatherLocationMode ParseWeatherLocationMode(string? value)
    {
        return string.Equals(value, "Coordinates", StringComparison.OrdinalIgnoreCase)
            ? WeatherLocationMode.Coordinates
            : WeatherLocationMode.CitySearch;
    }

    private static string ToWeatherLocationModeTag(WeatherLocationMode mode)
    {
        return mode == WeatherLocationMode.Coordinates ? "Coordinates" : "CitySearch";
    }

    private static double NormalizeLatitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 39.9042;
        }

        return Math.Clamp(value, -90, 90);
    }

    private static double NormalizeLongitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 116.4074;
        }

        return Math.Clamp(value, -180, 180);
    }

    private string BuildLegacyWeatherLocationQuery()
    {
        if (!string.IsNullOrWhiteSpace(_weatherLocationName))
        {
            return _weatherLocationName;
        }

        if (!string.IsNullOrWhiteSpace(_weatherLocationKey))
        {
            return _weatherLocationKey;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_weatherLatitude:F4},{_weatherLongitude:F4}");
    }

    private void SelectWeatherLocationModeInUi(WeatherLocationMode mode)
    {
        var targetTag = ToWeatherLocationModeTag(mode);
        var selected = false;
        if (WeatherLocationModeComboBox is not null)
        {
            foreach (var item in WeatherLocationModeComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), targetTag, StringComparison.OrdinalIgnoreCase))
                {
                    WeatherLocationModeComboBox.SelectedItem = item;
                    selected = true;
                    break;
                }
            }

            if (!selected)
            {
                WeatherLocationModeComboBox.SelectedIndex = mode == WeatherLocationMode.Coordinates ? 1 : 0;
            }
        }

        if (WeatherLocationModeChipListBox is null)
        {
            return;
        }

        foreach (var item in WeatherLocationModeChipListBox.Items.OfType<ListBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), targetTag, StringComparison.OrdinalIgnoreCase))
            {
                WeatherLocationModeChipListBox.SelectedItem = item;
                return;
            }
        }

        WeatherLocationModeChipListBox.SelectedIndex = mode == WeatherLocationMode.Coordinates ? 1 : 0;
    }

    private void SelectWeatherIconPackInUi(string iconPackId)
    {
        if (WeatherIconPackComboBox is null)
        {
            return;
        }

        foreach (var item in WeatherIconPackComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), iconPackId, StringComparison.OrdinalIgnoreCase))
            {
                WeatherIconPackComboBox.SelectedItem = item;
                return;
            }
        }

        WeatherIconPackComboBox.SelectedIndex = 0;
        _weatherIconPackId = "FluentRegular";
    }

    private void UpdateWeatherLocationModePanels()
    {
        if (WeatherCitySearchSettingsExpander is not null)
        {
            WeatherCitySearchSettingsExpander.IsVisible = _weatherLocationMode == WeatherLocationMode.CitySearch;
        }

        if (WeatherCoordinateSettingsExpander is not null)
        {
            WeatherCoordinateSettingsExpander.IsVisible = _weatherLocationMode == WeatherLocationMode.Coordinates;
        }
    }

    private void OnWeatherLocationModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherLocationModeComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _weatherLocationMode = ParseWeatherLocationMode(item.Tag?.ToString());
        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }
        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private void OnWeatherLocationModeChipSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherLocationModeChipListBox?.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        _weatherLocationMode = ParseWeatherLocationMode(item.Tag?.ToString());
        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private void OnWeatherAutoRefreshToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherAutoRefreshToggleSwitch is null)
        {
            return;
        }

        _weatherAutoRefreshLocation = WeatherAutoRefreshToggleSwitch.IsChecked == true;
        PersistSettings();
    }

    private void OnWeatherExcludedAlertsLostFocus(object? sender, RoutedEventArgs e)
    {
        if (WeatherExcludedAlertsTextBox is null)
        {
            return;
        }

        _weatherExcludedAlertsRaw = WeatherExcludedAlertsTextBox.Text?.Trim() ?? string.Empty;
        PersistSettings();
    }

    private void OnWeatherIconPackSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherIconPackComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _weatherIconPackId = item.Tag?.ToString() switch
        {
            "FluentFilled" => "FluentFilled",
            _ => "FluentRegular"
        };

        if (WeatherPreviewIconSymbol is not null)
        {
            WeatherPreviewIconSymbol.IconVariant = string.Equals(_weatherIconPackId, "FluentFilled", StringComparison.OrdinalIgnoreCase)
                ? IconVariant.Filled
                : IconVariant.Regular;
        }

        PersistSettings();
    }

    private void OnWeatherNoTlsToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherNoTlsToggleSwitch is null)
        {
            return;
        }

        _weatherNoTlsRequests = WeatherNoTlsToggleSwitch.IsChecked == true;
        PersistSettings();
    }

    private void OnAutoStartWithWindowsToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressAutoStartToggleEvents || AutoStartWithWindowsToggleSwitch is null)
        {
            return;
        }

        var requested = AutoStartWithWindowsToggleSwitch.IsChecked == true;
        if (!OperatingSystem.IsWindows())
        {
            _autoStartWithWindows = false;
            _suppressAutoStartToggleEvents = true;
            try
            {
                AutoStartWithWindowsToggleSwitch.IsEnabled = false;
                AutoStartWithWindowsToggleSwitch.IsChecked = false;
            }
            finally
            {
                _suppressAutoStartToggleEvents = false;
            }

            PersistSettings();
            return;
        }

        var applied = _windowsStartupService.SetEnabled(requested);
        _autoStartWithWindows = _windowsStartupService.IsEnabled();

        if (!applied || _autoStartWithWindows != requested)
        {
            _suppressAutoStartToggleEvents = true;
            try
            {
                AutoStartWithWindowsToggleSwitch.IsChecked = _autoStartWithWindows;
            }
            finally
            {
                _suppressAutoStartToggleEvents = false;
            }
        }

        PersistSettings();
    }

    private async void OnSearchWeatherCityClick(object? sender, RoutedEventArgs e)
    {
        if (_isWeatherSearchInProgress || WeatherCitySearchTextBox is null || WeatherCityResultsComboBox is null)
        {
            return;
        }

        var keyword = WeatherCitySearchTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            if (WeatherSearchStatusTextBlock is not null)
            {
                WeatherSearchStatusTextBlock.Text = L(
                    "settings.weather.search_required",
                    "Please enter a city keyword first.");
            }

            return;
        }

        _weatherSearchKeyword = keyword;
        _isWeatherSearchInProgress = true;
        SetWeatherSearchBusy(isBusy: true);
        try
        {
            var result = await _weatherDataService.SearchLocationsAsync(keyword, ResolveWeatherApiLocale());
            if (!result.Success || result.Data is null)
            {
                WeatherCityResultsComboBox.Items.Clear();
                if (WeatherSearchStatusTextBlock is not null)
                {
                    WeatherSearchStatusTextBlock.Text = Lf(
                        "settings.weather.search_failed_format",
                        "Search failed: {0}",
                        result.ErrorMessage ?? result.ErrorCode ?? "Unknown error");
                }

                return;
            }

            var locations = result.Data
                .Where(location => !string.IsNullOrWhiteSpace(location.LocationKey))
                .Take(80)
                .ToList();

            WeatherCityResultsComboBox.Items.Clear();
            foreach (var location in locations)
            {
                WeatherCityResultsComboBox.Items.Add(new ComboBoxItem
                {
                    Content = FormatWeatherLocationDisplayName(location),
                    Tag = location
                });
            }

            if (WeatherSearchStatusTextBlock is not null)
            {
                WeatherSearchStatusTextBlock.Text = locations.Count == 0
                    ? L("settings.weather.search_no_results", "No locations were found.")
                    : Lf(
                        "settings.weather.search_result_count_format",
                        "Found {0} locations.",
                        locations.Count);
            }

            if (locations.Count > 0)
            {
                WeatherCityResultsComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            if (WeatherSearchStatusTextBlock is not null)
            {
                WeatherSearchStatusTextBlock.Text = Lf(
                    "settings.weather.search_failed_format",
                    "Search failed: {0}",
                    ex.Message);
            }
        }
        finally
        {
            _isWeatherSearchInProgress = false;
            SetWeatherSearchBusy(isBusy: false);
        }
    }

    private static string FormatWeatherLocationDisplayName(WeatherLocation location)
    {
        var affiliation = string.IsNullOrWhiteSpace(location.Affiliation)
            ? string.Empty
            : $" ({location.Affiliation})";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{location.Name}{affiliation} | {location.LocationKey}");
    }

    private static string BuildWeatherLocationName(WeatherLocation location)
    {
        if (string.IsNullOrWhiteSpace(location.Affiliation))
        {
            return location.Name;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{location.Name} ({location.Affiliation})");
    }

    private void OnApplyWeatherCitySelectionClick(object? sender, RoutedEventArgs e)
    {
        if (WeatherCityResultsComboBox?.SelectedItem is not ComboBoxItem item ||
            item.Tag is not WeatherLocation location)
        {
            if (WeatherSearchStatusTextBlock is not null)
            {
                WeatherSearchStatusTextBlock.Text = L(
                    "settings.weather.search_select_required",
                    "Please select one location from search results.");
            }

            return;
        }

        _weatherLocationMode = WeatherLocationMode.CitySearch;
        _weatherLocationKey = location.LocationKey.Trim();
        _weatherLocationName = BuildWeatherLocationName(location);
        _weatherLatitude = NormalizeLatitude(location.Latitude);
        _weatherLongitude = NormalizeLongitude(location.Longitude);

        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
            if (WeatherLocationKeyTextBox is not null)
            {
                WeatherLocationKeyTextBox.Text = _weatherLocationKey;
            }

            if (WeatherLocationNameTextBox is not null)
            {
                WeatherLocationNameTextBox.Text = _weatherLocationName;
            }

            if (WeatherLatitudeNumberBox is not null)
            {
                WeatherLatitudeNumberBox.Value = _weatherLatitude;
            }

            if (WeatherLongitudeNumberBox is not null)
            {
                WeatherLongitudeNumberBox.Value = _weatherLongitude;
            }
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        if (WeatherSearchStatusTextBlock is not null)
        {
            WeatherSearchStatusTextBlock.Text = Lf(
                "settings.weather.search_applied_format",
                "Location applied: {0}",
                _weatherLocationName);
        }

        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private void OnApplyWeatherCoordinatesClick(object? sender, RoutedEventArgs e)
    {
        if (WeatherLatitudeNumberBox is null || WeatherLongitudeNumberBox is null)
        {
            return;
        }

        var latitude = NormalizeLatitude(WeatherLatitudeNumberBox.Value);
        var longitude = NormalizeLongitude(WeatherLongitudeNumberBox.Value);
        var keyInput = WeatherLocationKeyTextBox?.Text?.Trim() ?? string.Empty;
        var nameInput = WeatherLocationNameTextBox?.Text?.Trim() ?? string.Empty;

        _weatherLocationMode = WeatherLocationMode.Coordinates;
        _weatherLatitude = latitude;
        _weatherLongitude = longitude;
        _weatherLocationKey = string.IsNullOrWhiteSpace(keyInput)
            ? BuildCoordinateLocationKey(latitude, longitude)
            : keyInput;
        _weatherLocationName = string.IsNullOrWhiteSpace(nameInput)
            ? Lf(
                "settings.weather.coordinates_default_name_format",
                "Coordinate {0:F4}, {1:F4}",
                latitude,
                longitude)
            : nameInput;

        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
            if (WeatherLocationKeyTextBox is not null && string.IsNullOrWhiteSpace(keyInput))
            {
                WeatherLocationKeyTextBox.Text = _weatherLocationKey;
            }

            if (WeatherLocationNameTextBox is not null && string.IsNullOrWhiteSpace(nameInput))
            {
                WeatherLocationNameTextBox.Text = _weatherLocationName;
            }
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        if (WeatherCoordinateStatusTextBlock is not null)
        {
            WeatherCoordinateStatusTextBlock.Text = Lf(
                "settings.weather.coordinates_saved_format",
                "Coordinates saved: {0:F4}, {1:F4}",
                _weatherLatitude,
                _weatherLongitude);
        }

        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private static string BuildCoordinateLocationKey(double latitude, double longitude)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"coord:{latitude:F4},{longitude:F4}");
    }

    private async void OnTestWeatherRequestClick(object? sender, RoutedEventArgs e)
    {
        if (_isWeatherPreviewInProgress)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_weatherLocationKey))
        {
            if (_weatherLocationMode == WeatherLocationMode.Coordinates)
            {
                _weatherLocationKey = BuildCoordinateLocationKey(_weatherLatitude, _weatherLongitude);
            }
            else
            {
                if (WeatherPreviewResultTextBlock is not null)
                {
                    WeatherPreviewResultTextBlock.Text = L(
                        "settings.weather.preview_missing_location",
                        "Please apply one weather location before testing.");
                }

                UpdateWeatherPreviewSummary(
                    weatherCode: null,
                    temperatureText: "--",
                    updatedAt: null);

                return;
            }
        }

        _isWeatherPreviewInProgress = true;
        SetWeatherPreviewBusy(isBusy: true);
        try
        {
            var query = new WeatherQuery(
                LocationKey: _weatherLocationKey,
                Latitude: _weatherLatitude,
                Longitude: _weatherLongitude,
                ForecastDays: 3,
                Locale: ResolveWeatherApiLocale(),
                IsGlobal: false,
                ForceRefresh: true);

            var result = await _weatherDataService.GetWeatherAsync(query);
            if (!result.Success || result.Data is null)
            {
                if (WeatherPreviewResultTextBlock is not null)
                {
                    WeatherPreviewResultTextBlock.Text = Lf(
                        "settings.weather.preview_failed_format",
                        "Test fetch failed: {0}",
                        result.ErrorMessage ?? result.ErrorCode ?? "Unknown error");
                }

                UpdateWeatherPreviewSummary(
                    weatherCode: null,
                    temperatureText: "--",
                    updatedAt: DateTimeOffset.Now);

                return;
            }

            var snapshot = result.Data;
            var location = string.IsNullOrWhiteSpace(snapshot.LocationName)
                ? (!string.IsNullOrWhiteSpace(_weatherLocationName) ? _weatherLocationName : _weatherLocationKey)
                : snapshot.LocationName;
            var weather = snapshot.Current.WeatherText ??
                          L("settings.weather.preview_unknown", "Unknown");
            var temperature = snapshot.Current.TemperatureC.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{snapshot.Current.TemperatureC.Value:F1} C")
                : "--";
            var updatedAt = snapshot.ObservationTime ?? snapshot.FetchedAt;

            if (WeatherPreviewResultTextBlock is not null)
            {
                WeatherPreviewResultTextBlock.Text = Lf(
                    "settings.weather.preview_success_format",
                    "Test success: {0} | {1} | {2}",
                    location,
                    weather,
                    temperature);
            }

            UpdateWeatherPreviewSummary(
                weatherCode: snapshot.Current.WeatherCode,
                temperatureText: temperature,
                updatedAt: updatedAt);
        }
        catch (Exception ex)
        {
            if (WeatherPreviewResultTextBlock is not null)
            {
                WeatherPreviewResultTextBlock.Text = Lf(
                    "settings.weather.preview_failed_format",
                    "Test fetch failed: {0}",
                    ex.Message);
            }

            UpdateWeatherPreviewSummary(
                weatherCode: null,
                temperatureText: "--",
                updatedAt: DateTimeOffset.Now);
        }
        finally
        {
            _isWeatherPreviewInProgress = false;
            SetWeatherPreviewBusy(isBusy: false);
        }
    }

    private void UpdateWeatherPreviewSummary(int? weatherCode, string temperatureText, DateTimeOffset? updatedAt)
    {
        if (WeatherPreviewIconSymbol is not null)
        {
            WeatherPreviewIconSymbol.Symbol = ResolveWeatherPreviewSymbol(weatherCode, _isNightMode);
            WeatherPreviewIconSymbol.IconVariant = string.Equals(_weatherIconPackId, "FluentFilled", StringComparison.OrdinalIgnoreCase)
                ? IconVariant.Filled
                : IconVariant.Regular;
        }

        if (WeatherPreviewTemperatureTextBlock is not null)
        {
            WeatherPreviewTemperatureTextBlock.Text = string.IsNullOrWhiteSpace(temperatureText) ? "--" : temperatureText;
        }

        if (WeatherPreviewUpdatedTextBlock is null)
        {
            return;
        }

        WeatherPreviewUpdatedTextBlock.Text = updatedAt.HasValue
            ? Lf("weather.widget.updated_format", "Updated {0:HH:mm}", updatedAt.Value.LocalDateTime)
            : "-";
    }

    private static Symbol ResolveWeatherPreviewSymbol(int? weatherCode, bool isNight)
    {
        return weatherCode switch
        {
            0 => isNight ? Symbol.WeatherMoon : Symbol.WeatherSunny,
            1 or 2 => isNight ? Symbol.WeatherPartlyCloudyNight : Symbol.WeatherPartlyCloudyDay,
            3 or 7 => Symbol.WeatherRainShowersDay,
            8 or 9 => Symbol.WeatherRain,
            4 => Symbol.WeatherThunderstorm,
            13 or 14 or 15 or 16 => Symbol.WeatherSnow,
            18 or 32 => Symbol.WeatherFog,
            _ => isNight ? Symbol.WeatherPartlyCloudyNight : Symbol.WeatherPartlyCloudyDay
        };
    }

    private void SetWeatherSearchBusy(bool isBusy)
    {
        if (WeatherSearchButton is not null)
        {
            WeatherSearchButton.IsEnabled = !isBusy;
        }

        if (WeatherSearchProgressRing is not null)
        {
            WeatherSearchProgressRing.IsVisible = isBusy;
        }
    }

    private void SetWeatherPreviewBusy(bool isBusy)
    {
        if (WeatherPreviewButton is not null)
        {
            WeatherPreviewButton.IsEnabled = !isBusy;
        }

        if (WeatherPreviewProgressRing is not null)
        {
            WeatherPreviewProgressRing.IsVisible = isBusy;
        }
    }

    private string ResolveWeatherApiLocale()
    {
        return string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? "zh_cn"
            : "en_us";
    }

    private void UpdateAdaptiveTextSystem()
    {
        var isLightBackground = _isSettingsOpen
            ? !_isNightMode
            : CalculateCurrentBackgroundLuminance() >= LightBackgroundLuminanceThreshold;
        var isLightNavBackground = _isSettingsOpen
            ? !_isNightMode
            : CalculateBrushLuminance(SettingsNavPanelBorder?.Background) >= LightBackgroundLuminanceThreshold;
        var context = new ThemeColorContext(
            _selectedThemeColor,
            isLightBackground,
            isLightNavBackground,
            _isNightMode);

        ThemeColorSystemService.ApplyThemeResources(Resources, context);
        GlassEffectService.ApplyGlassResources(Resources, context);
        if (_fluentAvaloniaTheme is not null)
        {
            _fluentAvaloniaTheme.CustomAccentColor = _selectedThemeColor;
        }
    }

    private double CalculateCurrentBackgroundLuminance()
    {
        if (_isSettingsOpen)
        {
            return CalculateBrushLuminance(SettingsContentPanel?.Background ?? SettingsPage?.Background);
        }

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
        ThemeColorStatusTextBlock.Text = Lf(
            "settings.color.mode_status_format",
            "Theme mode: {0}.",
            enabled ? L("common.night", "Night") : L("common.day", "Day"));
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

    private void ApplyColorPaletteToButtons(
        IReadOnlyList<Color> colors,
        IReadOnlyList<(Button Button, Border Swatch)> targets)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var color = i < colors.Count
                ? colors[i]
                : Color.Parse("#00000000");
            var (button, swatch) = targets[i];
            button.Tag = color.ToString();
            button.IsEnabled = i < colors.Count;
            swatch.Background = i < colors.Count
                ? new SolidColorBrush(color)
                : new SolidColorBrush(Color.Parse("#00000000"));
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
        if (ContainsColor(_recommendedColors, _selectedThemeColor) ||
            ContainsColor(_monetColors, _selectedThemeColor))
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
        ThemeColorStatusTextBlock.Text = Lf(
            "settings.color.theme_applied_format",
            "{0} color applied: {1}.",
            sourceLabel,
            _selectedThemeColor);
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

            using var scaledBitmap = bitmap.CreateScaledBitmap(
                new PixelSize(sampleWidth, sampleHeight),
                BitmapInterpolationMode.MediumQuality);
            using var writeable = new WriteableBitmap(
                scaledBitmap.PixelSize,
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
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

                    var blue = (pixelBuffer[index] / 255d) / alpha;
                    var green = (pixelBuffer[index + 1] / 255d) / alpha;
                    var red = (pixelBuffer[index + 2] / 255d) / alpha;

                    red = Math.Clamp(red, 0, 1);
                    green = Math.Clamp(green, 0, 1);
                    blue = Math.Clamp(blue, 0, 1);

                    luminanceSum += CalculateRelativeLuminance(red, green, blue);
                    pixelCount++;
                }
            }

            return pixelCount > 0
                ? luminanceSum / pixelCount
                : CalculateRelativeLuminance(Color.Parse("#FF020617"));
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
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private void OpenSettingsPage()
    {
        if (_isComponentLibraryOpen)
        {
            return;
        }

        if (_isSettingsOpen)
        {
            return;
        }

        _isSettingsOpen = true;
        UpdateDesktopPageAwareComponentContext();
        UpdateAdaptiveTextSystem();
        ApplyWallpaperBrush();
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
        if (_settingsContentPanelTransform is not null)
        {
            _settingsContentPanelTransform.Y = 30;
        }
        SettingsPage.IsVisible = true;
        SettingsPage.Opacity = 0;
        UpdateSettingsViewportInsets(Math.Max(1, _currentDesktopCellSize));

        UpdateWallpaperPreviewLayout();

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isSettingsOpen)
            {
                return;
            }

            if (_settingsContentPanelTransform is not null)
            {
                _settingsContentPanelTransform.Y = 0;
            }
            SettingsPage.Opacity = 1;
        }, DispatcherPriority.Background);
    }

    private void CloseSettingsPage(bool immediate = false)
    {
        if (!_isSettingsOpen)
        {
            return;
        }

        _isSettingsOpen = false;
        UpdateDesktopPageAwareComponentContext();
        UpdateAdaptiveTextSystem();
        ApplyWallpaperBrush();
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        if (immediate)
        {
            SettingsPage.Opacity = 0;
            if (_settingsContentPanelTransform is not null)
            {
                _settingsContentPanelTransform.Y = 30;
            }
            SettingsPage.IsVisible = false;
            return;
        }

        if (_settingsContentPanelTransform is not null)
        {
            _settingsContentPanelTransform.Y = 30;
        }
        SettingsPage.Opacity = 0;

        DispatcherTimer.RunOnce(() =>
        {
            if (_isSettingsOpen)
            {
                return;
            }

            SettingsPage.IsVisible = false;
        }, TimeSpan.FromMilliseconds(SettingsTransitionDurationMs));
    }

    private void InitializeSettingsIcons()
    {
        const IconVariant variant = IconVariant.Regular;

        if (WallpaperPlacementSettingsExpander is not null)
        {
            WallpaperPlacementSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Wallpaper,
                IconVariant = variant
            };
        }

        if (ThemeColorSettingsExpander is not null)
        {
            ThemeColorSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Color,
                IconVariant = variant
            };
        }

        if (StatusBarClockSettingsExpander is not null)
        {
            StatusBarClockSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Clock,
                IconVariant = variant
            };
        }

        if (StatusBarSpacingSettingsExpander is not null)
        {
            StatusBarSpacingSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.TextLineSpacing,
                IconVariant = variant
            };
        }

        if (WeatherLocationSettingsExpander is not null)
        {
            WeatherLocationSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.WeatherSunny,
                IconVariant = variant
            };
        }

        if (WeatherPreviewSettingsExpander is not null)
        {
            WeatherPreviewSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.WeatherSunny,
                IconVariant = variant
            };
        }

        if (WeatherAlertFilterSettingsExpander is not null)
        {
            WeatherAlertFilterSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Info,
                IconVariant = variant
            };
        }

        if (WeatherIconPackSettingsExpander is not null)
        {
            WeatherIconPackSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Color,
                IconVariant = variant
            };
        }

        if (WeatherNoTlsSettingsExpander is not null)
        {
            WeatherNoTlsSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Globe,
                IconVariant = variant
            };
        }

        if (LanguageSettingsExpander is not null)
        {
            LanguageSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Translate,
                IconVariant = variant
            };
        }

        if (TimeZoneSettingsExpander is not null)
        {
            TimeZoneSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.GlobeClock,
                IconVariant = variant
            };
        }

        if (UpdateOptionsSettingsExpander is not null)
        {
            UpdateOptionsSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.ArrowClockwiseDashesSettings,
                IconVariant = variant
            };
        }

        if (UpdateActionsSettingsExpander is not null)
        {
            UpdateActionsSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.ArrowDownload,
                IconVariant = variant
            };
        }

        if (AboutStartupSettingsExpander is not null)
        {
            AboutStartupSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
            {
                Symbol = Symbol.Play,
                IconVariant = variant
            };
        }

        UpdateThemeModeIcon();
    }

    private void UpdateThemeModeIcon()
    {
        if (ThemeModeSettingsExpander is null)
        {
            return;
        }

        ThemeModeSettingsExpander.IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
        {
            Symbol = _isNightMode ? Symbol.WeatherMoon : Symbol.WeatherSunny,
            IconVariant = IconVariant.Regular
        };
    }
}
