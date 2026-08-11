using System;
using System.Collections.Generic;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.Services;

internal sealed class AirAppScopedSettingsService : IAirAppSettingsService
{
    private readonly ISettingsService _settingsService;

    public AirAppScopedSettingsService(string pluginId, ISettingsService settingsService)
    {
        AirAppId = string.IsNullOrWhiteSpace(pluginId) ? "__unknown__" : pluginId.Trim();
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public string AirAppId { get; }

    public IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId)
    {
        return new ScopedComponentAccessor(this, _settingsService.GetComponentAccessor(componentId, placementId));
    }

    public T LoadComponentSection<T>(string componentId, string? placementId, string sectionId) where T : new()
    {
        return _settingsService.LoadSection<T>(
            AirAppSettingsScope.ComponentInstance,
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
            AirAppSettingsScope.ComponentInstance,
            componentId,
            BuildScopedSectionId(sectionId),
            section,
            placementId,
            changedKeys);
    }

    public void DeleteComponentSection(string componentId, string? placementId, string sectionId)
    {
        _settingsService.DeleteSection(
            AirAppSettingsScope.ComponentInstance,
            componentId,
            BuildScopedSectionId(sectionId),
            placementId);
    }

    private string BuildScopedSectionId(string sectionId)
    {
        var normalizedSectionId = string.IsNullOrWhiteSpace(sectionId) ? "__default__" : sectionId.Trim();
        return $"{AirAppId}:{normalizedSectionId}";
    }

    private sealed class ScopedComponentAccessor : IComponentSettingsAccessor
    {
        private readonly AirAppScopedSettingsService _owner;
        private readonly IComponentSettingsAccessor _inner;

        public ScopedComponentAccessor(AirAppScopedSettingsService owner, IComponentSettingsAccessor inner)
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
            return _inner.GetValue<T>($"{_owner.AirAppId}:{key}");
        }

        public void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null)
        {
            _inner.SetValue($"{_owner.AirAppId}:{key}", value, changedKeys);
        }
    }
}
