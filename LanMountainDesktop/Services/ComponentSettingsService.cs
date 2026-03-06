using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class ComponentSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static readonly object CacheGate = new();
    private static readonly TimeSpan CacheProbeInterval = TimeSpan.FromMilliseconds(400);

    private static string? _cachedPath;
    private static ComponentSettingsSnapshot? _cachedSnapshot;
    private static DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private static DateTime _lastProbeUtc = DateTime.MinValue;

    private readonly string _settingsPath;
    private readonly string _legacyAppSettingsPath;

    public ComponentSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(appData, "LanMountainDesktop");
        _settingsPath = Path.Combine(settingsDirectory, "component-settings.json");
        _legacyAppSettingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public ComponentSettingsSnapshot Load()
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

                ComponentSettingsSnapshot loadedSnapshot;
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
                    loadedSnapshot = new ComponentSettingsSnapshot();
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
        catch
        {
            return new ComponentSettingsSnapshot();
        }
    }

    public void Save(ComponentSettingsSnapshot snapshot)
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
        catch
        {
            // Swallow persistence errors to keep UI interactions uninterrupted.
        }
    }

    private bool TryGetCachedWithoutProbe(DateTime nowUtc, out ComponentSettingsSnapshot snapshot)
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

    private bool TryGetCachedAfterProbe(DateTime writeTimeUtc, out ComponentSettingsSnapshot snapshot)
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

    private ComponentSettingsSnapshot LoadSnapshotFromDisk()
    {
        try
        {
            var json = File.ReadAllText(_settingsPath);
            var snapshot = JsonSerializer.Deserialize<ComponentSettingsSnapshot>(json, SerializerOptions);
            return NormalizeSnapshot(snapshot);
        }
        catch
        {
            return new ComponentSettingsSnapshot();
        }
    }

    private bool TryLoadLegacySnapshot(out ComponentSettingsSnapshot snapshot)
    {
        snapshot = new ComponentSettingsSnapshot();
        try
        {
            if (!File.Exists(_legacyAppSettingsPath))
            {
                return false;
            }

            var legacyJson = File.ReadAllText(_legacyAppSettingsPath);
            var legacy = JsonSerializer.Deserialize<LegacyComponentSettingsSnapshot>(legacyJson, SerializerOptions);
            if (legacy is null)
            {
                return false;
            }

            snapshot = new ComponentSettingsSnapshot
            {
                DailyArtworkMirrorSource = legacy.DailyArtworkMirrorSource,
                ImportedClassSchedules = legacy.ImportedClassSchedules ?? [],
                ActiveImportedClassScheduleId = legacy.ActiveImportedClassScheduleId ?? string.Empty,
                StudyEnvironmentShowDisplayDb = legacy.StudyEnvironmentShowDisplayDb,
                StudyEnvironmentShowDbfs = legacy.StudyEnvironmentShowDbfs,
                DesktopClockTimeZoneId = legacy.DesktopClockTimeZoneId,
                DesktopClockSecondHandMode = legacy.DesktopClockSecondHandMode,
                WorldClockTimeZoneIds = legacy.WorldClockTimeZoneIds ?? [],
                WorldClockSecondHandMode = legacy.WorldClockSecondHandMode,
                CnrDailyNewsAutoRotateEnabled = legacy.CnrDailyNewsAutoRotateEnabled,
                CnrDailyNewsAutoRotateIntervalMinutes = legacy.CnrDailyNewsAutoRotateIntervalMinutes,
                DailyWordAutoRefreshEnabled = legacy.DailyWordAutoRefreshEnabled,
                DailyWordAutoRefreshIntervalMinutes = legacy.DailyWordAutoRefreshIntervalMinutes,
                BilibiliHotSearchAutoRefreshEnabled = legacy.BilibiliHotSearchAutoRefreshEnabled,
                BilibiliHotSearchAutoRefreshIntervalMinutes = legacy.BilibiliHotSearchAutoRefreshIntervalMinutes,
                WeatherAutoRefreshEnabled = legacy.WeatherAutoRefreshEnabled,
                WeatherAutoRefreshIntervalMinutes = legacy.WeatherAutoRefreshIntervalMinutes,
                Stcn24ForumAutoRefreshEnabled = legacy.Stcn24ForumAutoRefreshEnabled,
                Stcn24ForumAutoRefreshIntervalMinutes = legacy.Stcn24ForumAutoRefreshIntervalMinutes,
                Stcn24ForumSourceType = legacy.Stcn24ForumSourceType
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private DateTime PersistSnapshotToDisk(ComponentSettingsSnapshot snapshot)
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

    private static ComponentSettingsSnapshot NormalizeSnapshot(ComponentSettingsSnapshot? snapshot)
    {
        var normalized = snapshot?.Clone() ?? new ComponentSettingsSnapshot();

        normalized.DailyArtworkMirrorSource = DailyArtworkMirrorSources.Normalize(normalized.DailyArtworkMirrorSource);
        normalized.ImportedClassSchedules = NormalizeImportedSchedules(normalized.ImportedClassSchedules);
        normalized.ActiveImportedClassScheduleId = NormalizeActiveScheduleId(
            normalized.ActiveImportedClassScheduleId,
            normalized.ImportedClassSchedules);

        if (!normalized.StudyEnvironmentShowDisplayDb && !normalized.StudyEnvironmentShowDbfs)
        {
            normalized.StudyEnvironmentShowDisplayDb = true;
        }

        normalized.DesktopClockTimeZoneId = NormalizeDesktopClockTimeZoneId(normalized.DesktopClockTimeZoneId);
        normalized.DesktopClockSecondHandMode = ClockSecondHandMode.Normalize(normalized.DesktopClockSecondHandMode);
        normalized.WorldClockTimeZoneIds = WorldClockTimeZoneCatalog
            .NormalizeTimeZoneIds(normalized.WorldClockTimeZoneIds)
            .ToList();
        normalized.WorldClockSecondHandMode = ClockSecondHandMode.Normalize(normalized.WorldClockSecondHandMode);
        normalized.CnrDailyNewsAutoRotateIntervalMinutes = NormalizeCnrInterval(normalized.CnrDailyNewsAutoRotateIntervalMinutes);
        normalized.DailyWordAutoRefreshIntervalMinutes = NormalizeDailyWordInterval(normalized.DailyWordAutoRefreshIntervalMinutes);
        normalized.BilibiliHotSearchAutoRefreshIntervalMinutes = NormalizeBilibiliHotSearchInterval(
            normalized.BilibiliHotSearchAutoRefreshIntervalMinutes);
        normalized.WeatherAutoRefreshIntervalMinutes = NormalizeWeatherInterval(normalized.WeatherAutoRefreshIntervalMinutes);
        normalized.Stcn24ForumAutoRefreshIntervalMinutes = NormalizeStcn24ForumInterval(normalized.Stcn24ForumAutoRefreshIntervalMinutes);
        normalized.Stcn24ForumSourceType = Stcn24ForumSourceTypes.Normalize(normalized.Stcn24ForumSourceType);

        return normalized;
    }

    private static List<ImportedClassScheduleSnapshot> NormalizeImportedSchedules(
        IReadOnlyList<ImportedClassScheduleSnapshot>? schedules)
    {
        if (schedules is null || schedules.Count == 0)
        {
            return [];
        }

        var result = new List<ImportedClassScheduleSnapshot>(schedules.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schedule in schedules)
        {
            if (schedule is null)
            {
                continue;
            }

            var id = schedule.Id?.Trim() ?? string.Empty;
            var filePath = schedule.FilePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            if (!seenIds.Add(id))
            {
                continue;
            }

            result.Add(new ImportedClassScheduleSnapshot
            {
                Id = id,
                DisplayName = schedule.DisplayName?.Trim() ?? string.Empty,
                FilePath = filePath
            });
        }

        return result;
    }

    private static string NormalizeActiveScheduleId(
        string? activeScheduleId,
        IReadOnlyList<ImportedClassScheduleSnapshot> schedules)
    {
        var activeId = activeScheduleId?.Trim() ?? string.Empty;
        if (schedules.Count == 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(activeId))
        {
            return schedules[0].Id;
        }

        return schedules.Any(item => string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase))
            ? activeId
            : schedules[0].Id;
    }

    private static string NormalizeDesktopClockTimeZoneId(string? timeZoneId)
    {
        var normalizedId = string.IsNullOrWhiteSpace(timeZoneId)
            ? "China Standard Time"
            : timeZoneId.Trim();
        return WorldClockTimeZoneCatalog.ResolveTimeZoneOrLocal(normalizedId).Id;
    }

    private static int NormalizeCnrInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 60);
    }

    private static int NormalizeDailyWordInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 360);
    }

    private static int NormalizeBilibiliHotSearchInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 15);
    }

    private static int NormalizeWeatherInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 12);
    }

    private static int NormalizeStcn24ForumInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 20);
    }

    private void UpdateCache(ComponentSettingsSnapshot snapshot, DateTime writeTimeUtc, DateTime probeTimeUtc)
    {
        _cachedPath = _settingsPath;
        _cachedSnapshot = snapshot.Clone();
        _cachedWriteTimeUtc = writeTimeUtc;
        _lastProbeUtc = probeTimeUtc;
    }

    private sealed class LegacyComponentSettingsSnapshot
    {
        public string DailyArtworkMirrorSource { get; set; } = DailyArtworkMirrorSources.Overseas;

        public List<ImportedClassScheduleSnapshot>? ImportedClassSchedules { get; set; }

        public string? ActiveImportedClassScheduleId { get; set; }

        public bool StudyEnvironmentShowDisplayDb { get; set; } = true;

        public bool StudyEnvironmentShowDbfs { get; set; }

        public string DesktopClockTimeZoneId { get; set; } = "China Standard Time";

        public string DesktopClockSecondHandMode { get; set; } = "Tick";

        public List<string>? WorldClockTimeZoneIds { get; set; }

        public string WorldClockSecondHandMode { get; set; } = "Tick";

        public bool CnrDailyNewsAutoRotateEnabled { get; set; } = true;

        public int CnrDailyNewsAutoRotateIntervalMinutes { get; set; } = 60;

        public bool DailyWordAutoRefreshEnabled { get; set; } = true;

        public int DailyWordAutoRefreshIntervalMinutes { get; set; } = 360;

        public bool BilibiliHotSearchAutoRefreshEnabled { get; set; } = true;

        public int BilibiliHotSearchAutoRefreshIntervalMinutes { get; set; } = 15;

        public bool WeatherAutoRefreshEnabled { get; set; } = true;

        public int WeatherAutoRefreshIntervalMinutes { get; set; } = 12;

        public bool Stcn24ForumAutoRefreshEnabled { get; set; } = true;

        public int Stcn24ForumAutoRefreshIntervalMinutes { get; set; } = 20;

        public string Stcn24ForumSourceType { get; set; } = Stcn24ForumSourceTypes.LatestCreated;
    }
}
