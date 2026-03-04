using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

namespace LanMountainDesktop.Views.Components;

public partial class MusicControlWidget : UserControl, IDesktopComponentWidget
{
    private const Symbol PlaySymbol = Symbol.Play;
    private const Symbol PauseSymbol = Symbol.Pause;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2.4)
    };

    private readonly IMusicControlService _musicControlService = MusicControlServiceFactory.CreateDefault();
    private readonly MonetColorService _monetColorService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();

    private CancellationTokenSource? _refreshCts;
    private Bitmap? _coverBitmap;
    private MusicPlaybackState _currentState = MusicPlaybackState.NoSession(isSupported: true);
    private string _languageCode = "zh-CN";
    private double _currentCellSize = 48;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool _isExecutingCommand;
    private double _progressRatio;
    private bool _isProgressIndeterminate;

    public MusicControlWidget()
    {
        InitializeComponent();

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        ApplyDynamicBackground(null);
        ApplyState(MusicPlaybackState.NoSession(isSupported: OperatingSystem.IsWindows()));
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        var rootRadius = Math.Clamp(30 * scale, 16, 44);
        var rootCornerRadius = new CornerRadius(rootRadius);

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
        CoverBorder.CornerRadius = new CornerRadius(Math.Clamp(12 * scale, 8, 16));

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

        UpdateProgressVisual(_progressRatio, _isProgressIndeterminate);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _refreshTimer.Start();
        _ = RefreshStateAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        DisposeCoverBitmap();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshStateAsync();
    }

    private async void OnPlayPauseButtonClick(object? sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync(token => _musicControlService.TogglePlayPauseAsync(token));
    }

    private async void OnPreviousButtonClick(object? sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync(token => _musicControlService.SkipPreviousAsync(token));
    }

    private async void OnNextButtonClick(object? sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync(token => _musicControlService.SkipNextAsync(token));
    }

    private async void OnSourceAppButtonClick(object? sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync(
            token => _musicControlService.LaunchSourceAppAsync(token),
            refreshAfterCommand: false,
            requireActiveSession: false);
    }

    private async Task ExecuteCommandAsync(
        Func<CancellationToken, Task<bool>> command,
        bool refreshAfterCommand = true,
        bool requireActiveSession = true)
    {
        if (_isExecutingCommand
            || !_currentState.IsSupported
            || (requireActiveSession && !_currentState.HasSession))
        {
            return;
        }

        _isExecutingCommand = true;
        ApplyActionButtonState(_currentState);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            _ = await command(cts.Token);
        }
        catch
        {
            // Ignore command transport errors and recover on next poll.
        }
        finally
        {
            _isExecutingCommand = false;
        }

        if (refreshAfterCommand)
        {
            await RefreshStateAsync();
        }
    }

    private async Task RefreshStateAsync()
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateLanguageCode();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var state = await _musicControlService.GetCurrentStateAsync(cts.Token);
            if (cts.IsCancellationRequested || !_isAttached)
            {
                return;
            }

            _currentState = state;
            ApplyState(state);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation.
        }
        catch
        {
            var fallbackState = MusicPlaybackState.NoSession(isSupported: OperatingSystem.IsWindows());
            _currentState = fallbackState;
            ApplyState(fallbackState);
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                _refreshCts = null;
            }

            cts.Dispose();
            _isRefreshing = false;
        }
    }

    private void ApplyState(MusicPlaybackState state)
    {
        var hasMediaSession = state.IsSupported && state.HasSession;

        if (!state.IsSupported)
        {
            TitleTextBlock.Text = L("music.widget.unsupported", "Music control is only available on Windows");
            ArtistTextBlock.Text = L("music.widget.unsupported_hint", "SMTC backend is unavailable");
            SourceAppTextBlock.Text = L("music.widget.open_player", "Open player");
            StatusTextBlock.Text = "--";
            PositionTextBlock.Text = "00:00";
            DurationTextBlock.Text = "00:00";
            PlaybackActivityIcon.IsVisible = false;
            PlayPauseGlyphIcon.Symbol = PlaySymbol;
            UpdateProgressVisual(0, false);
            SetCoverImage(null);
            ApplyNoMediaVisualTheme();
            ApplyActionButtonState(state);
            UpdateSourceAppButtonTooltip();
            return;
        }

        if (!state.HasSession)
        {
            TitleTextBlock.Text = L("music.widget.no_session", "No active media session");
            ArtistTextBlock.Text = L("music.widget.no_session_hint", "Open a player that supports SMTC");
            SourceAppTextBlock.Text = L("music.widget.open_player", "Open player");
            StatusTextBlock.Text = "--";
            PositionTextBlock.Text = "00:00";
            DurationTextBlock.Text = "00:00";
            PlaybackActivityIcon.IsVisible = false;
            PlayPauseGlyphIcon.Symbol = PlaySymbol;
            UpdateProgressVisual(0, false);
            SetCoverImage(null);
            ApplyNoMediaVisualTheme();
            ApplyActionButtonState(state);
            UpdateSourceAppButtonTooltip();
            return;
        }

        ApplyActiveVisualTheme();

        var title = string.IsNullOrWhiteSpace(state.Title)
            ? L("music.widget.unknown_title", "Unknown title")
            : state.Title;
        var subtitle = !string.IsNullOrWhiteSpace(state.Artist)
            ? state.Artist
            : !string.IsNullOrWhiteSpace(state.AlbumTitle)
                ? state.AlbumTitle
                : L("music.widget.unknown_artist", "Unknown artist");

        TitleTextBlock.Text = title;
        ArtistTextBlock.Text = subtitle;
        SourceAppTextBlock.Text = string.IsNullOrWhiteSpace(state.SourceAppName)
            ? L("music.widget.open_player", "Open player")
            : state.SourceAppName;
        StatusTextBlock.Text = ResolveStatusText(state.PlaybackStatus);
        PlaybackActivityIcon.IsVisible = state.PlaybackStatus == MusicPlaybackStatus.Playing;

        var position = ClampToNonNegative(state.Position);
        var duration = ClampToNonNegative(state.Duration);
        var progressRatio = duration.TotalMilliseconds <= 1
            ? 0
            : Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);

        PositionTextBlock.Text = FormatTimeline(position);
        DurationTextBlock.Text = duration.TotalMilliseconds > 1
            ? FormatTimeline(duration)
            : "00:00";
        UpdateProgressVisual(progressRatio, hasMediaSession && duration.TotalMilliseconds <= 1);

        PlayPauseGlyphIcon.Symbol = state.PlaybackStatus == MusicPlaybackStatus.Playing
            ? PauseSymbol
            : PlaySymbol;

        SetCoverImage(state.ThumbnailBytes);
        ApplyActionButtonState(state);
        UpdateSourceAppButtonTooltip();
    }

    private void ApplyActionButtonState(MusicPlaybackState state)
    {
        var canOperate = !_isExecutingCommand && state.IsSupported && state.HasSession;
        var showNoSessionStyle = !_isExecutingCommand && state.IsSupported && !state.HasSession;

        PlayPauseButton.IsEnabled = canOperate
            ? state.CanPlayPause
            : showNoSessionStyle;
        PreviousButton.IsEnabled = canOperate
            ? state.CanSkipPrevious
            : showNoSessionStyle;
        NextButton.IsEnabled = canOperate
            ? state.CanSkipNext
            : showNoSessionStyle;
        SourceAppButton.IsEnabled = !_isExecutingCommand && state.IsSupported;
        QueueButton.IsEnabled = canOperate || showNoSessionStyle;
        FavoriteButton.IsEnabled = canOperate || showNoSessionStyle;
    }

    private void ApplyNoMediaVisualTheme()
    {
        ArtistTextBlock.MaxLines = 2;

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
        ArtistTextBlock.MaxLines = 1;

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

    private void UpdateLanguageCode()
    {
        try
        {
            var snapshot = _settingsService.Load();
            _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
        }
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

    private string ResolveStatusText(MusicPlaybackStatus status)
    {
        return status switch
        {
            MusicPlaybackStatus.Playing => L("music.widget.status.playing", "Playing"),
            MusicPlaybackStatus.Paused => L("music.widget.status.paused", "Paused"),
            MusicPlaybackStatus.Stopped => L("music.widget.status.stopped", "Stopped"),
            MusicPlaybackStatus.Changing => L("music.widget.status.changing", "Changing"),
            MusicPlaybackStatus.Opened => L("music.widget.status.opened", "Opened"),
            _ => "--"
        };
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
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

    private static TimeSpan ClampToNonNegative(TimeSpan value)
    {
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    private static string FormatTimeline(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void SetCoverImage(byte[]? thumbnailBytes)
    {
        DisposeCoverBitmap();

        if (thumbnailBytes is null || thumbnailBytes.Length == 0)
        {
            CoverImage.Source = null;
            BackdropCoverImage.Source = null;
            CoverImage.IsVisible = false;
            BackdropCoverImage.IsVisible = false;
            CoverFallbackGlyph.IsVisible = true;
            ApplyDynamicBackground(null);
            return;
        }

        try
        {
            using var stream = new MemoryStream(thumbnailBytes, writable: false);
            _coverBitmap = new Bitmap(stream);
            CoverImage.Source = _coverBitmap;
            BackdropCoverImage.Source = _coverBitmap;
            CoverImage.IsVisible = true;
            BackdropCoverImage.IsVisible = true;
            CoverFallbackGlyph.IsVisible = false;
            ApplyDynamicBackground(_coverBitmap);
        }
        catch
        {
            CoverImage.Source = null;
            BackdropCoverImage.Source = null;
            CoverImage.IsVisible = false;
            BackdropCoverImage.IsVisible = false;
            CoverFallbackGlyph.IsVisible = true;
            ApplyDynamicBackground(null);
        }
    }

    private void DisposeCoverBitmap()
    {
        if (_coverBitmap is null)
        {
            return;
        }

        if (ReferenceEquals(CoverImage.Source, _coverBitmap))
        {
            CoverImage.Source = null;
        }

        if (ReferenceEquals(BackdropCoverImage.Source, _coverBitmap))
        {
            BackdropCoverImage.Source = null;
        }

        _coverBitmap.Dispose();
        _coverBitmap = null;
    }

    private void UpdateProgressVisual(double ratio, bool indeterminate)
    {
        _progressRatio = Math.Clamp(ratio, 0, 1);
        _isProgressIndeterminate = indeterminate;

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

        ProgressFillBorder.Width = trackWidth * _progressRatio;
        ProgressFillBorder.Opacity = 0.96;
    }

    private void UpdateSourceAppButtonTooltip()
    {
        var sourceName = string.IsNullOrWhiteSpace(SourceAppTextBlock.Text)
            ? L("music.widget.open_player", "Open player")
            : SourceAppTextBlock.Text;
        var statusText = string.IsNullOrWhiteSpace(StatusTextBlock.Text) || StatusTextBlock.Text == "--"
            ? sourceName
            : string.Create(CultureInfo.InvariantCulture, $"{sourceName} ({StatusTextBlock.Text})");
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
