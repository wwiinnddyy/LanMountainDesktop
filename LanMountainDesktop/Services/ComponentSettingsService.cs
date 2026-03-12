using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class ComponentSettingsService : IComponentInstanceSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly object CacheGate = new();
    private static readonly TimeSpan CacheProbeInterval = TimeSpan.FromMilliseconds(400);

    private static string? _cachedPath;
    private static ComponentSettingsDocumentSnapshot? _cachedSnapshot;
    private static DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private static DateTime _lastProbeUtc = DateTime.MinValue;

    private readonly string _settingsPath;
    private readonly string _legacyAppSettingsPath;
    private string _scopedComponentId = string.Empty;
    private string _scopedPlacementId = string.Empty;

    public ComponentSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop"))
    {
    }

    internal ComponentSettingsService(string settingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            throw new ArgumentException("Settings directory cannot be null or whitespace.", nameof(settingsDirectory));
        }

        _settingsPath = Path.Combine(settingsDirectory, "component-settings.json");
        _legacyAppSettingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public ComponentSettingsSnapshot Load()
    {
        if (HasScopedComponentContext())
        {
            return LoadForComponent(_scopedComponentId, _scopedPlacementId);
        }

        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                return document.DefaultSettings.Clone();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ComponentSettings", $"Failed to load component settings from '{_settingsPath}'.", ex);
            return new ComponentSettingsSnapshot();
        }
    }

    public void Save(ComponentSettingsSnapshot snapshot)
    {
        if (HasScopedComponentContext())
        {
            SaveForComponent(_scopedComponentId, _scopedPlacementId, snapshot);
            return;
        }

        var snapshotToPersist = NormalizeSnapshot(snapshot);

        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                document.DefaultSettings = snapshotToPersist;
                PersistDocumentLocked(document);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ComponentSettings", $"Failed to save default component settings to '{_settingsPath}'.", ex);
        }
    }

    public ComponentSettingsSnapshot LoadForComponent(string componentId, string? placementId)
    {
        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                var instanceKey = BuildInstanceKey(componentId, placementId);
                if (!string.IsNullOrWhiteSpace(instanceKey) &&
                    document.InstanceSettings.TryGetValue(instanceKey, out var snapshot))
                {
                    return snapshot.Clone();
                }

                return document.DefaultSettings.Clone();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "ComponentSettings",
                $"Failed to load component settings. ComponentId={componentId}; PlacementId={placementId}; Path={_settingsPath}",
                ex);
            return new ComponentSettingsSnapshot();
        }
    }

    public void SaveForComponent(string componentId, string? placementId, ComponentSettingsSnapshot snapshot)
    {
        var normalizedSnapshot = NormalizeSnapshot(snapshot);
        var instanceKey = BuildInstanceKey(componentId, placementId);
        if (string.IsNullOrWhiteSpace(instanceKey))
        {
            Save(normalizedSnapshot);
            return;
        }

        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                document.InstanceSettings[instanceKey] = normalizedSnapshot;
                PersistDocumentLocked(document);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "ComponentSettings",
                $"Failed to save component settings. ComponentId={componentId}; PlacementId={placementId}; Path={_settingsPath}",
                ex);
        }
    }

    public void DeleteForComponent(string componentId, string? placementId)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        if (string.IsNullOrWhiteSpace(instanceKey))
        {
            return;
        }

        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                var changed = document.InstanceSettings.Remove(instanceKey);
                changed |= document.PluginSettings.Remove(instanceKey);
                if (changed)
                {
                    PersistDocumentLocked(document);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "ComponentSettings",
                $"Failed to delete component settings. ComponentId={componentId}; PlacementId={placementId}; Path={_settingsPath}",
                ex);
        }
    }

    public T LoadPluginSettings<T>(string componentId, string? placementId) where T : new()
    {
        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                var instanceKey = BuildInstanceKey(componentId, placementId);
                if (string.IsNullOrWhiteSpace(instanceKey) ||
                    !document.PluginSettings.TryGetValue(instanceKey, out var settingsElement))
                {
                    return new T();
                }

                return JsonSerializer.Deserialize<T>(settingsElement.GetRawText(), SerializerOptions) ?? new T();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "ComponentSettings",
                $"Failed to load plugin settings. ComponentId={componentId}; PlacementId={placementId}; Path={_settingsPath}",
                ex);
            return new T();
        }
    }

    public void SavePluginSettings<T>(string componentId, string? placementId, T settings)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        if (string.IsNullOrWhiteSpace(instanceKey))
        {
            return;
        }

        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                document.PluginSettings[instanceKey] = JsonSerializer.SerializeToElement(settings, SerializerOptions).Clone();
                PersistDocumentLocked(document);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "ComponentSettings",
                $"Failed to save plugin settings. ComponentId={componentId}; PlacementId={placementId}; Path={_settingsPath}",
                ex);
        }
    }

    public void DeletePluginSettings(string componentId, string? placementId)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        if (string.IsNullOrWhiteSpace(instanceKey))
        {
            return;
        }

        try
        {
            lock (CacheGate)
            {
                var document = LoadDocumentLocked();
                if (document.PluginSettings.Remove(instanceKey))
                {
                    PersistDocumentLocked(document);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "ComponentSettings",
                $"Failed to delete plugin settings. ComponentId={componentId}; PlacementId={placementId}; Path={_settingsPath}",
                ex);
        }
    }

    public void SetScopedComponentContext(string componentId, string? placementId)
    {
        _scopedComponentId = componentId?.Trim() ?? string.Empty;
        _scopedPlacementId = placementId?.Trim() ?? string.Empty;
    }

    public void ClearScopedComponentContext()
    {
        _scopedComponentId = string.Empty;
        _scopedPlacementId = string.Empty;
    }

    public static void ApplyScopedContextToTarget(object? target, string componentId, string? placementId)
    {
        if (target is null)
        {
            return;
        }

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in target.GetType().GetFields(flags))
        {
            if (field.FieldType != typeof(ComponentSettingsService))
            {
                continue;
            }

            if (field.GetValue(target) is ComponentSettingsService settingsService)
            {
                settingsService.SetScopedComponentContext(componentId, placementId);
            }
        }

        foreach (var property in target.GetType().GetProperties(flags))
        {
            if (property.PropertyType != typeof(ComponentSettingsService) ||
                !property.CanRead)
            {
                continue;
            }

            if (property.GetValue(target) is ComponentSettingsService settingsService)
            {
                settingsService.SetScopedComponentContext(componentId, placementId);
            }
        }
    }

    private bool TryGetCachedWithoutProbe(DateTime nowUtc, out ComponentSettingsDocumentSnapshot snapshot)
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

    private bool TryGetCachedAfterProbe(DateTime writeTimeUtc, out ComponentSettingsDocumentSnapshot snapshot)
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

    private ComponentSettingsDocumentSnapshot LoadDocumentLocked()
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

        ComponentSettingsDocumentSnapshot loadedSnapshot;
        var loadDetails = ComponentSettingsLoadDetails.Empty;
        if (hasFile)
        {
            loadDetails = LoadSnapshotFromDisk();
            loadedSnapshot = loadDetails.Snapshot;
        }
        else if (TryLoadLegacySnapshot(out var migratedSnapshot))
        {
            loadedSnapshot = new ComponentSettingsDocumentSnapshot
            {
                DefaultSettings = NormalizeSnapshot(migratedSnapshot)
            };
            loadDetails = new ComponentSettingsLoadDetails(
                loadedSnapshot,
                ComponentSettingsDocumentFormat.LegacySnapshot,
                true);
        }
        else
        {
            loadedSnapshot = new ComponentSettingsDocumentSnapshot();
        }

        var normalizedSnapshot = NormalizeDocument(loadedSnapshot);
        if (loadDetails.ShouldRewriteToCanonical)
        {
            writeTimeUtc = PersistSnapshotToDisk(normalizedSnapshot);
        }

        LogLoadDetails(loadDetails.Format, loadDetails.ShouldRewriteToCanonical, normalizedSnapshot);
        UpdateCache(normalizedSnapshot, writeTimeUtc, nowUtc);
        return normalizedSnapshot.Clone();
    }

    private ComponentSettingsLoadDetails LoadSnapshotFromDisk()
    {
        try
        {
            var json = File.ReadAllText(_settingsPath);
            using var document = JsonDocument.Parse(json);
            if (TryGetDocumentFormat(document.RootElement, out var format))
            {
                var snapshot = JsonSerializer.Deserialize<ComponentSettingsDocumentSnapshot>(json, SerializerOptions);
                return new ComponentSettingsLoadDetails(
                    snapshot ?? new ComponentSettingsDocumentSnapshot(),
                    format,
                    format == ComponentSettingsDocumentFormat.PascalCaseDocument);
            }

            var legacySnapshot = JsonSerializer.Deserialize<ComponentSettingsSnapshot>(json, SerializerOptions);
            return new ComponentSettingsLoadDetails(
                new ComponentSettingsDocumentSnapshot
                {
                    DefaultSettings = NormalizeSnapshot(legacySnapshot)
                },
                ComponentSettingsDocumentFormat.LegacySnapshot,
                true);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ComponentSettings", $"Failed to deserialize component settings from '{_settingsPath}'.", ex);
            return ComponentSettingsLoadDetails.Empty;
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
                IfengNewsAutoRefreshEnabled = legacy.IfengNewsAutoRefreshEnabled,
                IfengNewsAutoRefreshIntervalMinutes = legacy.IfengNewsAutoRefreshIntervalMinutes,
                IfengNewsChannelType = legacy.IfengNewsChannelType,
                DailyWordAutoRefreshEnabled = legacy.DailyWordAutoRefreshEnabled,
                DailyWordAutoRefreshIntervalMinutes = legacy.DailyWordAutoRefreshIntervalMinutes,
                BilibiliHotSearchAutoRefreshEnabled = legacy.BilibiliHotSearchAutoRefreshEnabled,
                BilibiliHotSearchAutoRefreshIntervalMinutes = legacy.BilibiliHotSearchAutoRefreshIntervalMinutes,
                BaiduHotSearchAutoRefreshEnabled = legacy.BaiduHotSearchAutoRefreshEnabled,
                BaiduHotSearchAutoRefreshIntervalMinutes = legacy.BaiduHotSearchAutoRefreshIntervalMinutes,
                BaiduHotSearchSourceType = legacy.BaiduHotSearchSourceType,
                WeatherAutoRefreshEnabled = legacy.WeatherAutoRefreshEnabled,
                WeatherAutoRefreshIntervalMinutes = legacy.WeatherAutoRefreshIntervalMinutes,
                Stcn24ForumAutoRefreshEnabled = legacy.Stcn24ForumAutoRefreshEnabled,
                Stcn24ForumAutoRefreshIntervalMinutes = legacy.Stcn24ForumAutoRefreshIntervalMinutes,
                Stcn24ForumSourceType = legacy.Stcn24ForumSourceType
            };

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ComponentSettings", $"Failed to migrate legacy component settings from '{_legacyAppSettingsPath}'.", ex);
            return false;
        }
    }

    private void PersistDocumentLocked(ComponentSettingsDocumentSnapshot snapshot)
    {
        var writeTimeUtc = PersistSnapshotToDisk(snapshot);
        UpdateCache(snapshot, writeTimeUtc, DateTime.UtcNow);
    }

    private DateTime PersistSnapshotToDisk(ComponentSettingsDocumentSnapshot snapshot)
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
        normalized.IfengNewsAutoRefreshIntervalMinutes = NormalizeIfengNewsInterval(normalized.IfengNewsAutoRefreshIntervalMinutes);
        normalized.IfengNewsChannelType = IfengNewsChannelTypes.Normalize(normalized.IfengNewsChannelType);
        normalized.DailyWordAutoRefreshIntervalMinutes = NormalizeDailyWordInterval(normalized.DailyWordAutoRefreshIntervalMinutes);
        normalized.BilibiliHotSearchAutoRefreshIntervalMinutes = NormalizeBilibiliHotSearchInterval(
            normalized.BilibiliHotSearchAutoRefreshIntervalMinutes);
        normalized.BaiduHotSearchAutoRefreshIntervalMinutes = NormalizeBaiduHotSearchInterval(
            normalized.BaiduHotSearchAutoRefreshIntervalMinutes);
        normalized.BaiduHotSearchSourceType = BaiduHotSearchSourceTypes.Normalize(normalized.BaiduHotSearchSourceType);
        normalized.WeatherAutoRefreshIntervalMinutes = NormalizeWeatherInterval(normalized.WeatherAutoRefreshIntervalMinutes);
        normalized.Stcn24ForumAutoRefreshIntervalMinutes = NormalizeStcn24ForumInterval(normalized.Stcn24ForumAutoRefreshIntervalMinutes);
        normalized.Stcn24ForumSourceType = Stcn24ForumSourceTypes.Normalize(normalized.Stcn24ForumSourceType);

        return normalized;
    }

    private static ComponentSettingsDocumentSnapshot NormalizeDocument(ComponentSettingsDocumentSnapshot? snapshot)
    {
        var normalized = snapshot?.Clone() ?? new ComponentSettingsDocumentSnapshot();
        normalized.DefaultSettings = NormalizeSnapshot(normalized.DefaultSettings);

        var instanceSettings = new Dictionary<string, ComponentSettingsSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in normalized.InstanceSettings)
        {
            var key = NormalizeInstanceKey(pair.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            instanceSettings[key] = NormalizeSnapshot(pair.Value);
        }

        var pluginSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in normalized.PluginSettings)
        {
            var key = NormalizeInstanceKey(pair.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            pluginSettings[key] = pair.Value.Clone();
        }

        normalized.InstanceSettings = instanceSettings;
        normalized.PluginSettings = pluginSettings;
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

    private static int NormalizeIfengNewsInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 20);
    }

    private static int NormalizeBilibiliHotSearchInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 15);
    }

    private static int NormalizeBaiduHotSearchInterval(int minutes)
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

    private static string BuildInstanceKey(string componentId, string? placementId)
    {
        var normalizedComponentId = componentId?.Trim() ?? string.Empty;
        var normalizedPlacementId = placementId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedComponentId) || string.IsNullOrWhiteSpace(normalizedPlacementId))
        {
            return string.Empty;
        }

        return $"{normalizedComponentId}::{normalizedPlacementId}";
    }

    private static string NormalizeInstanceKey(string? key)
    {
        return key?.Trim() ?? string.Empty;
    }

    private bool HasScopedComponentContext()
    {
        return !string.IsNullOrWhiteSpace(_scopedComponentId) &&
               !string.IsNullOrWhiteSpace(_scopedPlacementId);
    }

    private void UpdateCache(ComponentSettingsDocumentSnapshot snapshot, DateTime writeTimeUtc, DateTime probeTimeUtc)
    {
        _cachedPath = _settingsPath;
        _cachedSnapshot = snapshot.Clone();
        _cachedWriteTimeUtc = writeTimeUtc;
        _lastProbeUtc = probeTimeUtc;
    }

    internal static void ResetCacheForTests()
    {
        lock (CacheGate)
        {
            _cachedPath = null;
            _cachedSnapshot = null;
            _cachedWriteTimeUtc = DateTime.MinValue;
            _lastProbeUtc = DateTime.MinValue;
        }
    }

    private void LogLoadDetails(
        ComponentSettingsDocumentFormat format,
        bool rewroteToCanonical,
        ComponentSettingsDocumentSnapshot snapshot)
    {
        AppLogger.Info(
            "ComponentSettings",
            $"Loaded component settings document. Format={format}; RewroteToCanonical={rewroteToCanonical}; " +
            $"InstanceSettings={snapshot.InstanceSettings.Count}; PluginSettings={snapshot.PluginSettings.Count}; Path={_settingsPath}");
    }

    private static bool TryGetDocumentFormat(
        JsonElement rootElement,
        out ComponentSettingsDocumentFormat format)
    {
        format = ComponentSettingsDocumentFormat.EmptyDocument;
        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var hasDocumentProperties = false;
        var requiresCanonicalRewrite = false;
        foreach (var property in rootElement.EnumerateObject())
        {
            if (!IsDocumentPropertyName(property.Name))
            {
                continue;
            }

            hasDocumentProperties = true;
            if (!IsCanonicalDocumentPropertyName(property.Name))
            {
                requiresCanonicalRewrite = true;
            }
        }

        if (!hasDocumentProperties)
        {
            return false;
        }

        format = requiresCanonicalRewrite
            ? ComponentSettingsDocumentFormat.PascalCaseDocument
            : ComponentSettingsDocumentFormat.CanonicalDocument;
        return true;
    }

    private static bool IsDocumentPropertyName(string propertyName)
    {
        return string.Equals(propertyName, "defaultSettings", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(propertyName, "instanceSettings", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(propertyName, "pluginSettings", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCanonicalDocumentPropertyName(string propertyName)
    {
        return string.Equals(propertyName, "defaultSettings", StringComparison.Ordinal) ||
               string.Equals(propertyName, "instanceSettings", StringComparison.Ordinal) ||
               string.Equals(propertyName, "pluginSettings", StringComparison.Ordinal);
    }

    private sealed class ComponentSettingsDocumentSnapshot
    {
        public ComponentSettingsSnapshot DefaultSettings { get; set; } = new();

        public Dictionary<string, ComponentSettingsSnapshot> InstanceSettings { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, JsonElement> PluginSettings { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public ComponentSettingsDocumentSnapshot Clone()
        {
            var clone = new ComponentSettingsDocumentSnapshot
            {
                DefaultSettings = DefaultSettings?.Clone() ?? new ComponentSettingsSnapshot(),
                InstanceSettings = new Dictionary<string, ComponentSettingsSnapshot>(StringComparer.OrdinalIgnoreCase),
                PluginSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var pair in InstanceSettings)
            {
                clone.InstanceSettings[pair.Key] = pair.Value?.Clone() ?? new ComponentSettingsSnapshot();
            }

            foreach (var pair in PluginSettings)
            {
                clone.PluginSettings[pair.Key] = pair.Value.Clone();
            }

            return clone;
        }
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

        public bool IfengNewsAutoRefreshEnabled { get; set; } = true;

        public int IfengNewsAutoRefreshIntervalMinutes { get; set; } = 20;

        public string IfengNewsChannelType { get; set; } = IfengNewsChannelTypes.Comprehensive;

        public bool DailyWordAutoRefreshEnabled { get; set; } = true;

        public int DailyWordAutoRefreshIntervalMinutes { get; set; } = 360;

        public bool BilibiliHotSearchAutoRefreshEnabled { get; set; } = true;

        public int BilibiliHotSearchAutoRefreshIntervalMinutes { get; set; } = 15;

        public bool BaiduHotSearchAutoRefreshEnabled { get; set; } = true;

        public int BaiduHotSearchAutoRefreshIntervalMinutes { get; set; } = 15;

        public string BaiduHotSearchSourceType { get; set; } = BaiduHotSearchSourceTypes.Official;

        public bool WeatherAutoRefreshEnabled { get; set; } = true;

        public int WeatherAutoRefreshIntervalMinutes { get; set; } = 12;

        public bool Stcn24ForumAutoRefreshEnabled { get; set; } = true;

        public int Stcn24ForumAutoRefreshIntervalMinutes { get; set; } = 20;

        public string Stcn24ForumSourceType { get; set; } = Stcn24ForumSourceTypes.LatestCreated;
    }

    private readonly record struct ComponentSettingsLoadDetails(
        ComponentSettingsDocumentSnapshot Snapshot,
        ComponentSettingsDocumentFormat Format,
        bool ShouldRewriteToCanonical)
    {
        public static ComponentSettingsLoadDetails Empty { get; } = new(
            new ComponentSettingsDocumentSnapshot(),
            ComponentSettingsDocumentFormat.EmptyDocument,
            false);
    }

    private enum ComponentSettingsDocumentFormat
    {
        EmptyDocument,
        CanonicalDocument,
        PascalCaseDocument,
        LegacySnapshot
    }
}
