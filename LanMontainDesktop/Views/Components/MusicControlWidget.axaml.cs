using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class MusicControlWidget : UserControl, IDesktopComponentWidget
{
    private static readonly Geometry PlayGlyph = Geometry.Parse("M 2,1 L 2,13 L 12,7 Z");
    private static readonly Geometry PauseGlyph = Geometry.Parse("M 2,1 H 5 V 13 H 2 Z M 9,1 H 12 V 13 H 9 Z");

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2.4)
    };

    private readonly IMusicControlService _musicControlService = MusicControlServiceFactory.CreateDefault();
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

    public MusicControlWidget()
    {
        InitializeComponent();

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        ApplyState(MusicPlaybackState.NoSession(isSupported: OperatingSystem.IsWindows()));
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(30 * scale, 16, 44));
        RootBorder.Padding = new Thickness(
            Math.Clamp(14 * scale, 8, 24),
            Math.Clamp(11 * scale, 7, 18),
            Math.Clamp(14 * scale, 8, 24),
            Math.Clamp(11 * scale, 7, 18));

        CoverBorder.Width = Math.Clamp(56 * scale, 38, 92);
        CoverBorder.Height = Math.Clamp(56 * scale, 38, 92);
        CoverBorder.CornerRadius = new CornerRadius(Math.Clamp(12 * scale, 8, 18));

        StatusBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(10 * scale, 6, 14));
        StatusBadgeBorder.Padding = new Thickness(
            Math.Clamp(8 * scale, 5, 12),
            Math.Clamp(4 * scale, 3, 8));

        TitleTextBlock.FontSize = Math.Clamp(22 * scale, 13, 30);
        ArtistTextBlock.FontSize = Math.Clamp(16 * scale, 10, 20);
        SourceAppTextBlock.FontSize = Math.Clamp(12 * scale, 9, 15);
        SourceAppButton.Padding = new Thickness(
            Math.Clamp(8 * scale, 5, 12),
            Math.Clamp(3 * scale, 2, 6));
        StatusTextBlock.FontSize = Math.Clamp(12 * scale, 9, 14);

        PositionTextBlock.FontSize = Math.Clamp(13 * scale, 9, 16);
        DurationTextBlock.FontSize = Math.Clamp(13 * scale, 9, 16);
        ProgressBar.Height = Math.Clamp(5 * scale, 3, 8);

        QueueButton.Width = QueueButton.Height = Math.Clamp(32 * scale, 24, 44);
        FavoriteButton.Width = FavoriteButton.Height = Math.Clamp(32 * scale, 24, 44);
        PreviousButton.Width = PreviousButton.Height = Math.Clamp(34 * scale, 25, 46);
        NextButton.Width = NextButton.Height = Math.Clamp(34 * scale, 25, 46);
        PlayPauseButton.Width = PlayPauseButton.Height = Math.Clamp(42 * scale, 30, 58);
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
        await ExecuteCommandAsync(token => _musicControlService.LaunchSourceAppAsync(token), refreshAfterCommand: false);
    }

    private async Task ExecuteCommandAsync(Func<CancellationToken, Task<bool>> command, bool refreshAfterCommand = true)
    {
        if (_isExecutingCommand || !_currentState.IsSupported || !_currentState.HasSession)
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
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            PlayPauseGlyphPath.Data = PlayGlyph;
            SetCoverImage(null);
            ApplyActionButtonState(state);
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
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            PlayPauseGlyphPath.Data = PlayGlyph;
            SetCoverImage(null);
            ApplyActionButtonState(state);
            return;
        }

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

        var position = ClampToNonNegative(state.Position);
        var duration = ClampToNonNegative(state.Duration);
        var progress = duration.TotalMilliseconds <= 1
            ? 0
            : Math.Clamp((position.TotalMilliseconds / duration.TotalMilliseconds) * 100d, 0, 100);

        PositionTextBlock.Text = FormatTimeline(position);
        DurationTextBlock.Text = duration.TotalMilliseconds > 1
            ? FormatTimeline(duration)
            : "00:00";
        ProgressBar.IsIndeterminate = hasMediaSession && duration.TotalMilliseconds <= 1;
        ProgressBar.Value = ProgressBar.IsIndeterminate ? 0 : progress;

        PlayPauseGlyphPath.Data = state.PlaybackStatus == MusicPlaybackStatus.Playing
            ? PauseGlyph
            : PlayGlyph;

        SetCoverImage(state.ThumbnailBytes);
        ApplyActionButtonState(state);
    }

    private void ApplyActionButtonState(MusicPlaybackState state)
    {
        var canOperate = !_isExecutingCommand && state.IsSupported && state.HasSession;
        PlayPauseButton.IsEnabled = canOperate && state.CanPlayPause;
        PreviousButton.IsEnabled = canOperate && state.CanSkipPrevious;
        NextButton.IsEnabled = canOperate && state.CanSkipNext;
        SourceAppButton.IsEnabled = canOperate && !string.IsNullOrWhiteSpace(state.SourceAppId);
        QueueButton.IsEnabled = false;
        FavoriteButton.IsEnabled = false;
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
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * 4), 0.60, 1.8)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * 2), 0.60, 1.8)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale) * 1.05), 0.58, 2.0);
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
            CoverImage.IsVisible = false;
            CoverFallbackGlyph.IsVisible = true;
            return;
        }

        try
        {
            using var stream = new MemoryStream(thumbnailBytes, writable: false);
            _coverBitmap = new Bitmap(stream);
            CoverImage.Source = _coverBitmap;
            CoverImage.IsVisible = true;
            CoverFallbackGlyph.IsVisible = false;
        }
        catch
        {
            CoverImage.Source = null;
            CoverImage.IsVisible = false;
            CoverFallbackGlyph.IsVisible = true;
        }
    }

    private void DisposeCoverBitmap()
    {
        if (_coverBitmap is null)
        {
            return;
        }

        _coverBitmap.Dispose();
        _coverBitmap = null;
    }
}
