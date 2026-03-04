using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed class WindowsSmtcMusicControlService : IMusicControlService
{
    private static readonly Type? SessionManagerType = ResolveWinRtType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager");
    private static readonly Type? AppInfoType = ResolveWinRtType("Windows.ApplicationModel.AppInfo");
    private static readonly MethodInfo? RequestSessionManagerAsyncMethod =
        SessionManagerType?.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static);
    private static readonly MethodInfo? AsTaskGenericMethodDefinition = ResolveAsTaskGenericMethod();
    private static readonly MethodInfo? AsStreamForReadMethod = ResolveAsStreamForReadMethod();

    private static readonly SemaphoreSlim ManagerLock = new(1, 1);
    private static object? _sessionManager;

    private readonly ConcurrentDictionary<string, string> _sourceAppNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    private string _thumbnailKey = string.Empty;
    private byte[]? _thumbnailBytesCache;

    public async Task<MusicPlaybackState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRuntimeSupported())
        {
            return MusicPlaybackState.Unsupported();
        }

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var session = await GetCurrentSessionAsync(cancellationToken);
            if (session is null)
            {
                return MusicPlaybackState.NoSession(isSupported: true);
            }

            var mediaProperties = await TryGetMediaPropertiesAsync(session, cancellationToken);
            var title = ReadStringProperty(mediaProperties, "Title");
            var artist = ReadStringProperty(mediaProperties, "Artist");
            var albumTitle = ReadStringProperty(mediaProperties, "AlbumTitle");

            var playbackInfo = GetPropertyValue(session, "PlaybackInfo") ?? InvokeMethod(session, "GetPlaybackInfo");
            var controls = GetPropertyValue(playbackInfo, "Controls");

            var playbackStatusRaw = ReadIntProperty(playbackInfo, "PlaybackStatus");
            var canPlayPause = ReadBoolProperty(controls, "IsPauseEnabled") || ReadBoolProperty(controls, "IsPlayEnabled");
            var canSkipNext = ReadBoolProperty(controls, "IsNextEnabled");
            var canSkipPrevious = ReadBoolProperty(controls, "IsPreviousEnabled");

            var sourceAppId = ReadStringProperty(session, "SourceAppUserModelId");
            var sourceAppName = await ResolveSourceAppDisplayNameAsync(sourceAppId, cancellationToken);

            var timeline = InvokeMethod(session, "GetTimelineProperties");
            var position = ReadTimeSpanProperty(timeline, "Position");
            var start = ReadTimeSpanProperty(timeline, "StartTime");
            var end = ReadTimeSpanProperty(timeline, "EndTime");

            var duration = end - start;
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            var normalizedPosition = position - start;
            if (normalizedPosition < TimeSpan.Zero)
            {
                normalizedPosition = TimeSpan.Zero;
            }

            if (duration > TimeSpan.Zero && normalizedPosition > duration)
            {
                normalizedPosition = duration;
            }

            var thumbnailBytes = await ResolveThumbnailBytesAsync(
                mediaProperties,
                sourceAppId,
                title,
                artist,
                albumTitle,
                cancellationToken);

            return new MusicPlaybackState(
                IsSupported: true,
                HasSession: true,
                SourceAppId: sourceAppId,
                SourceAppName: sourceAppName,
                Title: title,
                Artist: artist,
                AlbumTitle: albumTitle,
                ThumbnailBytes: thumbnailBytes,
                Position: normalizedPosition,
                Duration: duration,
                PlaybackStatus: MapPlaybackStatus(playbackStatusRaw),
                CanPlayPause: canPlayPause,
                CanSkipPrevious: canSkipPrevious,
                CanSkipNext: canSkipNext);
        }
        catch
        {
            return MusicPlaybackState.NoSession(isSupported: true);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRuntimeSupported())
        {
            return false;
        }

        var session = await GetCurrentSessionAsync(cancellationToken);
        if (session is null)
        {
            return false;
        }

        var playbackInfo = GetPropertyValue(session, "PlaybackInfo") ?? InvokeMethod(session, "GetPlaybackInfo");
        var controls = GetPropertyValue(playbackInfo, "Controls");
        var playbackStatusRaw = ReadIntProperty(playbackInfo, "PlaybackStatus");

        object? operation = null;
        if (playbackStatusRaw == 4 && ReadBoolProperty(controls, "IsPauseEnabled"))
        {
            operation = InvokeMethod(session, "TryPauseAsync");
        }
        else if (ReadBoolProperty(controls, "IsPlayEnabled"))
        {
            operation = InvokeMethod(session, "TryPlayAsync");
        }
        else if (ReadBoolProperty(controls, "IsPauseEnabled"))
        {
            operation = InvokeMethod(session, "TryPauseAsync");
        }
        else
        {
            operation = InvokeMethod(session, "TryTogglePlayPauseAsync");
        }

        return await AwaitBooleanWinRtOperationAsync(operation, cancellationToken);
    }

    public async Task<bool> SkipNextAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRuntimeSupported())
        {
            return false;
        }

        var session = await GetCurrentSessionAsync(cancellationToken);
        if (session is null)
        {
            return false;
        }

        var playbackInfo = GetPropertyValue(session, "PlaybackInfo") ?? InvokeMethod(session, "GetPlaybackInfo");
        var controls = GetPropertyValue(playbackInfo, "Controls");
        if (!ReadBoolProperty(controls, "IsNextEnabled"))
        {
            return false;
        }

        return await AwaitBooleanWinRtOperationAsync(InvokeMethod(session, "TrySkipNextAsync"), cancellationToken);
    }

    public async Task<bool> SkipPreviousAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRuntimeSupported())
        {
            return false;
        }

        var session = await GetCurrentSessionAsync(cancellationToken);
        if (session is null)
        {
            return false;
        }

        var playbackInfo = GetPropertyValue(session, "PlaybackInfo") ?? InvokeMethod(session, "GetPlaybackInfo");
        var controls = GetPropertyValue(playbackInfo, "Controls");
        if (!ReadBoolProperty(controls, "IsPreviousEnabled"))
        {
            return false;
        }

        return await AwaitBooleanWinRtOperationAsync(InvokeMethod(session, "TrySkipPreviousAsync"), cancellationToken);
    }

    public async Task<bool> LaunchSourceAppAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRuntimeSupported())
        {
            return false;
        }

        var session = await GetCurrentSessionAsync(cancellationToken);
        if (session is null)
        {
            return false;
        }

        var sourceAppId = ReadStringProperty(session, "SourceAppUserModelId");
        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return false;
        }

        return TryOpenSourceApp(sourceAppId);
    }

    private async Task<object?> GetCurrentSessionAsync(CancellationToken cancellationToken)
    {
        var manager = await GetSessionManagerAsync(cancellationToken);
        return manager is null ? null : InvokeMethod(manager, "GetCurrentSession");
    }

    private static async Task<object?> GetSessionManagerAsync(CancellationToken cancellationToken)
    {
        if (_sessionManager is not null)
        {
            return _sessionManager;
        }

        await ManagerLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessionManager is not null)
            {
                return _sessionManager;
            }

            var operation = RequestSessionManagerAsyncMethod?.Invoke(null, null);
            var manager = await AwaitWinRtOperationAsync(operation, cancellationToken);
            _sessionManager = manager;
            return manager;
        }
        finally
        {
            ManagerLock.Release();
        }
    }

    private async Task<object?> TryGetMediaPropertiesAsync(object session, CancellationToken cancellationToken)
    {
        var operation = InvokeMethod(session, "TryGetMediaPropertiesAsync");
        return await AwaitWinRtOperationAsync(operation, cancellationToken);
    }

    private async Task<byte[]?> ResolveThumbnailBytesAsync(
        object? mediaProperties,
        string sourceAppId,
        string title,
        string artist,
        string albumTitle,
        CancellationToken cancellationToken)
    {
        var key = $"{sourceAppId}|{title}|{artist}|{albumTitle}";
        if (string.Equals(key, _thumbnailKey, StringComparison.Ordinal) && _thumbnailBytesCache is not null)
        {
            return _thumbnailBytesCache;
        }

        var thumbnailReference = GetPropertyValue(mediaProperties, "Thumbnail");
        var thumbnailBytes = await TryReadThumbnailBytesAsync(thumbnailReference, cancellationToken);

        _thumbnailKey = key;
        _thumbnailBytesCache = thumbnailBytes;
        return thumbnailBytes;
    }

    private static async Task<byte[]?> TryReadThumbnailBytesAsync(object? thumbnailReference, CancellationToken cancellationToken)
    {
        if (thumbnailReference is null)
        {
            return null;
        }

        object? randomAccessStream = null;
        try
        {
            var openReadAsyncOperation = InvokeMethod(thumbnailReference, "OpenReadAsync");
            randomAccessStream = await AwaitWinRtOperationAsync(openReadAsyncOperation, cancellationToken);
            if (randomAccessStream is null || AsStreamForReadMethod is null)
            {
                return null;
            }

            using var dotnetStream = AsStreamForReadMethod.Invoke(null, [randomAccessStream]) as Stream;
            if (dotnetStream is null)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            await dotnetStream.CopyToAsync(buffer, cancellationToken);
            return buffer.Length > 0 ? buffer.ToArray() : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (randomAccessStream is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task<string> ResolveSourceAppDisplayNameAsync(string sourceAppId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return string.Empty;
        }

        if (_sourceAppNameCache.TryGetValue(sourceAppId, out var cached))
        {
            return cached;
        }

        var resolved = sourceAppId;
        try
        {
            if (AppInfoType is not null)
            {
                var getFromAumidMethod = AppInfoType.GetMethod(
                    "GetFromAppUserModelId",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeof(string)],
                    null);
                var appInfo = getFromAumidMethod?.Invoke(null, [sourceAppId]);
                var displayInfo = GetPropertyValue(appInfo, "DisplayInfo");
                var displayName = ReadStringProperty(displayInfo, "DisplayName");
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    resolved = displayName;
                }
                else
                {
                    resolved = SimplifySourceAppId(sourceAppId);
                }
            }
            else
            {
                resolved = SimplifySourceAppId(sourceAppId);
            }
        }
        catch
        {
            resolved = SimplifySourceAppId(sourceAppId);
        }

        _sourceAppNameCache[sourceAppId] = resolved;
        await Task.CompletedTask;
        return resolved;
    }

    private static string SimplifySourceAppId(string sourceAppId)
    {
        var text = sourceAppId.Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var exclamationIndex = text.IndexOf('!');
        if (exclamationIndex > 0)
        {
            text = text[..exclamationIndex];
        }

        var packageSplit = text.Split('_');
        if (packageSplit.Length > 0 && packageSplit[0].Length > 0)
        {
            text = packageSplit[0];
        }

        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            text = Path.GetFileNameWithoutExtension(text);
        }

        if (text.Contains('.'))
        {
            var lastSegment = text.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                text = lastSegment;
            }
        }

        return text.Replace('_', ' ').Replace('-', ' ').Trim();
    }

    private static bool TryOpenSourceApp(string sourceAppId)
    {
        try
        {
            var launchTarget = $"shell:AppsFolder\\{sourceAppId}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = launchTarget,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> AwaitBooleanWinRtOperationAsync(object? operation, CancellationToken cancellationToken)
    {
        var result = await AwaitWinRtOperationAsync(operation, cancellationToken);
        return result is bool boolValue && boolValue;
    }

    private static async Task<object?> AwaitWinRtOperationAsync(object? operation, CancellationToken cancellationToken)
    {
        if (operation is null || AsTaskGenericMethodDefinition is null)
        {
            return null;
        }

        var resultType = ResolveWinRtOperationResultType(operation.GetType());
        if (resultType is null)
        {
            return null;
        }

        var asTaskMethod = AsTaskGenericMethodDefinition.MakeGenericMethod(resultType);
        var taskObject = asTaskMethod.Invoke(null, [operation]) as Task;
        if (taskObject is null)
        {
            return null;
        }

        await taskObject.WaitAsync(cancellationToken);
        return taskObject.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(taskObject);
    }

    private static Type? ResolveWinRtOperationResultType(Type operationType)
    {
        if (operationType.IsGenericType)
        {
            var genericArguments = operationType.GetGenericArguments();
            if (genericArguments.Length == 1)
            {
                return genericArguments[0];
            }
        }

        foreach (var iface in operationType.GetInterfaces())
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var genericTypeDef = iface.GetGenericTypeDefinition();
            if (string.Equals(genericTypeDef.FullName, "Windows.Foundation.IAsyncOperation`1", StringComparison.Ordinal))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static MethodInfo? ResolveAsTaskGenericMethod()
    {
        var type = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime", throwOnError: false);
        return type?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                method.Name == "AsTask" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 1);
    }

    private static MethodInfo? ResolveAsStreamForReadMethod()
    {
        var type = Type.GetType("System.IO.WindowsRuntimeStreamExtensions, System.Runtime.WindowsRuntime", throwOnError: false);
        return type?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                method.Name == "AsStreamForRead" &&
                method.GetParameters().Length == 1);
    }

    private static Type? ResolveWinRtType(string typeName)
    {
        return Type.GetType($"{typeName}, Windows, ContentType=WindowsRuntime", throwOnError: false);
    }

    private static bool IsRuntimeSupported()
    {
        return OperatingSystem.IsWindows() &&
               SessionManagerType is not null &&
               RequestSessionManagerAsyncMethod is not null &&
               AsTaskGenericMethodDefinition is not null;
    }

    private static object? InvokeMethod(object? target, string methodName)
    {
        return target?.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, null);
    }

    private static object? GetPropertyValue(object? target, string propertyName)
    {
        return target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }

    private static string ReadStringProperty(object? target, string propertyName)
    {
        return GetPropertyValue(target, propertyName)?.ToString()?.Trim() ?? string.Empty;
    }

    private static bool ReadBoolProperty(object? target, string propertyName)
    {
        var value = GetPropertyValue(target, propertyName);
        return value is bool boolValue && boolValue;
    }

    private static int ReadIntProperty(object? target, string propertyName)
    {
        var value = GetPropertyValue(target, propertyName);
        if (value is null)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private static TimeSpan ReadTimeSpanProperty(object? target, string propertyName)
    {
        var value = GetPropertyValue(target, propertyName);
        return value is TimeSpan timeSpan ? timeSpan : TimeSpan.Zero;
    }

    private static MusicPlaybackStatus MapPlaybackStatus(int rawStatus)
    {
        return rawStatus switch
        {
            1 => MusicPlaybackStatus.Opened,
            2 => MusicPlaybackStatus.Changing,
            3 => MusicPlaybackStatus.Stopped,
            4 => MusicPlaybackStatus.Playing,
            5 => MusicPlaybackStatus.Paused,
            _ => MusicPlaybackStatus.Unknown
        };
    }
}
