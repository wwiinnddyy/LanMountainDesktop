using System;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;

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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(appData, "LanMountainDesktop");
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
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
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(snapshotToPersist, SerializerOptions);
            File.WriteAllText(_settingsPath, json);

            var writeTimeUtc = File.Exists(_settingsPath)
                ? File.GetLastWriteTimeUtc(_settingsPath)
                : DateTime.UtcNow;

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
