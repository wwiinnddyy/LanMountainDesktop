using System;
using System.Reflection;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed class ComponentSettingsService : IComponentInstanceSettingsStore
{
    private const string LegacySectionId = "__legacy__";
    private readonly ISettingsService? _settingsService;
    private readonly IComponentStateStore? _stateStore;
    private readonly IComponentMessageStore? _messageStore;
    private string _scopedComponentId = string.Empty;
    private string _scopedPlacementId = string.Empty;

    public ComponentSettingsService()
    {
        _settingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;
    }

    public ComponentSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    internal ComponentSettingsService(string settingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            throw new ArgumentException("Settings directory cannot be null or whitespace.", nameof(settingsDirectory));
        }

        var storage = new SqliteComponentDomainStorage(settingsDirectory);
        _stateStore = storage;
        _messageStore = storage;
    }

    public ComponentSettingsSnapshot Load()
    {
        if (HasScopedComponentContext())
        {
            return LoadForComponent(_scopedComponentId, _scopedPlacementId);
        }

        if (_settingsService is not null)
        {
            return _settingsService.LoadSnapshot<ComponentSettingsSnapshot>(
                SettingsScope.ComponentInstance,
                subjectId: string.Empty,
                placementId: null);
        }

        return _stateStore?.LoadState(componentId: string.Empty, placementId: null) ?? new ComponentSettingsSnapshot();
    }

    public void Save(ComponentSettingsSnapshot snapshot)
    {
        if (HasScopedComponentContext())
        {
            SaveForComponent(_scopedComponentId, _scopedPlacementId, snapshot);
            return;
        }

        if (_settingsService is not null)
        {
            _settingsService.SaveSnapshot(
                SettingsScope.ComponentInstance,
                snapshot ?? new ComponentSettingsSnapshot(),
                subjectId: string.Empty,
                placementId: null);
            return;
        }

        _stateStore?.SaveState(componentId: string.Empty, placementId: null, snapshot ?? new ComponentSettingsSnapshot());
    }

    public ComponentSettingsSnapshot LoadForComponent(string componentId, string? placementId)
    {
        if (_settingsService is not null)
        {
            return _settingsService.LoadSnapshot<ComponentSettingsSnapshot>(
                SettingsScope.ComponentInstance,
                subjectId: componentId,
                placementId: placementId);
        }

        return _stateStore?.LoadState(componentId, placementId) ?? new ComponentSettingsSnapshot();
    }

    public void SaveForComponent(string componentId, string? placementId, ComponentSettingsSnapshot snapshot)
    {
        if (_settingsService is not null)
        {
            _settingsService.SaveSnapshot(
                SettingsScope.ComponentInstance,
                snapshot ?? new ComponentSettingsSnapshot(),
                subjectId: componentId,
                placementId: placementId);
            return;
        }

        _stateStore?.SaveState(componentId, placementId, snapshot ?? new ComponentSettingsSnapshot());
    }

    public void DeleteForComponent(string componentId, string? placementId)
    {
        if (_settingsService is not null)
        {
            _settingsService.SaveSnapshot(
                SettingsScope.ComponentInstance,
                new ComponentSettingsSnapshot(),
                subjectId: componentId,
                placementId: placementId);
            _settingsService.DeleteSection(SettingsScope.ComponentInstance, componentId, LegacySectionId, placementId);
            return;
        }

        _stateStore?.DeleteState(componentId, placementId);
    }

    public T LoadPluginSettings<T>(string componentId, string? placementId) where T : new()
    {
        if (_settingsService is not null)
        {
            return _settingsService.LoadSection<T>(
                SettingsScope.ComponentInstance,
                subjectId: componentId,
                sectionId: LegacySectionId,
                placementId: placementId);
        }

        if (_messageStore is SqliteComponentDomainStorage sqliteStorage)
        {
            return sqliteStorage.LoadLegacyMessage<T>(componentId, placementId);
        }

        return new T();
    }

    public void SavePluginSettings<T>(string componentId, string? placementId, T settings)
    {
        if (_settingsService is not null)
        {
            _settingsService.SaveSection(
                SettingsScope.ComponentInstance,
                subjectId: componentId,
                sectionId: LegacySectionId,
                section: settings,
                placementId: placementId);
            return;
        }

        if (_messageStore is SqliteComponentDomainStorage sqliteStorage)
        {
            sqliteStorage.SaveLegacyMessage(componentId, placementId, settings);
        }
    }

    public void DeletePluginSettings(string componentId, string? placementId)
    {
        if (_settingsService is not null)
        {
            _settingsService.DeleteSection(
                SettingsScope.ComponentInstance,
                subjectId: componentId,
                sectionId: LegacySectionId,
                placementId: placementId);
            return;
        }

        if (_messageStore is SqliteComponentDomainStorage sqliteStorage)
        {
            sqliteStorage.DeleteLegacyMessage(componentId, placementId);
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

    internal static void ResetCacheForTests()
    {
        // no-op: SQLite storage is directly persisted without in-memory cache.
    }

    private bool HasScopedComponentContext()
    {
        return !string.IsNullOrWhiteSpace(_scopedComponentId) &&
               !string.IsNullOrWhiteSpace(_scopedPlacementId);
    }
}
