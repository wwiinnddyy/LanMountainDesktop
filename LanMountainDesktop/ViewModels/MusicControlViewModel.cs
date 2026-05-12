using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public sealed partial class MusicControlViewModel : ViewModelBase, IDisposable
{
    private readonly IMusicControlService _musicControlService;
    private readonly ISettingsService _settingsService;
    private readonly LocalizationService _localizationService;

    private CancellationTokenSource? _refreshCts;
    private Bitmap? _coverBitmap;
    private bool _isExecutingCommand;
    private string _languageCode = "zh-CN";

    [ObservableProperty] private MusicPlaybackState _state = MusicPlaybackState.NoSession(isSupported: true);
    [ObservableProperty] private string _titleText = string.Empty;
    [ObservableProperty] private string _artistText = string.Empty;
    [ObservableProperty] private string _sourceAppText = string.Empty;
    [ObservableProperty] private string _statusText = "--";
    [ObservableProperty] private string _positionText = "00:00";
    [ObservableProperty] private string _durationText = "00:00";
    [ObservableProperty] private double _progressRatio;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private bool _isPlaybackActive;
    [ObservableProperty] private bool _isNoMedia;
    [ObservableProperty] private bool _canPlayPause;
    [ObservableProperty] private bool _canSkipPrevious;
    [ObservableProperty] private bool _canSkipNext;
    [ObservableProperty] private bool _canLaunchSource;
    [ObservableProperty] private Bitmap? _cover;

    public MusicControlViewModel()
        : this(
            MusicControlServiceFactory.CreateDefault(),
            HostSettingsFacadeProvider.GetOrCreate().Settings,
            new LocalizationService())
    {
    }

    internal MusicControlViewModel(
        IMusicControlService musicControlService,
        ISettingsService settingsService,
        LocalizationService localizationService)
    {
        _musicControlService = musicControlService;
        _settingsService = settingsService;
        _localizationService = localizationService;
        _musicControlService.StateChanged += OnServiceStateChanged;
        ApplyState(MusicPlaybackState.NoSession(isSupported: OperatingSystem.IsWindows() || OperatingSystem.IsLinux()));
    }

    public async Task RefreshAsync()
    {
        UpdateLanguageCode();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var state = await _musicControlService.GetCurrentStateAsync(cts.Token).ConfigureAwait(false);
            if (!cts.IsCancellationRequested)
            {
                ApplyState(state);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ApplyState(MusicPlaybackState.NoSession(
                isSupported: true,
                platform: OperatingSystem.IsLinux() ? MusicPlatform.Linux : MusicPlatform.Windows,
                statusMessage: ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                _refreshCts = null;
            }

            cts.Dispose();
        }
    }

    public Task TogglePlayPauseAsync()
        => ExecuteCommandAsync(token => _musicControlService.TogglePlayPauseAsync(token));

    public Task SkipPreviousAsync()
        => ExecuteCommandAsync(token => _musicControlService.SkipPreviousAsync(token));

    public Task SkipNextAsync()
        => ExecuteCommandAsync(token => _musicControlService.SkipNextAsync(token));

    public Task LaunchSourceAsync()
        => ExecuteCommandAsync(token => _musicControlService.LaunchSourceAppAsync(token), refreshAfterCommand: false, requireActiveSession: false);

    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _refreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
        _musicControlService.StateChanged -= OnServiceStateChanged;
        if (_musicControlService is IDisposable disposable)
        {
            disposable.Dispose();
        }

        SetCover(null);
    }

    private async Task ExecuteCommandAsync(
        Func<CancellationToken, Task<bool>> command,
        bool refreshAfterCommand = true,
        bool requireActiveSession = true)
    {
        if (_isExecutingCommand ||
            !State.IsSupported ||
            (requireActiveSession && !State.HasSession))
        {
            return;
        }

        _isExecutingCommand = true;
        UpdateCommandAvailability(State);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            _ = await command(cts.Token).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _isExecutingCommand = false;
        }

        if (refreshAfterCommand)
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        else
        {
            UpdateCommandAvailability(State);
        }
    }

    private void ApplyState(MusicPlaybackState state)
    {
        State = state;
        IsNoMedia = !state.IsSupported || !state.HasSession;

        if (!state.IsSupported)
        {
            TitleText = L("music.widget.unsupported", "Music control is not supported on this platform");
            ArtistText = string.IsNullOrWhiteSpace(state.StatusMessage)
                ? L("music.widget.unsupported_hint", "Media backend is unavailable")
                : state.StatusMessage;
            SourceAppText = L("music.widget.open_player", "Open player");
            StatusText = "--";
            PositionText = "00:00";
            DurationText = "00:00";
            ProgressRatio = 0;
            IsProgressIndeterminate = false;
            IsPlaybackActive = false;
            SetCover(null);
            UpdateCommandAvailability(state);
            return;
        }

        if (!state.HasSession)
        {
            TitleText = L("music.widget.no_session", "No active media session");
            ArtistText = string.IsNullOrWhiteSpace(state.StatusMessage)
                ? L("music.widget.no_session_hint", "Open a player that supports system media sessions")
                : state.StatusMessage;
            SourceAppText = L("music.widget.open_player", "Open player");
            StatusText = "--";
            PositionText = "00:00";
            DurationText = "00:00";
            ProgressRatio = 0;
            IsProgressIndeterminate = false;
            IsPlaybackActive = false;
            SetCover(null);
            UpdateCommandAvailability(state);
            return;
        }

        TitleText = string.IsNullOrWhiteSpace(state.Title)
            ? L("music.widget.unknown_title", "Unknown title")
            : state.Title;
        ArtistText = !string.IsNullOrWhiteSpace(state.Artist)
            ? state.Artist
            : !string.IsNullOrWhiteSpace(state.AlbumTitle)
                ? state.AlbumTitle
                : L("music.widget.unknown_artist", "Unknown artist");
        SourceAppText = string.IsNullOrWhiteSpace(state.SourceAppName)
            ? L("music.widget.open_player", "Open player")
            : state.SourceAppName;
        StatusText = ResolveStatusText(state.PlaybackStatus);
        IsPlaybackActive = state.PlaybackStatus == MusicPlaybackStatus.Playing;

        var position = ClampToNonNegative(state.Position);
        var duration = ClampToNonNegative(state.Duration);
        PositionText = FormatTimeline(position);
        DurationText = duration.TotalMilliseconds > 1 ? FormatTimeline(duration) : "00:00";
        ProgressRatio = duration.TotalMilliseconds <= 1
            ? 0
            : Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        IsProgressIndeterminate = duration.TotalMilliseconds <= 1;
        SetCover(state.ThumbnailBytes);
        UpdateCommandAvailability(state);
    }

    private void UpdateCommandAvailability(MusicPlaybackState state)
    {
        var canOperate = !_isExecutingCommand && state.IsSupported && state.HasSession;
        var noSessionButSupported = !_isExecutingCommand && state.IsSupported && !state.HasSession;
        CanPlayPause = canOperate ? state.CanPlayPause : noSessionButSupported;
        CanSkipPrevious = canOperate ? state.CanSkipPrevious : noSessionButSupported;
        CanSkipNext = canOperate ? state.CanSkipNext : noSessionButSupported;
        CanLaunchSource = !_isExecutingCommand && state.IsSupported && (state.CanLaunch || !state.HasSession);
    }

    private void SetCover(byte[]? thumbnailBytes)
    {
        Bitmap? next = null;
        if (thumbnailBytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(thumbnailBytes, writable: false);
                next = new Bitmap(stream);
            }
            catch
            {
                next?.Dispose();
                next = null;
            }
        }

        var old = _coverBitmap;
        _coverBitmap = next;
        Cover = next;
        old?.Dispose();
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

    private string ResolveStatusText(MusicPlaybackStatus status)
        => status switch
        {
            MusicPlaybackStatus.Playing => L("music.widget.status.playing", "Playing"),
            MusicPlaybackStatus.Paused => L("music.widget.status.paused", "Paused"),
            MusicPlaybackStatus.Stopped => L("music.widget.status.stopped", "Stopped"),
            MusicPlaybackStatus.Changing => L("music.widget.status.changing", "Changing"),
            MusicPlaybackStatus.Opened => L("music.widget.status.opened", "Opened"),
            _ => "--"
        };

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);

    private void OnServiceStateChanged(object? sender, EventArgs e)
        => _ = RefreshAsync();

    private static TimeSpan ClampToNonNegative(TimeSpan value)
        => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static string FormatTimeline(TimeSpan value)
        => value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}
