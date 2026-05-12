using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public enum MusicPlatform
{
    Unknown = 0,
    Windows = 1,
    Linux = 2
}

public enum MusicPlaybackStatus
{
    Unknown = 0,
    Opened = 1,
    Changing = 2,
    Stopped = 3,
    Playing = 4,
    Paused = 5
}

public sealed record MusicPlaybackState(
    bool IsSupported,
    bool HasSession,
    MusicPlatform Platform,
    string SessionId,
    string SourceAppId,
    string SourceAppName,
    string SourceExecutableOrBusName,
    string Title,
    string Artist,
    string AlbumTitle,
    byte[]? ThumbnailBytes,
    TimeSpan Position,
    TimeSpan Duration,
    MusicPlaybackStatus PlaybackStatus,
    bool CanPlayPause,
    bool CanSkipPrevious,
    bool CanSkipNext,
    bool CanLaunch,
    bool IsStale,
    string StatusMessage,
    DateTimeOffset UpdatedAtUtc)
{
    public static MusicPlaybackState Unsupported(string statusMessage = "Music control is not supported on this platform.")
    {
        return new MusicPlaybackState(
            IsSupported: false,
            HasSession: false,
            Platform: MusicPlatform.Unknown,
            SessionId: string.Empty,
            SourceAppId: string.Empty,
            SourceAppName: string.Empty,
            SourceExecutableOrBusName: string.Empty,
            Title: string.Empty,
            Artist: string.Empty,
            AlbumTitle: string.Empty,
            ThumbnailBytes: null,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.Zero,
            PlaybackStatus: MusicPlaybackStatus.Unknown,
            CanPlayPause: false,
            CanSkipPrevious: false,
            CanSkipNext: false,
            CanLaunch: false,
            IsStale: false,
            StatusMessage: statusMessage,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    public static MusicPlaybackState NoSession(
        bool isSupported = true,
        MusicPlatform platform = MusicPlatform.Unknown,
        string statusMessage = "No active media session.")
    {
        return new MusicPlaybackState(
            IsSupported: isSupported,
            HasSession: false,
            Platform: platform,
            SessionId: string.Empty,
            SourceAppId: string.Empty,
            SourceAppName: string.Empty,
            SourceExecutableOrBusName: string.Empty,
            Title: string.Empty,
            Artist: string.Empty,
            AlbumTitle: string.Empty,
            ThumbnailBytes: null,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.Zero,
            PlaybackStatus: MusicPlaybackStatus.Unknown,
            CanPlayPause: false,
            CanSkipPrevious: false,
            CanSkipNext: false,
            CanLaunch: false,
            IsStale: false,
            StatusMessage: statusMessage,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }
}

public interface IMusicSessionProvider : IDisposable
{
    MusicPlatform Platform { get; }

    event EventHandler? SessionsChanged;

    Task<IReadOnlyList<MusicPlaybackState>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<bool> TogglePlayPauseAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> SkipNextAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> SkipPreviousAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> LaunchSourceAppAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface IMusicControlService
{
    event EventHandler? StateChanged;

    Task<MusicPlaybackState> GetCurrentStateAsync(CancellationToken cancellationToken = default);

    Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default);

    Task<bool> SkipNextAsync(CancellationToken cancellationToken = default);

    Task<bool> SkipPreviousAsync(CancellationToken cancellationToken = default);

    Task<bool> LaunchSourceAppAsync(CancellationToken cancellationToken = default);
}

public sealed class MusicControlService : IMusicControlService, IDisposable
{
    private readonly IMusicSessionProvider _provider;
    private MusicPlaybackState _currentState = MusicPlaybackState.NoSession();

    public MusicControlService(IMusicSessionProvider provider)
    {
        _provider = provider;
        _provider.SessionsChanged += OnProviderSessionsChanged;
    }

    public event EventHandler? StateChanged;

    public async Task<MusicPlaybackState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _provider.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        _currentState = SelectCurrentSession(sessions, _provider.Platform);
        return _currentState;
    }

    public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
        => ExecuteOnCurrentSessionAsync((sessionId, token) => _provider.TogglePlayPauseAsync(sessionId, token), cancellationToken);

    public Task<bool> SkipNextAsync(CancellationToken cancellationToken = default)
        => ExecuteOnCurrentSessionAsync((sessionId, token) => _provider.SkipNextAsync(sessionId, token), cancellationToken);

    public Task<bool> SkipPreviousAsync(CancellationToken cancellationToken = default)
        => ExecuteOnCurrentSessionAsync((sessionId, token) => _provider.SkipPreviousAsync(sessionId, token), cancellationToken);

    public Task<bool> LaunchSourceAppAsync(CancellationToken cancellationToken = default)
        => ExecuteOnCurrentSessionAsync((sessionId, token) => _provider.LaunchSourceAppAsync(sessionId, token), cancellationToken);

    internal static MusicPlaybackState SelectCurrentSession(IReadOnlyList<MusicPlaybackState> sessions, MusicPlatform platform)
    {
        if (sessions.Count == 0)
        {
            return MusicPlaybackState.NoSession(isSupported: true, platform: platform);
        }

        return sessions
            .OrderByDescending(session => session.PlaybackStatus == MusicPlaybackStatus.Playing)
            .ThenByDescending(session => session.UpdatedAtUtc)
            .First();
    }

    public void Dispose()
    {
        _provider.SessionsChanged -= OnProviderSessionsChanged;
        _provider.Dispose();
    }

    private async Task<bool> ExecuteOnCurrentSessionAsync(
        Func<string, CancellationToken, Task<bool>> command,
        CancellationToken cancellationToken)
    {
        var state = _currentState.HasSession
            ? _currentState
            : await GetCurrentStateAsync(cancellationToken).ConfigureAwait(false);

        if (!state.IsSupported || !state.HasSession || string.IsNullOrWhiteSpace(state.SessionId))
        {
            return false;
        }

        return await command(state.SessionId, cancellationToken).ConfigureAwait(false);
    }

    private void OnProviderSessionsChanged(object? sender, EventArgs e)
        => StateChanged?.Invoke(this, EventArgs.Empty);
}

public static class MusicControlServiceFactory
{
    public static IMusicControlService CreateDefault()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new MusicControlService(new WindowsSmtcMusicControlService());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new MusicControlService(new LinuxMprisMusicSessionProvider());
        }

        return new MusicControlService(new NoOpMusicSessionProvider());
    }
}

internal sealed class NoOpMusicSessionProvider : IMusicSessionProvider
{
    public MusicPlatform Platform => MusicPlatform.Unknown;

    public event EventHandler? SessionsChanged;

    public Task<IReadOnlyList<MusicPlaybackState>> GetSessionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MusicPlaybackState>>([MusicPlaybackState.Unsupported()]);

    public Task<bool> TogglePlayPauseAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> SkipNextAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> SkipPreviousAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> LaunchSourceAppAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public void Dispose()
        => SessionsChanged = null;
}
