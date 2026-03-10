using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class DesktopLayoutSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private static readonly object CacheGate = new();
    private static readonly TimeSpan CacheProbeInterval = TimeSpan.FromMilliseconds(400);

    private static string? _cachedPath;
    private static DesktopLayoutSettingsSnapshot? _cachedSnapshot;
    private static DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private static DateTime _lastProbeUtc = DateTime.MinValue;

    private readonly string _settingsPath;
    private readonly string _legacyAppSettingsPath;

    public DesktopLayoutSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(appData, "LanMountainDesktop");
        _settingsPath = Path.Combine(settingsDirectory, "desktop-layout-settings.json");
        _legacyAppSettingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public DesktopLayoutSettingsSnapshot Load()
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

                DesktopLayoutSettingsSnapshot loadedSnapshot;
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
                    loadedSnapshot = new DesktopLayoutSettingsSnapshot();
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
            AppLogger.Warn("DesktopLayout", $"Failed to load desktop layout settings from '{_settingsPath}'.", ex);
            return new DesktopLayoutSettingsSnapshot();
        }
    }

    public void Save(DesktopLayoutSettingsSnapshot snapshot)
    {
        var snapshotToPersist = NormalizeSnapshot(snapshot);

        try
        {
            var writeTimeUtc = PersistSnapshotToDisk(snapshotToPersist);

            lock (CacheGate)
            {
                UpdateCache(snapshotToPersist, writeTimeUtc, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopLayout", $"Failed to save desktop layout settings to '{_settingsPath}'.", ex);
        }
    }

    private bool TryGetCachedWithoutProbe(DateTime nowUtc, out DesktopLayoutSettingsSnapshot snapshot)
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

    private bool TryGetCachedAfterProbe(DateTime writeTimeUtc, out DesktopLayoutSettingsSnapshot snapshot)
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

    private DesktopLayoutSettingsSnapshot LoadSnapshotFromDisk()
    {
        try
        {
            var json = File.ReadAllText(_settingsPath);
            var snapshot = JsonSerializer.Deserialize<DesktopLayoutSettingsSnapshot>(json, SerializerOptions);
            return NormalizeSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopLayout", $"Failed to deserialize desktop layout settings from '{_settingsPath}'.", ex);
            return new DesktopLayoutSettingsSnapshot();
        }
    }

    private bool TryLoadLegacySnapshot(out DesktopLayoutSettingsSnapshot snapshot)
    {
        snapshot = new DesktopLayoutSettingsSnapshot();

        try
        {
            if (!File.Exists(_legacyAppSettingsPath))
            {
                return false;
            }

            var legacyJson = File.ReadAllText(_legacyAppSettingsPath);
            var legacy = JsonSerializer.Deserialize<LegacyDesktopLayoutSettingsSnapshot>(legacyJson, SerializerOptions);
            if (legacy is null)
            {
                return false;
            }

            snapshot = new DesktopLayoutSettingsSnapshot
            {
                DesktopPageCount = legacy.DesktopPageCount,
                CurrentDesktopSurfaceIndex = legacy.CurrentDesktopSurfaceIndex,
                DesktopComponentPlacements = legacy.DesktopComponentPlacements ?? []
            };

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopLayout", $"Failed to migrate legacy desktop layout settings from '{_legacyAppSettingsPath}'.", ex);
            return false;
        }
    }

    private DateTime PersistSnapshotToDisk(DesktopLayoutSettingsSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(_settingsPath, json);

        return File.Exists(_settingsPath)
            ? File.GetLastWriteTimeUtc(_settingsPath)
            : DateTime.UtcNow;
    }

    private static DesktopLayoutSettingsSnapshot NormalizeSnapshot(DesktopLayoutSettingsSnapshot? snapshot)
    {
        var normalized = snapshot?.Clone() ?? new DesktopLayoutSettingsSnapshot();
        normalized.DesktopPageCount = Math.Max(1, normalized.DesktopPageCount);
        normalized.CurrentDesktopSurfaceIndex = Math.Max(0, normalized.CurrentDesktopSurfaceIndex);

        var placements = new List<DesktopComponentPlacementSnapshot>(normalized.DesktopComponentPlacements?.Count ?? 0);
        if (normalized.DesktopComponentPlacements is not null)
        {
            foreach (var placement in normalized.DesktopComponentPlacements)
            {
                if (placement is null)
                {
                    continue;
                }

                placements.Add(new DesktopComponentPlacementSnapshot
                {
                    PlacementId = placement.PlacementId?.Trim() ?? string.Empty,
                    PageIndex = Math.Max(0, placement.PageIndex),
                    ComponentId = placement.ComponentId?.Trim() ?? string.Empty,
                    Row = Math.Max(0, placement.Row),
                    Column = Math.Max(0, placement.Column),
                    WidthCells = Math.Max(1, placement.WidthCells),
                    HeightCells = Math.Max(1, placement.HeightCells)
                });
            }
        }

        normalized.DesktopComponentPlacements = placements;
        return normalized;
    }

    private void UpdateCache(DesktopLayoutSettingsSnapshot snapshot, DateTime writeTimeUtc, DateTime probeTimeUtc)
    {
        _cachedPath = _settingsPath;
        _cachedSnapshot = snapshot.Clone();
        _cachedWriteTimeUtc = writeTimeUtc;
        _lastProbeUtc = probeTimeUtc;
    }

    private sealed class LegacyDesktopLayoutSettingsSnapshot
    {
        public int DesktopPageCount { get; set; } = 1;

        public int CurrentDesktopSurfaceIndex { get; set; }

        public List<DesktopComponentPlacementSnapshot>? DesktopComponentPlacements { get; set; }
    }
}
