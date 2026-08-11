using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace LanMountainDesktop.Services;

internal sealed class LinuxMprisMusicSessionProvider : IMusicSessionProvider
{
    private const string MprisPrefix = "org.mpris.MediaPlayer2.";
    private static readonly Regex StringValueRegex = new("\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled);
    private static readonly Regex Int64ValueRegex = new(@"int64\s+(?<value>-?\d+)", RegexOptions.Compiled);
    private static readonly Regex BooleanValueRegex = new(@"boolean\s+(?<value>true|false)", RegexOptions.Compiled);
    private static readonly Regex ArrayStringRegex = new(@"string\s+""(?<value>(?:\\.|[^""])*)""", RegexOptions.Compiled);

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.Ordinal);

    private IDisposable? _nameOwnerChangedWatcher;

    public MusicPlatform Platform => MusicPlatform.Linux;

    public event EventHandler? SessionsChanged;

    public async Task<IReadOnlyList<MusicPlaybackState>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return [MusicPlaybackState.Unsupported("Linux MPRIS is only available on Linux.")];
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
        {
            return [MusicPlaybackState.Unsupported("DBUS_SESSION_BUS_ADDRESS is not set; MPRIS cannot be reached.")];
        }

        try
        {
            await EnsureSignalWatchAsync(cancellationToken).ConfigureAwait(false);
            var names = await ListMprisNamesAsync(cancellationToken).ConfigureAwait(false);
            var sessions = new List<MusicPlaybackState>();
            foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var session = await ReadSessionAsync(name, cancellationToken).ConfigureAwait(false);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }

            return sessions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [MusicPlaybackState.Unsupported($"Linux MPRIS read failed: {ex.Message}")];
        }
    }

    public Task<bool> TogglePlayPauseAsync(string sessionId, CancellationToken cancellationToken = default)
        => CallPlayerMethodAsync(sessionId, "PlayPause", cancellationToken);

    public Task<bool> SkipNextAsync(string sessionId, CancellationToken cancellationToken = default)
        => CallPlayerMethodAsync(sessionId, "Next", cancellationToken);

    public Task<bool> SkipPreviousAsync(string sessionId, CancellationToken cancellationToken = default)
        => CallPlayerMethodAsync(sessionId, "Previous", cancellationToken);

    public async Task<bool> LaunchSourceAppAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (await CallRootMethodAsync(sessionId, "Raise", cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var desktopEntry = sessionId.StartsWith(MprisPrefix, StringComparison.Ordinal)
            ? sessionId[MprisPrefix.Length..].Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            : sessionId;
        return !string.IsNullOrWhiteSpace(desktopEntry) && TryLaunchDesktopEntry(desktopEntry);
    }

    internal static MusicPlaybackState MapMprisSession(
        string busName,
        string identity,
        string playbackStatus,
        string metadataText,
        long positionMicroseconds,
        bool canPlay,
        bool canPause,
        bool canGoNext,
        bool canGoPrevious,
        bool canControl,
        DateTimeOffset lastSeen)
    {
        var metadata = ParseMetadata(metadataText);
        var title = metadata.TryGetValue("xesam:title", out var mappedTitle) ? mappedTitle : string.Empty;
        var album = metadata.TryGetValue("xesam:album", out var mappedAlbum) ? mappedAlbum : string.Empty;
        var artist = metadata.TryGetValue("xesam:artist", out var mappedArtist) ? mappedArtist : string.Empty;
        var artUrl = metadata.TryGetValue("mpris:artUrl", out var mappedArtUrl) ? mappedArtUrl : string.Empty;
        var duration = metadata.TryGetValue("mpris:length", out var lengthText) &&
                       long.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lengthUs) &&
                       lengthUs > 0
            ? TimeSpan.FromMilliseconds(lengthUs / 1000d)
            : TimeSpan.Zero;
        var position = positionMicroseconds > 0
            ? TimeSpan.FromMilliseconds(positionMicroseconds / 1000d)
            : TimeSpan.Zero;
        if (duration > TimeSpan.Zero && position > duration)
        {
            position = duration;
        }

        var displayName = string.IsNullOrWhiteSpace(identity)
            ? SimplifyBusName(busName)
            : identity.Trim();
        var thumbnailBytes = TryReadArtUrlBytes(artUrl);

        return new MusicPlaybackState(
            IsSupported: true,
            HasSession: true,
            Platform: MusicPlatform.Linux,
            SessionId: busName,
            SourceAppId: SimplifyBusName(busName),
            SourceAppName: displayName,
            SourceExecutableOrBusName: busName,
            Title: title,
            Artist: artist,
            AlbumTitle: album,
            ThumbnailBytes: thumbnailBytes,
            Position: position,
            Duration: duration,
            PlaybackStatus: MapPlaybackStatus(playbackStatus),
            CanPlayPause: canControl && (canPlay || canPause),
            CanSkipPrevious: canControl && canGoPrevious,
            CanSkipNext: canControl && canGoNext,
            CanLaunch: true,
            IsStale: false,
            StatusMessage: string.Empty,
            UpdatedAtUtc: lastSeen);
    }

    internal static Dictionary<string, string> ParseMetadata(string text)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return metadata;
        }

        var keys = new[] { "xesam:title", "xesam:artist", "xesam:album", "mpris:length", "mpris:artUrl" };
        foreach (var key in keys)
        {
            var keyIndex = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                continue;
            }

            var tail = text[keyIndex..];
            var nextEntryIndex = tail.IndexOf("dict entry", key.Length + 2, StringComparison.Ordinal);
            if (nextEntryIndex > 0)
            {
                tail = tail[..nextEntryIndex];
            }
            if (key == "mpris:length")
            {
                var intMatch = Int64ValueRegex.Match(tail);
                if (intMatch.Success)
                {
                    metadata[key] = intMatch.Groups["value"].Value;
                }

                continue;
            }

            if (key == "xesam:artist")
            {
                var values = ArrayStringRegex.Matches(tail)
                    .Cast<Match>()
                    .Select(match => Unescape(match.Groups["value"].Value))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToArray();
                if (values.Length > 0)
                {
                    metadata[key] = string.Join(", ", values);
                    continue;
                }
            }

            var valueMatches = StringValueRegex.Matches(tail);
            if (valueMatches.Count >= 2)
            {
                metadata[key] = Unescape(valueMatches[1].Groups["value"].Value);
            }
        }

        return metadata;
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _nameOwnerChangedWatcher?.Dispose();
        _disposeCts.Dispose();
    }

    private async Task EnsureSignalWatchAsync(CancellationToken cancellationToken)
    {
        if (_nameOwnerChangedWatcher is not null)
        {
            return;
        }

        try
        {
            await DBusConnection.Session.ConnectAsync().ConfigureAwait(false);
            _nameOwnerChangedWatcher = await DBusConnection.Session.WatchSignalAsync(
                "org.freedesktop.DBus",
                "/org/freedesktop/DBus",
                "org.freedesktop.DBus",
                "NameOwnerChanged",
                ex =>
                {
                    if (ex is null || !ObserverHandler.IsObserverDisposed(ex))
                    {
                        SessionsChanged?.Invoke(this, EventArgs.Empty);
                    }
                },
                this,
                emitOnCapturedContext: false,
                ObserverFlags.None).ConfigureAwait(false);
        }
        catch
        {
            _nameOwnerChangedWatcher = null;
        }
    }

    private static async Task<IReadOnlyList<string>> ListMprisNamesAsync(CancellationToken cancellationToken)
    {
        await DBusConnection.Session.ConnectAsync().ConfigureAwait(false);
        var names = await DBusConnection.Session.ListServicesAsync().ConfigureAwait(false);
        return names
            .Where(name => name.StartsWith(MprisPrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<MusicPlaybackState?> ReadSessionAsync(string busName, CancellationToken cancellationToken)
    {
        var identity = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2", "Identity", cancellationToken).ConfigureAwait(false);
        var playbackStatus = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "PlaybackStatus", cancellationToken).ConfigureAwait(false);
        var metadata = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "Metadata", cancellationToken).ConfigureAwait(false);
        var positionText = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "Position", cancellationToken).ConfigureAwait(false);
        var canPlayText = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "CanPlay", cancellationToken).ConfigureAwait(false);
        var canPauseText = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "CanPause", cancellationToken).ConfigureAwait(false);
        var canGoNextText = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "CanGoNext", cancellationToken).ConfigureAwait(false);
        var canGoPreviousText = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "CanGoPrevious", cancellationToken).ConfigureAwait(false);
        var canControlText = await GetPropertyTextAsync(busName, "org.mpris.MediaPlayer2.Player", "CanControl", cancellationToken).ConfigureAwait(false);

        var lastSeen = DateTimeOffset.UtcNow;
        _lastSeen[busName] = lastSeen;

        return MapMprisSession(
            busName,
            ExtractFirstString(identity),
            ExtractFirstString(playbackStatus),
            metadata,
            ExtractFirstInt64(positionText),
            ExtractBool(canPlayText),
            ExtractBool(canPauseText),
            ExtractBool(canGoNextText),
            ExtractBool(canGoPreviousText),
            ExtractBool(canControlText, defaultValue: true),
            lastSeen);
    }

    private static async Task<string> GetPropertyTextAsync(
        string busName,
        string interfaceName,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var result = await RunDbusSendAsync(
            [
                "--session",
                "--print-reply",
                $"--dest={busName}",
                "/org/mpris/MediaPlayer2",
                "org.freedesktop.DBus.Properties.Get",
                $"string:{interfaceName}",
                $"string:{propertyName}"
            ],
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static Task<bool> CallPlayerMethodAsync(string busName, string methodName, CancellationToken cancellationToken)
        => CallMethodAsync(busName, $"org.mpris.MediaPlayer2.Player.{methodName}", cancellationToken);

    private static Task<bool> CallRootMethodAsync(string busName, string methodName, CancellationToken cancellationToken)
        => CallMethodAsync(busName, $"org.mpris.MediaPlayer2.{methodName}", cancellationToken);

    private static async Task<bool> CallMethodAsync(string busName, string methodName, CancellationToken cancellationToken)
    {
        try
        {
            _ = await RunDbusSendAsync(
                [
                    "--session",
                    "--print-reply",
                    $"--dest={busName}",
                    "/org/mpris/MediaPlayer2",
                    methodName
                ],
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> RunDbusSendAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dbus-send",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dbus-send.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"dbus-send exited with {process.ExitCode}." : error.Trim());
        }

        return output;
    }

    private static bool TryLaunchDesktopEntry(string desktopEntry)
    {
        var normalized = desktopEntry.EndsWith(".desktop", StringComparison.Ordinal)
            ? desktopEntry
            : $"{desktopEntry}.desktop";
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications", normalized),
            Path.Combine("/usr/share/applications", normalized)
        };

        var desktopFile = candidates.FirstOrDefault(File.Exists);
        if (desktopFile is null)
        {
            return false;
        }

        var execLine = File.ReadLines(desktopFile)
            .FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(execLine))
        {
            return false;
        }

        var command = Regex.Replace(execLine[5..], @"\s+%[fFuUdDnNickvm]", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-lc", command },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractFirstString(string text)
    {
        var match = StringValueRegex.Match(text);
        return match.Success ? Unescape(match.Groups["value"].Value) : string.Empty;
    }

    private static long ExtractFirstInt64(string text)
    {
        var match = Int64ValueRegex.Match(text);
        return match.Success && long.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static bool ExtractBool(string text, bool defaultValue = false)
    {
        var match = BooleanValueRegex.Match(text);
        return match.Success
            ? string.Equals(match.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase)
            : defaultValue;
    }

    private static MusicPlaybackStatus MapPlaybackStatus(string status)
        => status.Trim() switch
        {
            "Playing" => MusicPlaybackStatus.Playing,
            "Paused" => MusicPlaybackStatus.Paused,
            "Stopped" => MusicPlaybackStatus.Stopped,
            _ => MusicPlaybackStatus.Unknown
        };

    private static string SimplifyBusName(string busName)
        => busName.StartsWith(MprisPrefix, StringComparison.Ordinal)
            ? busName[MprisPrefix.Length..].Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? busName
            : busName;

    private static byte[]? TryReadArtUrlBytes(string artUrl)
    {
        if (string.IsNullOrWhiteSpace(artUrl) ||
            !Uri.TryCreate(artUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return File.Exists(uri.LocalPath) ? File.ReadAllBytes(uri.LocalPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Unescape(string value)
        => value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
}
