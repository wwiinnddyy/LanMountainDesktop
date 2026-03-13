using System;
using System.Collections.Generic;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

internal sealed class PluginScopedSettingsService : IPluginSettingsService
{
    private readonly ISettingsService _settingsService;

    public PluginScopedSettingsService(string pluginId, ISettingsService settingsService)
    {
        PluginId = string.IsNullOrWhiteSpace(pluginId) ? "__unknown__" : pluginId.Trim();
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public string PluginId { get; }

    public IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId)
    {
        return new ScopedComponentAccessor(this, _settingsService.GetComponentAccessor(componentId, placementId));
    }

    public T LoadComponentSection<T>(string componentId, string? placementId, string sectionId) where T : new()
    {
        return _settingsService.LoadSection<T>(
            SettingsScope.ComponentInstance,
            componentId,
            BuildScopedSectionId(sectionId),
            placementId);
    }

    public void SaveComponentSection<T>(
        string componentId,
        string? placementId,
        string sectionId,
        T section,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        _settingsService.SaveSection(
            SettingsScope.ComponentInstance,
            componentId,
            BuildScopedSectionId(sectionId),
            section,
            placementId,
            changedKeys);
    }

    public void DeleteComponentSection(string componentId, string? placementId, string sectionId)
    {
        _settingsService.DeleteSection(
            SettingsScope.ComponentInstance,
            componentId,
            BuildScopedSectionId(sectionId),
            placementId);
    }

    private string BuildScopedSectionId(string sectionId)
    {
        var normalizedSectionId = string.IsNullOrWhiteSpace(sectionId) ? "__default__" : sectionId.Trim();
        return $"{PluginId}:{normalizedSectionId}";
    }

    private sealed class ScopedComponentAccessor : IComponentSettingsAccessor
    {
        private readonly PluginScopedSettingsService _owner;
        private readonly IComponentSettingsAccessor _inner;

        public ScopedComponentAccessor(PluginScopedSettingsService owner, IComponentSettingsAccessor inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public string ComponentId => _inner.ComponentId;

        public string? PlacementId => _inner.PlacementId;

        public T LoadSnapshot<T>() where T : new()
        {
            return _inner.LoadSnapshot<T>();
        }

        public void SaveSnapshot<T>(T snapshot, IReadOnlyCollection<string>? changedKeys = null)
        {
            _inner.SaveSnapshot(snapshot, changedKeys);
        }

        public T LoadSection<T>(string sectionId) where T : new()
        {
            return _inner.LoadSection<T>(_owner.BuildScopedSectionId(sectionId));
        }

        public void SaveSection<T>(string sectionId, T section, IReadOnlyCollection<string>? changedKeys = null)
        {
            _inner.SaveSection(_owner.BuildScopedSectionId(sectionId), section, changedKeys);
        }

        public void DeleteSection(string sectionId)
        {
            _inner.DeleteSection(_owner.BuildScopedSectionId(sectionId));
        }

        public T? GetValue<T>(string key)
        {
            return _inner.GetValue<T>($"{_owner.PluginId}:{key}");
        }

        public void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null)
        {
            _inner.SetValue($"{_owner.PluginId}:{key}", value, changedKeys);
        }
    }
}
