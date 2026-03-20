using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

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
    bool CanSkipNext,
    bool CanToggleFavorite,
    bool IsFavorite)
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
            CanSkipNext: false,
            CanToggleFavorite: false,
            IsFavorite: false);
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
            CanSkipNext: false,
            CanToggleFavorite: false,
            IsFavorite: false);
    }
}

public sealed record MusicQueueItem(
    string Id,
    string Title,
    string Artist,
    string AlbumTitle,
    byte[]? ThumbnailBytes,
    TimeSpan Duration,
    bool IsCurrentItem);

public sealed record MusicQueueState(
    bool IsSupported,
    IReadOnlyList<MusicQueueItem> Items,
    int CurrentIndex,
    bool HasMoreItems)
{
    public static MusicQueueState Unsupported()
    {
        return new MusicQueueState(false, Array.Empty<MusicQueueItem>(), -1, false);
    }

    public static MusicQueueState Empty()
    {
        return new MusicQueueState(true, Array.Empty<MusicQueueItem>(), -1, false);
    }
}

public interface IMusicControlService
{
    Task<MusicPlaybackState> GetCurrentStateAsync(CancellationToken cancellationToken = default);

    Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default);

    Task<bool> SkipNextAsync(CancellationToken cancellationToken = default);

    Task<bool> SkipPreviousAsync(CancellationToken cancellationToken = default);

    Task<bool> LaunchSourceAppAsync(CancellationToken cancellationToken = default);

    Task<bool> ToggleFavoriteAsync(CancellationToken cancellationToken = default);

    Task<MusicQueueState> GetPlaybackQueueAsync(int maxItems = 20, CancellationToken cancellationToken = default);

    event EventHandler<MusicPlaybackState>? PlaybackStateChanged;

    event EventHandler<MusicQueueState>? QueueChanged;

    void StartListening();

    void StopListening();
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

    public Task<bool> ToggleFavoriteAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<MusicQueueState> GetPlaybackQueueAsync(int maxItems = 20, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MusicQueueState.Unsupported());
    }

    public event EventHandler<MusicPlaybackState>? PlaybackStateChanged;
    public event EventHandler<MusicQueueState>? QueueChanged;

    public void StartListening()
    {
    }

    public void StopListening()
    {
    }
}
