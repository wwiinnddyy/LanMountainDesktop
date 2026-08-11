using System.Collections.Generic;

namespace LanMountainDesktop.PluginSdk;

public interface ISettingsService
{
    event EventHandler<SettingsChangedEvent>? Changed;

    T LoadSnapshot<T>(SettingsScope scope, string? subjectId = null, string? placementId = null) where T : new();

    void SaveSnapshot<T>(
        SettingsScope scope,
        T snapshot,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null);

    T LoadSection<T>(
        SettingsScope scope,
        string subjectId,
        string sectionId,
        string? placementId = null) where T : new();

    void SaveSection<T>(
        SettingsScope scope,
        string subjectId,
        string sectionId,
        T section,
        string? placementId = null,
        IReadOnlyCollection<string>? changedKeys = null);

    void DeleteSection(
        SettingsScope scope,
        string subjectId,
        string sectionId,
        string? placementId = null);

    T? GetValue<T>(
        SettingsScope scope,
        string key,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null);

    void SetValue<T>(
        SettingsScope scope,
        string key,
        T value,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null);

    IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId);
}
