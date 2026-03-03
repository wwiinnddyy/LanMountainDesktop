using System;
using System.Threading;
using System.Threading.Tasks;

namespace LanMontainDesktop.Services;

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
    string SourceAppId,
    string SourceAppName,
    string Title,
    string Artist,
    string AlbumTitle,
    byte[]? ThumbnailBytes,
    TimeSpan Position,
    TimeSpan Duration,
    MusicPlaybackStatus PlaybackStatus,
    bool CanPlayPause,
    bool CanSkipPrevious,
    bool CanSkipNext)
{
    public static MusicPlaybackState Unsupported()
    {
        return new MusicPlaybackState(
            IsSupported: false,
            HasSession: false,
            SourceAppId: string.Empty,
            SourceAppName: string.Empty,
            Title: string.Empty,
            Artist: string.Empty,
            AlbumTitle: string.Empty,
            ThumbnailBytes: null,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.Zero,
            PlaybackStatus: MusicPlaybackStatus.Unknown,
            CanPlayPause: false,
            CanSkipPrevious: false,
            CanSkipNext: false);
    }

    public static MusicPlaybackState NoSession(bool isSupported = true)
    {
        return new MusicPlaybackState(
            IsSupported: isSupported,
            HasSession: false,
            SourceAppId: string.Empty,
            SourceAppName: string.Empty,
            Title: string.Empty,
            Artist: string.Empty,
            AlbumTitle: string.Empty,
            ThumbnailBytes: null,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.Zero,
            PlaybackStatus: MusicPlaybackStatus.Unknown,
            CanPlayPause: false,
            CanSkipPrevious: false,
            CanSkipNext: false);
    }
}

public interface IMusicControlService
{
    Task<MusicPlaybackState> GetCurrentStateAsync(CancellationToken cancellationToken = default);

    Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default);

    Task<bool> SkipNextAsync(CancellationToken cancellationToken = default);

    Task<bool> SkipPreviousAsync(CancellationToken cancellationToken = default);

    Task<bool> LaunchSourceAppAsync(CancellationToken cancellationToken = default);
}

public static class MusicControlServiceFactory
{
    public static IMusicControlService CreateDefault()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsSmtcMusicControlService()
            : new NoOpMusicControlService();
    }
}

internal sealed class NoOpMusicControlService : IMusicControlService
{
    public Task<MusicPlaybackState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MusicPlaybackState.Unsupported());
    }

    public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> SkipNextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> SkipPreviousAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> LaunchSourceAppAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
