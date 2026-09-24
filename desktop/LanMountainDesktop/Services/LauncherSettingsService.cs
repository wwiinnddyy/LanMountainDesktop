using System;
using LanMountainDesktop.Shared.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Services;

public sealed class LauncherSettingsService
{
    public static event Action<string>? SettingsSaved;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private static readonly object CacheGate = new();
    /// <summary>判据与状态都在 <see cref="SettingsSnapshotCache{T}" /> 里；锁仍由本服务按自己的 CacheGate 罩。</summary>
    private static readonly SettingsSnapshotCache<LauncherSettingsSnapshot> Cache =
        new(static snapshot => snapshot.Clone(), TimeSpan.FromMilliseconds(400));

    private readonly string _settingsPath;
    private readonly string _legacyAppSettingsPath;

    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    public LauncherSettingsService()
    {
        var settingsDirectory = AppDataPathProvider.GetSettingsDirectory();
        _settingsPath = Path.Combine(settingsDirectory, "launcher-settings.json");
        _legacyAppSettingsPath = Path.Combine(settingsDirectory, UserDataRoot.SettingsFileName);
    }

    public LauncherSettingsSnapshot Load()
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

                Cache.MarkProbed(nowUtc);
                if (TryGetCachedAfterProbe(writeTimeUtc, out cached))
                {
                    return cached;
                }

                LauncherSettingsSnapshot loadedSnapshot;
                var loadedFromLegacy = false;
                if (hasFile)
                {
                    loadedSnapshot = LoadSnapshotFromDisk();
                }
                else if (TryLoadLegacySnapshot(out var migratedSnapshot))
                {
                    loadedSnapshot = migratedSnapshot;
                    loadedFromLegacy = true;
                }
                else
                {
                    loadedSnapshot = new LauncherSettingsSnapshot();
                }

                var normalizedSnapshot = NormalizeSnapshot(loadedSnapshot);
                if (loadedFromLegacy)
                {
                    writeTimeUtc = PersistSnapshotToDisk(normalizedSnapshot);
                }

                UpdateCache(normalizedSnapshot, writeTimeUtc, nowUtc);
                return normalizedSnapshot.Clone();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherSettings", $"Failed to load launcher settings from '{_settingsPath}'.", ex);
            return new LauncherSettingsSnapshot();
        }
    }

    public void Save(LauncherSettingsSnapshot snapshot)
    {
        var snapshotToPersist = NormalizeSnapshot(snapshot);

        try
        {
            var writeTimeUtc = PersistSnapshotToDisk(snapshotToPersist);

            lock (CacheGate)
            {
                UpdateCache(snapshotToPersist, writeTimeUtc, DateTime.UtcNow);
            }

            SettingsSaved?.Invoke(InstanceId);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherSettings", $"Failed to save launcher settings to '{_settingsPath}'.", ex);
        }
    }

    /// <summary>还在探针窗口里就别再问磁盘（这条判据的来历见 SettingsSnapshotCache）。</summary>
    private bool TryGetCachedWithoutProbe(DateTime nowUtc, out LauncherSettingsSnapshot snapshot) =>
        Cache.TryGetWithinProbeWindow(_settingsPath, nowUtc, out snapshot);

    private bool TryGetCachedAfterProbe(DateTime writeTimeUtc, out LauncherSettingsSnapshot snapshot) =>
        Cache.TryGetAtCachedWriteTime(_settingsPath, writeTimeUtc, out snapshot);

    private LauncherSettingsSnapshot LoadSnapshotFromDisk()
    {
        try
        {
            var json = File.ReadAllText(_settingsPath);
            var snapshot = JsonSerializer.Deserialize<LauncherSettingsSnapshot>(json, SerializerOptions);
            return NormalizeSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherSettings", $"Failed to deserialize launcher settings from '{_settingsPath}'.", ex);
            return new LauncherSettingsSnapshot();
        }
    }

    private bool TryLoadLegacySnapshot(out LauncherSettingsSnapshot snapshot)
    {
        snapshot = new LauncherSettingsSnapshot();

        try
        {
            if (!File.Exists(_legacyAppSettingsPath))
            {
                return false;
            }

            var legacyJson = File.ReadAllText(_legacyAppSettingsPath);
            var legacy = JsonSerializer.Deserialize<LegacyLauncherSettingsSnapshot>(legacyJson, SerializerOptions);
            if (legacy is null)
            {
                return false;
            }

            snapshot = new LauncherSettingsSnapshot
            {
                HiddenLauncherFolderPaths = legacy.HiddenLauncherFolderPaths ?? [],
                HiddenLauncherAppPaths = legacy.HiddenLauncherAppPaths ?? []
            };

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherSettings", $"Failed to migrate legacy launcher settings from '{_legacyAppSettingsPath}'.", ex);
            return false;
        }
    }

    private DateTime PersistSnapshotToDisk(LauncherSettingsSnapshot snapshot)
    {
        AtomicFileWriter.WriteText(_settingsPath, JsonSerializer.Serialize(snapshot, SerializerOptions), "LauncherSettings");

        return File.Exists(_settingsPath)
            ? File.GetLastWriteTimeUtc(_settingsPath)
            : DateTime.UtcNow;
    }

    private static LauncherSettingsSnapshot NormalizeSnapshot(LauncherSettingsSnapshot? snapshot)
    {
        var normalized = snapshot?.Clone() ?? new LauncherSettingsSnapshot();
        normalized.HiddenLauncherFolderPaths = NormalizeKeys(normalized.HiddenLauncherFolderPaths);
        normalized.HiddenLauncherAppPaths = NormalizeKeys(normalized.HiddenLauncherAppPaths);
        return normalized;
    }

    private static List<string> NormalizeKeys(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void UpdateCache(LauncherSettingsSnapshot snapshot, DateTime writeTimeUtc, DateTime probeTimeUtc) =>
        Cache.Update(_settingsPath, snapshot, writeTimeUtc, probeTimeUtc);

    private sealed class LegacyLauncherSettingsSnapshot
    {
        public List<string>? HiddenLauncherFolderPaths { get; set; }

        public List<string>? HiddenLauncherAppPaths { get; set; }
    }
}
