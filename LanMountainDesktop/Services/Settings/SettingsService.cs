using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;

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
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop");
        _pluginSettingsPath = Path.Combine(root, "plugin-settings.json");
    }

    public event EventHandler<SettingsChangedEvent>? Changed;

    public T LoadSnapshot<T>(SettingsScope scope, string? subjectId = null, string? placementId = null) where T : new()
    {
        return scope switch
        {
            SettingsScope.App => ConvertSnapshot<AppSettingsSnapshot, T>(_appSettingsService.Load()),
            SettingsScope.Launcher => ConvertSnapshot<LauncherSettingsSnapshot, T>(_launcherSettingsService.Load()),
            SettingsScope.ComponentInstance => LoadComponentSnapshot<T>(subjectId, placementId),
            SettingsScope.Plugin => LoadSection<T>(scope, EnsureKey(subjectId), sectionId: "__snapshot__", placementId),
            _ => new T()
        };
    }

    public void SaveSnapshot<T>(
        SettingsScope scope,
        T snapshot,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        switch (scope)
        {
            case SettingsScope.App:
                _appSettingsService.Save(ConvertSnapshot<T, AppSettingsSnapshot>(snapshot));
                break;
            case SettingsScope.Launcher:
                _launcherSettingsService.Save(ConvertSnapshot<T, LauncherSettingsSnapshot>(snapshot));
                break;
            case SettingsScope.ComponentInstance:
                SaveComponentSnapshot(subjectId, placementId, snapshot);
                break;
            case SettingsScope.Plugin:
                SaveSection(scope, EnsureKey(subjectId), "__snapshot__", snapshot, placementId, changedKeys);
                break;
        }

        OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys));
    }

    public T LoadSection<T>(
        SettingsScope scope,
        string subjectId,
        string sectionId,
        string? placementId = null) where T : new()
    {
        if (scope == SettingsScope.ComponentInstance)
        {
            return _componentMessageStore.LoadSection<T>(EnsureKey(subjectId), placementId, EnsureKey(sectionId));
        }

        if (scope != SettingsScope.Plugin)
        {
            return new T();
        }

        lock (_pluginSettingsGate)
        {
            var document = LoadPluginDocumentLocked();
            if (!document.Sections.TryGetValue(EnsureKey(subjectId), out var pluginSections) ||
                !pluginSections.TryGetValue(EnsureKey(sectionId), out var payload))
            {
                return new T();
            }

            return JsonSerializer.Deserialize<T>(payload.GetRawText(), SerializerOptions) ?? new T();
        }
    }

    public void SaveSection<T>(
        SettingsScope scope,
        string subjectId,
        string sectionId,
        T section,
        string? placementId = null,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        if (scope == SettingsScope.ComponentInstance)
        {
            _componentMessageStore.SaveSection(EnsureKey(subjectId), placementId, EnsureKey(sectionId), section);
            OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys));
            return;
        }

        if (scope != SettingsScope.Plugin)
        {
            return;
        }

        lock (_pluginSettingsGate)
        {
            var document = LoadPluginDocumentLocked();
            var pluginId = EnsureKey(subjectId);
            if (!document.Sections.TryGetValue(pluginId, out var pluginSections))
            {
                pluginSections = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                document.Sections[pluginId] = pluginSections;
            }

            pluginSections[EnsureKey(sectionId)] = JsonSerializer.SerializeToElement(section, SerializerOptions).Clone();
            PersistPluginDocumentLocked(document);
        }

        OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys));
    }

    public void DeleteSection(SettingsScope scope, string subjectId, string sectionId, string? placementId = null)
    {
        if (scope == SettingsScope.ComponentInstance)
        {
            _componentMessageStore.DeleteSection(EnsureKey(subjectId), placementId, EnsureKey(sectionId));
            OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId));
            return;
        }

        if (scope != SettingsScope.Plugin)
        {
            return;
        }

        lock (_pluginSettingsGate)
        {
            var document = LoadPluginDocumentLocked();
            var pluginId = EnsureKey(subjectId);
            if (document.Sections.TryGetValue(pluginId, out var sections) &&
                sections.Remove(EnsureKey(sectionId)))
            {
                if (sections.Count == 0)
                {
                    document.Sections.Remove(pluginId);
                }

                PersistPluginDocumentLocked(document);
            }
        }

        OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId));
    }

    public T? GetValue<T>(
        SettingsScope scope,
        string key,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null)
    {
        var snapshot = scope switch
        {
            SettingsScope.App => JsonSerializer.SerializeToElement(_appSettingsService.Load(), SerializerOptions),
            SettingsScope.Launcher => JsonSerializer.SerializeToElement(_launcherSettingsService.Load(), SerializerOptions),
            SettingsScope.ComponentInstance => JsonSerializer.SerializeToElement(
                LoadSection<Dictionary<string, JsonElement>>(
                    SettingsScope.ComponentInstance,
                    EnsureKey(subjectId),
                    sectionId ?? "__root__",
                    placementId),
                SerializerOptions),
            SettingsScope.Plugin => JsonSerializer.SerializeToElement(
                LoadSection<Dictionary<string, JsonElement>>(SettingsScope.Plugin, EnsureKey(subjectId), sectionId ?? "__root__", placementId),
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
        SettingsScope scope,
        string key,
        T value,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        if (scope == SettingsScope.Plugin)
        {
            var dict = LoadSection<Dictionary<string, JsonElement>>(
                SettingsScope.Plugin,
                EnsureKey(subjectId),
                sectionId ?? "__root__",
                placementId);
            dict[key] = JsonSerializer.SerializeToElement(value, SerializerOptions).Clone();
            SaveSection(SettingsScope.Plugin, EnsureKey(subjectId), sectionId ?? "__root__", dict, placementId, changedKeys ?? [key]);
            return;
        }

        if (scope == SettingsScope.ComponentInstance)
        {
            var effectiveSection = sectionId ?? "__root__";
            var dict = _componentMessageStore.LoadSection<Dictionary<string, JsonElement>>(EnsureKey(subjectId), placementId, effectiveSection);
            dict[key] = JsonSerializer.SerializeToElement(value, SerializerOptions).Clone();
            _componentMessageStore.SaveSection(EnsureKey(subjectId), placementId, effectiveSection, dict);
            OnChanged(new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys ?? [key]));
            return;
        }

        if (scope == SettingsScope.App)
        {
            var snapshot = _appSettingsService.Load();
            var updated = UpdateObjectKey(snapshot, key, value);
            _appSettingsService.Save(updated);
            OnChanged(new SettingsChangedEvent(scope, null, null, sectionId, changedKeys ?? [key]));
            return;
        }

        if (scope == SettingsScope.Launcher)
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

    private PluginSettingsDocument LoadPluginDocumentLocked()
    {
        try
        {
            if (!File.Exists(_pluginSettingsPath))
            {
                return new PluginSettingsDocument();
            }

            var json = File.ReadAllText(_pluginSettingsPath);
            return JsonSerializer.Deserialize<PluginSettingsDocument>(json, SerializerOptions) ?? new PluginSettingsDocument();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SettingsService", $"Failed to load plugin settings '{_pluginSettingsPath}'.", ex);
            return new PluginSettingsDocument();
        }
    }

    private void PersistPluginDocumentLocked(PluginSettingsDocument document)
    {
        try
        {
            var directory = Path.GetDirectoryName(_pluginSettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_pluginSettingsPath, JsonSerializer.Serialize(document, SerializerOptions));
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
            => _settingsService.LoadSnapshot<T>(SettingsScope.ComponentInstance, ComponentId, PlacementId);

        public void SaveSnapshot<T>(T snapshot, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSnapshot(SettingsScope.ComponentInstance, snapshot, ComponentId, PlacementId, changedKeys: changedKeys);

        public T LoadSection<T>(string sectionId) where T : new()
            => _settingsService.LoadSection<T>(SettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public void SaveSection<T>(string sectionId, T section, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSection(SettingsScope.ComponentInstance, ComponentId, sectionId, section, PlacementId, changedKeys);

        public void DeleteSection(string sectionId)
            => _settingsService.DeleteSection(SettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public T? GetValue<T>(string key)
            => _settingsService.GetValue<T>(SettingsScope.ComponentInstance, key, ComponentId, PlacementId);

        public void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SetValue(SettingsScope.ComponentInstance, key, value, ComponentId, PlacementId, changedKeys: changedKeys);
    }

    private sealed class PluginSettingsDocument
    {
        public Dictionary<string, Dictionary<string, JsonElement>> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
