using System;
using LanMountainDesktop.Shared.Data;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Services;

public sealed class AppSettingsService
{
    public static event Action<string>? SettingsSaved;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private static readonly object CacheGate = new();
    private static readonly TimeSpan CacheProbeInterval = TimeSpan.FromMilliseconds(400);

    private static string? _cachedPath;
    private static AppSettingsSnapshot? _cachedSnapshot;
    private static DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private static DateTime _lastProbeUtc = DateTime.MinValue;

    private readonly string _settingsPath;

    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    public AppSettingsService()
    {
        var settingsDirectory = AppDataPathProvider.GetSettingsDirectory();
        _settingsPath = Path.Combine(settingsDirectory, UserDataRoot.SettingsFileName);
    }

    public AppSettingsSnapshot Load()
    {
        try
        {
            lock (CacheGate)
            {
                var nowUtc = DateTime.UtcNow;
                if (TryGetCachedWithoutProbe(nowUtc, out var cached))
                {
                    return cached;
                }

                var hasFile = File.Exists(_settingsPath);
                var writeTimeUtc = hasFile
                    ? File.GetLastWriteTimeUtc(_settingsPath)
                    : DateTime.MinValue;

                _lastProbeUtc = nowUtc;
                if (TryGetCachedAfterProbe(writeTimeUtc, out cached))
                {
                    return cached;
                }

                var loadedSnapshot = hasFile
                    ? LoadSnapshotFromDisk()
                    : new AppSettingsSnapshot();

                if (hasFile && TryMergeLegacySettingsKeys(loadedSnapshot))
                {
                    writeTimeUtc = WriteSnapshotToFile(loadedSnapshot);
                }

                UpdateCache(loadedSnapshot, writeTimeUtc, nowUtc);
                return loadedSnapshot.Clone();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AppSettings", $"Failed to load settings from '{_settingsPath}'.", ex);
            return new AppSettingsSnapshot();
        }
    }

    public void Save(AppSettingsSnapshot snapshot)
    {
        var snapshotToPersist = snapshot?.Clone() ?? new AppSettingsSnapshot();

        try
        {
            var writeTimeUtc = WriteSnapshotToFile(snapshotToPersist);

            lock (CacheGate)
            {
                UpdateCache(snapshotToPersist, writeTimeUtc, DateTime.UtcNow);
            }

            SettingsSaved?.Invoke(InstanceId);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AppSettings", $"Failed to save settings to '{_settingsPath}'.", ex);
        }
    }

    private DateTime WriteSnapshotToFile(AppSettingsSnapshot snapshot)
    {
        AtomicFileWriter.WriteText(_settingsPath, JsonSerializer.Serialize(snapshot, SerializerOptions), "AppSettings");

        return File.Exists(_settingsPath)
            ? File.GetLastWriteTimeUtc(_settingsPath)
            : DateTime.UtcNow;
    }

    /// <summary>
    /// 把升级前 settings.json 里的旧键并入新键，并清掉别名以免再写回磁盘。
    /// 新旧并存时以新键为准（用户可能在升级后又改过设置）。
    /// </summary>
    internal static bool TryMergeLegacySettingsKeys(AppSettingsSnapshot snapshot)
    {
        var migrated = false;

        if (snapshot.LegacyDisabledPluginIds is { Count: > 0 } legacyDisabledIds)
        {
            migrated = true;
            if (snapshot.DisabledAirAppIds.Count == 0)
            {
                snapshot.DisabledAirAppIds = new List<string>(legacyDisabledIds);
            }
        }

        snapshot.LegacyDisabledPluginIds = null;

        if (!string.IsNullOrWhiteSpace(snapshot.LegacyDevPluginPath))
        {
            migrated = true;
            if (string.IsNullOrWhiteSpace(snapshot.DevAirAppPath))
            {
                snapshot.DevAirAppPath = snapshot.LegacyDevPluginPath;
            }
        }

        snapshot.LegacyDevPluginPath = null;

        return migrated;
    }

    private bool TryGetCachedWithoutProbe(DateTime nowUtc, out AppSettingsSnapshot snapshot)
    {
        if (string.Equals(_cachedPath, _settingsPath, StringComparison.Ordinal) &&
            _cachedSnapshot is not null &&
            nowUtc - _lastProbeUtc < CacheProbeInterval)
        {
            snapshot = _cachedSnapshot.Clone();
            return true;
        }

        snapshot = null!;
        return false;
    }

    private bool TryGetCachedAfterProbe(DateTime writeTimeUtc, out AppSettingsSnapshot snapshot)
    {
        if (string.Equals(_cachedPath, _settingsPath, StringComparison.Ordinal) &&
            _cachedSnapshot is not null &&
            writeTimeUtc == _cachedWriteTimeUtc)
        {
            snapshot = _cachedSnapshot.Clone();
            return true;
        }

        snapshot = null!;
        return false;
    }

    private AppSettingsSnapshot LoadSnapshotFromDisk()
    {
        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettingsSnapshot>(json, SerializerOptions) ?? new AppSettingsSnapshot();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AppSettings", $"Failed to deserialize settings from '{_settingsPath}'.", ex);
            return new AppSettingsSnapshot();
        }
    }

    private void UpdateCache(AppSettingsSnapshot snapshot, DateTime writeTimeUtc, DateTime probeTimeUtc)
    {
        _cachedPath = _settingsPath;
        _cachedSnapshot = snapshot.Clone();
        _cachedWriteTimeUtc = writeTimeUtc;
        _lastProbeUtc = probeTimeUtc;
    }
}
