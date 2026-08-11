using System.Collections.Generic;

namespace LanMountainDesktop.AirAppSdk;

public interface IAirAppSettingsService
{
    string AirAppId { get; }

    IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId);

    T LoadComponentSection<T>(string componentId, string? placementId, string sectionId) where T : new();

    void SaveComponentSection<T>(
        string componentId,
        string? placementId,
        string sectionId,
        T section,
        IReadOnlyCollection<string>? changedKeys = null);

    void DeleteComponentSection(string componentId, string? placementId, string sectionId);
}
