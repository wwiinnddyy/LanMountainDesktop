using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentIcons.Common;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.Components;

public partial class MusicControlWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget
{
    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2.4)
    };

    private readonly MusicControlViewModel _viewModel = new();
    private readonly MonetColorService _monetColorService = new();

    private double _currentCellSize = 48;
    private bool _isAttached;
    private bool _isOnActivePage = true;

    public MusicControlWidget()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _refreshTimer.Tick += OnRefreshTimerTick;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        ApplyViewModel();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        var rootCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();

        RootBorder.CornerRadius = rootCornerRadius;
        ContentPaddingBorder.Padding = new Thickness(
            Math.Clamp(14 * scale, 9, 22),
            Math.Clamp(11 * scale, 7, 18),
            Math.Clamp(14 * scale, 9, 22),
            Math.Clamp(11 * scale, 7, 18));
        LayoutGrid.RowSpacing = Math.Clamp(9 * scale, 6, 14);
        HeaderRowGrid.ColumnSpacing = Math.Clamp(11 * scale, 8, 18);
        MetaStackPanel.Spacing = Math.Clamp(3 * scale, 1, 6);
        TimelineRowGrid.ColumnSpacing = Math.Clamp(9 * scale, 6, 14);
        ActionRowGrid.ColumnSpacing = Math.Clamp(12 * scale, 8, 20);
        ActionRowGrid.Margin = new Thickness(0, Math.Clamp(1 * scale, 0, 4), 0, 0);
        DynamicBackgroundBase.CornerRadius = rootCornerRadius;
        BackdropCoverHost.CornerRadius = rootCornerRadius;
        DynamicGradientOverlay.CornerRadius = rootCornerRadius;
        DynamicSoftLightOverlay.CornerRadius = rootCornerRadius;

        CoverBorder.Width = Math.Clamp(56 * scale, 38, 86);
        CoverBorder.Height = Math.Clamp(56 * scale, 38, 86);
        CoverBorder.CornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();

        TitleTextBlock.FontSize = Math.Clamp(20 * scale, 12, 28);
        ArtistTextBlock.FontSize = Math.Clamp(14 * scale, 9, 18);
        PlaybackActivityIcon.FontSize = Math.Clamp(13 * scale, 9, 16);

        SourceAppButton.Padding = new Thickness(
            Math.Clamp(9 * scale, 6, 14),
            Math.Clamp(5 * scale, 3, 8));
        SourceAppButton.Margin = new Thickness(0, Math.Clamp(1 * scale, 0, 3), 0, 0);
        var sourceButtonHeight = Math.Clamp(32 * scale, 22, 44);
        SourceAppButton.Height = sourceButtonHeight;
        SourceAppButton.MinWidth = Math.Clamp(62 * scale, 46, 94);
        SourceAppButton.CornerRadius = new CornerRadius(sourceButtonHeight / 2d);
        SourceAppGlyphBadge.Width = Math.Clamp(22 * scale, 15, 30);
        SourceAppGlyphBadge.Height = Math.Clamp(22 * scale, 15, 30);
        SourceAppIcon.FontSize = Math.Clamp(13 * scale, 9, 18);
        SourceChevronIcon.FontSize = Math.Clamp(12 * scale, 8, 16);

        PositionTextBlock.FontSize = Math.Clamp(13 * scale, 8, 15);
        DurationTextBlock.FontSize = Math.Clamp(13 * scale, 8, 15);
        ProgressTrackHost.MinWidth = Math.Clamp(124 * scale, 88, 190);
        var progressHeight = Math.Clamp(3.2 * scale, 2, 6);
        ProgressTrackHost.Height = progressHeight;
        ProgressTrackBorder.CornerRadius = new CornerRadius(progressHeight / 2d);
        ProgressFillBorder.CornerRadius = new CornerRadius(progressHeight / 2d);

        QueueButton.Width = QueueButton.Height = Math.Clamp(31 * scale, 23, 42);
        FavoriteButton.Width = FavoriteButton.Height = Math.Clamp(31 * scale, 23, 42);
        PreviousButton.Width = PreviousButton.Height = Math.Clamp(34 * scale, 25, 44);
        NextButton.Width = NextButton.Height = Math.Clamp(34 * scale, 25, 44);
        PlayPauseButton.Width = PlayPauseButton.Height = Math.Clamp(44 * scale, 31, 58);

        QueueIcon.FontSize = Math.Clamp(16 * scale, 11, 21);
        PreviousIcon.FontSize = Math.Clamp(18 * scale, 13, 24);
        PlayPauseGlyphIcon.FontSize = Math.Clamp(23 * scale, 15, 32);
        NextIcon.FontSize = Math.Clamp(18 * scale, 13, 24);
        FavoriteIcon.FontSize = Math.Clamp(16 * scale, 11, 21);

        UpdateProgressVisual(_viewModel.ProgressRatio, _viewModel.IsProgressIndeterminate);
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        var wasOnActivePage = _isOnActivePage;
        _isOnActivePage = isOnActivePage;
        UpdateRefreshTimerState();

        if (!wasOnActivePage && _isOnActivePage && _isAttached)
        {
            _ = _viewModel.RefreshAsync();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        UpdateRefreshTimerState();
        if (_isOnActivePage)
        {
            _ = _viewModel.RefreshAsync();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UpdateRefreshTimerState();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        => ApplyCellSize(_currentCellSize);

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
        => await _viewModel.RefreshAsync();

    private async void OnPlayPauseButtonClick(object? sender, RoutedEventArgs e)
        => await _viewModel.TogglePlayPauseAsync();

    private async void OnPreviousButtonClick(object? sender, RoutedEventArgs e)
        => await _viewModel.SkipPreviousAsync();

    private async void OnNextButtonClick(object? sender, RoutedEventArgs e)
        => await _viewModel.SkipNextAsync();

    private async void OnSourceAppButtonClick(object? sender, RoutedEventArgs e)
        => await _viewModel.LaunchSourceAsync();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(ApplyViewModel);

    private void UpdateRefreshTimerState()
    {
        if (_isAttached && _isOnActivePage)
        {
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }

            return;
        }

        _refreshTimer.Stop();
    }

    private void ApplyViewModel()
    {
        var state = _viewModel.State;
        var cover = _viewModel.Cover;
        var hasCover = cover is not null;

        TitleTextBlock.Text = _viewModel.TitleText;
        ArtistTextBlock.Text = _viewModel.ArtistText;
        ArtistTextBlock.MaxLines = _viewModel.IsNoMedia ? 2 : 1;
        SourceAppTextBlock.Text = _viewModel.SourceAppText;
        StatusTextBlock.Text = _viewModel.StatusText;
        PositionTextBlock.Text = _viewModel.PositionText;
        DurationTextBlock.Text = _viewModel.DurationText;
        PlaybackActivityIcon.IsVisible = _viewModel.IsPlaybackActive;
        PlayPauseGlyphIcon.Symbol = _viewModel.IsPlaybackActive ? Symbol.Pause : Symbol.Play;

        PlayPauseButton.IsEnabled = _viewModel.CanPlayPause;
        PreviousButton.IsEnabled = _viewModel.CanSkipPrevious;
        NextButton.IsEnabled = _viewModel.CanSkipNext;
        SourceAppButton.IsEnabled = _viewModel.CanLaunchSource;
        QueueButton.IsEnabled = state.IsSupported;
        FavoriteButton.IsEnabled = state.IsSupported;

        CoverImage.Source = cover;
        BackdropCoverImage.Source = cover;
        CoverImage.IsVisible = hasCover;
        BackdropCoverImage.IsVisible = hasCover;
        CoverFallbackGlyph.IsVisible = !hasCover;

        if (_viewModel.IsNoMedia)
        {
            ApplyNoMediaVisualTheme();
        }
        else
        {
            ApplyActiveVisualTheme();
        }

        ApplyDynamicBackground(cover);
        UpdateProgressVisual(_viewModel.ProgressRatio, _viewModel.IsProgressIndeterminate);
        UpdateSourceAppButtonTooltip();
    }

    private void ApplyNoMediaVisualTheme()
    {
        DynamicBackgroundBase.Background = new SolidColorBrush(Color.Parse("#F0635D61"));
        DynamicGradientOverlay.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#44FFFFFF"), 0.0),
                new GradientStop(Color.Parse("#15000000"), 0.60),
                new GradientStop(Color.Parse("#30000000"), 1.0)
            ]
        };
        DynamicSoftLightOverlay.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#05000000"), 0.0),
                new GradientStop(Color.Parse("#24000000"), 1.0)
            ]
        };

        RootBorder.BorderBrush = new SolidColorBrush(Color.Parse("#58FFFFFF"));
        ProgressTrackBorder.Background = new SolidColorBrush(Color.Parse("#3DFFFFFF"));
        ProgressFillBorder.Background = new SolidColorBrush(Color.Parse("#65FFFFFF"));

        CoverBorder.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#FFFF4767"), 0.0),
                new GradientStop(Color.Parse("#FFFF1F56"), 0.58),
                new GradientStop(Color.Parse("#FFD60045"), 1.0)
            ]
        };
        CoverBorder.BorderBrush = new SolidColorBrush(Color.Parse("#48FFFFFF"));
        CoverFallbackGlyph.Symbol = Symbol.MusicNote1;
        CoverFallbackGlyph.IconVariant = IconVariant.Filled;
        CoverFallbackGlyph.Foreground = new SolidColorBrush(Color.Parse("#F5EFF3"));

        SourceAppButton.Background = new SolidColorBrush(Color.Parse("#2FFFFFFF"));
        SourceAppButton.BorderBrush = new SolidColorBrush(Color.Parse("#30FFFFFF"));
        SourceAppGlyphBadge.Background = new SolidColorBrush(Color.Parse("#57FFFFFF"));
        SourceAppGlyphBadge.BorderBrush = new SolidColorBrush(Color.Parse("#00FFFFFF"));
        SourceAppIcon.IconVariant = IconVariant.Filled;
        SourceAppIcon.Foreground = new SolidColorBrush(Color.Parse("#FBFFFFFF"));
    }

    private void ApplyActiveVisualTheme()
    {
        CoverBorder.Background = new SolidColorBrush(Color.Parse("#3CFFFFFF"));
        CoverBorder.BorderBrush = new SolidColorBrush(Color.Parse("#77FFFFFF"));
        CoverFallbackGlyph.Symbol = Symbol.Album;
        CoverFallbackGlyph.IconVariant = IconVariant.Regular;
        CoverFallbackGlyph.Foreground = new SolidColorBrush(Color.Parse("#F3FFFFFF"));

        SourceAppButton.Background = new SolidColorBrush(Color.Parse("#3AFFFFFF"));
        SourceAppButton.BorderBrush = new SolidColorBrush(Color.Parse("#46FFFFFF"));
        SourceAppGlyphBadge.Background = new SolidColorBrush(Color.Parse("#33FFFFFF"));
        SourceAppGlyphBadge.BorderBrush = new SolidColorBrush(Color.Parse("#3CFFFFFF"));
        SourceAppIcon.IconVariant = IconVariant.Filled;
        SourceAppIcon.Foreground = new SolidColorBrush(Color.Parse("#F7FFFFFF"));
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.62, 2.1);
        var widthScale = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * 4), 0.58, 1.9)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * 2), 0.58, 1.9)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale) * 1.05), 0.56, 2.0);
    }

    private void UpdateProgressVisual(double ratio, bool indeterminate)
    {
        if (ProgressTrackHost.Bounds.Width <= 0)
        {
            return;
        }

        var trackWidth = ProgressTrackHost.Bounds.Width;
        if (indeterminate)
        {
            ProgressFillBorder.Width = Math.Max(trackWidth * 0.24, 14);
            ProgressFillBorder.Opacity = 0.56;
            return;
        }

        ProgressFillBorder.Width = trackWidth * Math.Clamp(ratio, 0, 1);
        ProgressFillBorder.Opacity = 0.96;
    }

    private void UpdateSourceAppButtonTooltip()
    {
        var sourceName = string.IsNullOrWhiteSpace(_viewModel.SourceAppText)
            ? "Open player"
            : _viewModel.SourceAppText;
        var statusText = string.IsNullOrWhiteSpace(_viewModel.StatusText) || _viewModel.StatusText == "--"
            ? sourceName
            : $"{sourceName} ({_viewModel.StatusText})";
        ToolTip.SetTip(SourceAppButton, statusText);
    }

    private void ApplyDynamicBackground(Bitmap? albumBitmap)
    {
        var nightMode = ResolveIsNightMode();
        var palette = _monetColorService.BuildPalette(albumBitmap, nightMode);
        var colors = palette.MonetColors.Count > 0 ? palette.MonetColors : palette.RecommendedColors;

        var c0 = PickPaletteColor(colors, 0, Color.Parse("#C4A983"));
        var c1 = PickPaletteColor(colors, 1, Color.Parse("#A88C6B"));
        var c2 = PickPaletteColor(colors, 2, Color.Parse("#8B7459"));
        var c3 = PickPaletteColor(colors, 4, Color.Parse("#6F5E4C"));

        var topLeft = ColorMath.Blend(c0, Color.Parse("#FFFFFFFF"), nightMode ? 0.08 : 0.30);
        var center = ColorMath.Blend(c1, c2, 0.34);
        var bottomRight = ColorMath.Blend(c3, Color.Parse("#FF1F1A16"), nightMode ? 0.42 : 0.20);
        var glow = ColorMath.Blend(c0, Color.Parse("#FFFFFFFF"), 0.38);
        var borderColor = ColorMath.Blend(c0, Color.Parse("#FFFFFFFF"), 0.44);

        DynamicBackgroundBase.Background = new SolidColorBrush(ColorMath.WithAlpha(center, 0xD6));
        DynamicGradientOverlay.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(ColorMath.WithAlpha(topLeft, 0xE6), 0.0),
                new GradientStop(ColorMath.WithAlpha(center, 0xCF), 0.52),
                new GradientStop(ColorMath.WithAlpha(bottomRight, 0xDA), 1.0)
            ]
        };

        DynamicSoftLightOverlay.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(ColorMath.WithAlpha(glow, 0x44), 0.0),
                new GradientStop(ColorMath.WithAlpha(Color.Parse("#FFFFFFFF"), 0x10), 0.45),
                new GradientStop(ColorMath.WithAlpha(Color.Parse("#FF000000"), nightMode ? (byte)0x44 : (byte)0x2B), 1.0)
            ]
        };

        RootBorder.BorderBrush = new SolidColorBrush(ColorMath.WithAlpha(borderColor, 0x7A));
        ProgressTrackBorder.Background = new SolidColorBrush(
            ColorMath.WithAlpha(ColorMath.Blend(center, Color.Parse("#FFFFFFFF"), 0.44), 0x88));
        ProgressFillBorder.Background = new SolidColorBrush(
            ColorMath.WithAlpha(ColorMath.Blend(c0, Color.Parse("#FFFFFFFF"), 0.76), 0xF2));
    }

    private bool ResolveIsNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }

    private static Color PickPaletteColor(IReadOnlyList<Color> colors, int index, Color fallback)
    {
        if (colors.Count == 0)
        {
            return fallback;
        }

        var safeIndex = Math.Clamp(index, 0, colors.Count - 1);
        return colors[safeIndex];
    }
}
