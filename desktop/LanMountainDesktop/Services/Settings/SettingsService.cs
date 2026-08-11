using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.Services.Settings;

internal sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppSettingsService _appSettingsService = new();
    private readonly LauncherSettingsService _launcherSettingsService = new();
    private readonly IComponentStateStore _componentStateStore = ComponentDomainStorageProvider.Instance;
    private readonly IComponentMessageStore _componentMessageStore = ComponentDomainStorageProvider.Instance;
    private readonly string _pluginSettingsPath;
    private readonly object _pluginSettingsGate = new();

    public SettingsService()
    {
        var root = AppDataPathProvider.GetDataRoot();
        _pluginSettingsPath = Path.Combine(root, "plugin-settings.json");
    }

    public event EventHandler<SettingsChangedEvent>? Changed;

    public T LoadSnapshot<T>(AirAppSettingsScope scope, string? subjectId = null, string? placementId = null) where T : new()
    {
        return scope switch
        {
            AirAppSettingsScope.App => ConvertSnapshot<AppSettingsSnapshot, T>(_appSettingsService.Load()),
            AirAppSettingsScope.Launcher => ConvertSnapshot<LauncherSettingsSnapshot, T>(_launcherSettingsService.Load()),
            AirAppSettingsScope.ComponentInstance => LoadComponentSnapshot<T>(subjectId, placementId),
            AirAppSettingsScope.AirApp => LoadSection<T>(scope, EnsureKey(subjectId), sectionId: "__snapshot__", placementId),
            _ => new T()
        };
    }

    public void SaveSnapshot<T>(
        AirAppSettingsScope scope,
        T snapshot,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        switch (scope)
        {
            case AirAppSettingsScope.App:
                _appSettingsService.Save(ConvertSnapshot<T, AppSettingsSnapshot>(snapshot));
                break;
            case AirAppSettingsScope.Launcher:
                _launcherSettingsService.Save(ConvertSnapshot<T, LauncherSettingsSnapshot>(snapshot));
                break;
            case AirAppSettingsScope.ComponentInstance:
                SaveComponentSnapshot(subjectId, placementId, snapshot);
                break;
            case AirAppSettingsScope.AirApp:
                SaveSection(scope, EnsureKey(subjectId), "__snapshot__", snapshot, placementId, changedKeys);
                break;
        }

        OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys));
    }

    public T LoadSection<T>(
        AirAppSettingsScope scope,
        string subjectId,
        string sectionId,
        string? placementId = null) where T : new()
    {
        if (scope == AirAppSettingsScope.ComponentInstance)
        {
            return _componentMessageStore.LoadSection<T>(EnsureKey(subjectId), placementId, EnsureKey(sectionId));
        }

        if (scope != AirAppSettingsScope.AirApp)
        {
            return new T();
        }

        lock (_pluginSettingsGate)
        {
            var document = LoadAirAppDocumentLocked();
            if (!document.Sections.TryGetValue(EnsureKey(subjectId), out var pluginSections) ||
                !pluginSections.TryGetValue(EnsureKey(sectionId), out var payload))
            {
                return new T();
            }

            return JsonSerializer.Deserialize<T>(payload.GetRawText(), SerializerOptions) ?? new T();
        }
    }

    public void SaveSection<T>(
        AirAppSettingsScope scope,
        string subjectId,
        string sectionId,
        T section,
        string? placementId = null,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        if (scope == AirAppSettingsScope.ComponentInstance)
        {
            _componentMessageStore.SaveSection(EnsureKey(subjectId), placementId, EnsureKey(sectionId), section);
            OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys));
            return;
        }

        if (scope != AirAppSettingsScope.AirApp)
        {
            return;
        }

        lock (_pluginSettingsGate)
        {
            var document = LoadAirAppDocumentLocked();
            var pluginId = EnsureKey(subjectId);
            if (!document.Sections.TryGetValue(pluginId, out var pluginSections))
            {
                pluginSections = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                document.Sections[pluginId] = pluginSections;
            }

            pluginSections[EnsureKey(sectionId)] = JsonSerializer.SerializeToElement(section, SerializerOptions).Clone();
            PersistAirAppDocumentLocked(document);
        }

        OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys));
    }

    public void DeleteSection(AirAppSettingsScope scope, string subjectId, string sectionId, string? placementId = null)
    {
        if (scope == AirAppSettingsScope.ComponentInstance)
        {
            _componentMessageStore.DeleteSection(EnsureKey(subjectId), placementId, EnsureKey(sectionId));
            OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId));
            return;
        }

        if (scope != AirAppSettingsScope.AirApp)
        {
            return;
        }

        lock (_pluginSettingsGate)
        {
            var document = LoadAirAppDocumentLocked();
            var pluginId = EnsureKey(subjectId);
            if (document.Sections.TryGetValue(pluginId, out var sections) &&
                sections.Remove(EnsureKey(sectionId)))
            {
                if (sections.Count == 0)
                {
                    document.Sections.Remove(pluginId);
                }

                PersistAirAppDocumentLocked(document);
            }
        }

        OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId));
    }

    public T? GetValue<T>(
        AirAppSettingsScope scope,
        string key,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null)
    {
        var snapshot = scope switch
        {
            AirAppSettingsScope.App => JsonSerializer.SerializeToElement(_appSettingsService.Load(), SerializerOptions),
            AirAppSettingsScope.Launcher => JsonSerializer.SerializeToElement(_launcherSettingsService.Load(), SerializerOptions),
            AirAppSettingsScope.ComponentInstance => JsonSerializer.SerializeToElement(
                LoadSection<Dictionary<string, JsonElement>>(
                    AirAppSettingsScope.ComponentInstance,
                    EnsureKey(subjectId),
                    sectionId ?? "__root__",
                    placementId),
                SerializerOptions),
            AirAppSettingsScope.AirApp => JsonSerializer.SerializeToElement(
                LoadSection<Dictionary<string, JsonElement>>(AirAppSettingsScope.AirApp, EnsureKey(subjectId), sectionId ?? "__root__", placementId),
                SerializerOptions),
            _ => default
        };

        if (snapshot.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        foreach (var property in snapshot.EnumerateObject())
        {
            if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                return property.Value.Deserialize<T>(SerializerOptions);
            }
            catch
            {
                return default;
            }
        }

        return default;
    }

    public void SetValue<T>(
        AirAppSettingsScope scope,
        string key,
        T value,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        if (scope == AirAppSettingsScope.AirApp)
        {
            var dict = LoadSection<Dictionary<string, JsonElement>>(
                AirAppSettingsScope.AirApp,
                EnsureKey(subjectId),
                sectionId ?? "__root__",
                placementId);
            dict[key] = JsonSerializer.SerializeToElement(value, SerializerOptions).Clone();
            SaveSection(AirAppSettingsScope.AirApp, EnsureKey(subjectId), sectionId ?? "__root__", dict, placementId, changedKeys ?? [key]);
            return;
        }

        if (scope == AirAppSettingsScope.ComponentInstance)
        {
            var effectiveSection = sectionId ?? "__root__";
            var dict = _componentMessageStore.LoadSection<Dictionary<string, JsonElement>>(EnsureKey(subjectId), placementId, effectiveSection);
            dict[key] = JsonSerializer.SerializeToElement(value, SerializerOptions).Clone();
            _componentMessageStore.SaveSection(EnsureKey(subjectId), placementId, effectiveSection, dict);
            OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys ?? [key]));
            return;
        }

        if (scope == AirAppSettingsScope.App)
        {
            var snapshot = _appSettingsService.Load();
            var updated = UpdateObjectKey(snapshot, key, value);
            _appSettingsService.Save(updated);
            OnChanged(new SettingsChangedEvent(scope, null, null, sectionId, changedKeys ?? [key]));
            return;
        }

        if (scope == AirAppSettingsScope.Launcher)
        {
            var snapshot = _launcherSettingsService.Load();
            var updated = UpdateObjectKey(snapshot, key, value);
            _launcherSettingsService.Save(updated);
            OnChanged(new SettingsChangedEvent(scope, null, null, sectionId, changedKeys ?? [key]));
        }
    }

    public IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId)
    {
        return new ComponentSettingsAccessor(this, componentId, placementId);
    }

    private T LoadComponentSnapshot<T>(string? componentId, string? placementId) where T : new()
    {
        var snapshot = _componentStateStore.LoadState(EnsureKey(componentId), placementId);
        return ConvertSnapshot<ComponentSettingsSnapshot, T>(snapshot);
    }

    private void SaveComponentSnapshot<T>(string? componentId, string? placementId, T snapshot)
    {
        var converted = ConvertSnapshot<T, ComponentSettingsSnapshot>(snapshot);
        _componentStateStore.SaveState(EnsureKey(componentId), placementId, converted);
    }

    private static TOut ConvertSnapshot<TIn, TOut>(TIn source) where TOut : new()
    {
        if (source is null)
        {
            return new TOut();
        }

        if (source is TOut direct)
        {
            return direct;
        }

        try
        {
            var json = JsonSerializer.Serialize(source, SerializerOptions);
            return JsonSerializer.Deserialize<TOut>(json, SerializerOptions) ?? new TOut();
        }
        catch
        {
            return new TOut();
        }
    }

    private static TSnapshot UpdateObjectKey<TSnapshot, TValue>(TSnapshot snapshot, string key, TValue value)
        where TSnapshot : new()
    {
        var bag = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(snapshot, SerializerOptions),
            SerializerOptions) ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        var actualKey = bag.Keys.FirstOrDefault(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)) ?? key;
        bag[actualKey] = JsonSerializer.SerializeToElement(value, SerializerOptions).Clone();

        try
        {
            var json = JsonSerializer.Serialize(bag, SerializerOptions);
            return JsonSerializer.Deserialize<TSnapshot>(json, SerializerOptions) ?? new TSnapshot();
        }
        catch
        {
            return snapshot is null ? new TSnapshot() : snapshot;
        }
    }

    private AirAppSettingsDocument LoadAirAppDocumentLocked()
    {
        try
        {
            if (!File.Exists(_pluginSettingsPath))
            {
                return new AirAppSettingsDocument();
            }

            var json = File.ReadAllText(_pluginSettingsPath);
            return JsonSerializer.Deserialize<AirAppSettingsDocument>(json, SerializerOptions) ?? new AirAppSettingsDocument();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SettingsService", $"Failed to load plugin settings '{_pluginSettingsPath}'.", ex);
            return new AirAppSettingsDocument();
        }
    }

    private void PersistAirAppDocumentLocked(AirAppSettingsDocument document)
    {
        try
        {
            var directory = Path.GetDirectoryName(_pluginSettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{_pluginSettingsPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, SerializerOptions));
            File.Move(tempPath, _pluginSettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SettingsService", $"Failed to persist plugin settings '{_pluginSettingsPath}'.", ex);
        }
    }

    private static string EnsureKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "__default__" : value.Trim();
    }

    private void OnChanged(SettingsChangedEvent e)
    {
        try
        {
            Changed?.Invoke(this, e);
        }
        catch
        {
            // Never let a subscriber break settings persistence.
        }
    }

    private sealed class ComponentSettingsAccessor : IComponentSettingsAccessor
    {
        private readonly SettingsService _settingsService;

        public ComponentSettingsAccessor(SettingsService settingsService, string componentId, string? placementId)
        {
            _settingsService = settingsService;
            ComponentId = componentId;
            PlacementId = placementId;
        }

        public string ComponentId { get; }

        public string? PlacementId { get; }

        public T LoadSnapshot<T>() where T : new()
            => _settingsService.LoadSnapshot<T>(AirAppSettingsScope.ComponentInstance, ComponentId, PlacementId);

        public void SaveSnapshot<T>(T snapshot, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSnapshot(AirAppSettingsScope.ComponentInstance, snapshot, ComponentId, PlacementId, changedKeys: changedKeys);

        public T LoadSection<T>(string sectionId) where T : new()
            => _settingsService.LoadSection<T>(AirAppSettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public void SaveSection<T>(string sectionId, T section, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSection(AirAppSettingsScope.ComponentInstance, ComponentId, sectionId, section, PlacementId, changedKeys);

        public void DeleteSection(string sectionId)
            => _settingsService.DeleteSection(AirAppSettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public T? GetValue<T>(string key)
            => _settingsService.GetValue<T>(AirAppSettingsScope.ComponentInstance, key, ComponentId, PlacementId);

        public void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SetValue(AirAppSettingsScope.ComponentInstance, key, value, ComponentId, PlacementId, changedKeys: changedKeys);
    }

    private sealed class AirAppSettingsDocument
    {
        public Dictionary<string, Dictionary<string, JsonElement>> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
