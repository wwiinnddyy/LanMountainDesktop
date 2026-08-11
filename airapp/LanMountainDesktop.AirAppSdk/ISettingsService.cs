using System.Collections.Generic;

namespace LanMountainDesktop.AirAppSdk;

public interface ISettingsService
{
    event EventHandler<SettingsChangedEvent>? Changed;

    T LoadSnapshot<T>(AirAppSettingsScope scope, string? subjectId = null, string? placementId = null) where T : new();

    void SaveSnapshot<T>(
        AirAppSettingsScope scope,
        T snapshot,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null);

    T LoadSection<T>(
        AirAppSettingsScope scope,
        string subjectId,
        string sectionId,
        string? placementId = null) where T : new();

    void SaveSection<T>(
        AirAppSettingsScope scope,
        string subjectId,
        string sectionId,
        T section,
        string? placementId = null,
        IReadOnlyCollection<string>? changedKeys = null);

    void DeleteSection(
        AirAppSettingsScope scope,
        string subjectId,
        string sectionId,
        string? placementId = null);

    T? GetValue<T>(
        AirAppSettingsScope scope,
        string key,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null);

    void SetValue<T>(
        AirAppSettingsScope scope,
        string key,
        T value,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null);

    IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId);
}
