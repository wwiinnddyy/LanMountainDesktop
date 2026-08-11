using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class MusicControlServiceTests
{
    [Fact]
    public void SelectCurrentSession_PrefersPlayingSession()
    {
        var olderPlaying = CreateState("playing", MusicPlaybackStatus.Playing, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newerPaused = CreateState("paused", MusicPlaybackStatus.Paused, DateTimeOffset.UtcNow);

        var selected = MusicControlService.SelectCurrentSession([newerPaused, olderPlaying], MusicPlatform.Windows);

        Assert.Equal("playing", selected.SessionId);
    }

    [Fact]
    public void SelectCurrentSession_UsesMostRecentWhenNothingPlaying()
    {
        var older = CreateState("older", MusicPlaybackStatus.Paused, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = CreateState("newer", MusicPlaybackStatus.Stopped, DateTimeOffset.UtcNow);

        var selected = MusicControlService.SelectCurrentSession([older, newer], MusicPlatform.Linux);

        Assert.Equal("newer", selected.SessionId);
    }

    [Fact]
    public void ParseMetadata_MapsCommonMprisFields()
    {
        const string metadata = """
            array [
              dict entry(
                string "xesam:title"
                variant string "Song Title"
              )
              dict entry(
                string "xesam:artist"
                variant array [
                  string "Artist A"
                  string "Artist B"
                ]
              )
              dict entry(
                string "xesam:album"
                variant string "Album"
              )
              dict entry(
                string "mpris:length"
                variant int64 185000000
              )
            ]
            """;

        var parsed = LinuxMprisMusicSessionProvider.ParseMetadata(metadata);

        Assert.Equal("Song Title", parsed["xesam:title"]);
        Assert.Equal("Artist A, Artist B", parsed["xesam:artist"]);
        Assert.Equal("Album", parsed["xesam:album"]);
        Assert.Equal("185000000", parsed["mpris:length"]);
    }

    [Fact]
    public void MapMprisSession_ConvertsStatusCapabilitiesAndDuration()
    {
        const string metadata = """
            dict entry(
              string "xesam:title"
              variant string "Track"
            )
            dict entry(
              string "mpris:length"
              variant int64 120000000
            )
            """;

        var state = LinuxMprisMusicSessionProvider.MapMprisSession(
            "org.mpris.MediaPlayer2.spotify",
            "Spotify",
            "Playing",
            metadata,
            positionMicroseconds: 30_000_000,
            canPlay: true,
            canPause: true,
            canGoNext: true,
            canGoPrevious: false,
            canControl: true,
            DateTimeOffset.UtcNow);

        Assert.True(state.HasSession);
        Assert.Equal(MusicPlatform.Linux, state.Platform);
        Assert.Equal(MusicPlaybackStatus.Playing, state.PlaybackStatus);
        Assert.Equal(TimeSpan.FromSeconds(30), state.Position);
        Assert.Equal(TimeSpan.FromSeconds(120), state.Duration);
        Assert.True(state.CanPlayPause);
        Assert.True(state.CanSkipNext);
        Assert.False(state.CanSkipPrevious);
    }

    private static MusicPlaybackState CreateState(string sessionId, MusicPlaybackStatus status, DateTimeOffset updatedAt)
        => new(
            IsSupported: true,
            HasSession: true,
            Platform: MusicPlatform.Windows,
            SessionId: sessionId,
            SourceAppId: sessionId,
            SourceAppName: sessionId,
            SourceExecutableOrBusName: sessionId,
            Title: sessionId,
            Artist: string.Empty,
            AlbumTitle: string.Empty,
            ThumbnailBytes: null,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.Zero,
            PlaybackStatus: status,
            CanPlayPause: true,
            CanSkipPrevious: true,
            CanSkipNext: true,
            CanLaunch: true,
            IsStale: false,
            StatusMessage: string.Empty,
            UpdatedAtUtc: updatedAt);
}
