using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public interface IComponentInstanceSettingsStore
{
    ComponentSettingsSnapshot Load();

    void Save(ComponentSettingsSnapshot snapshot);

    ComponentSettingsSnapshot LoadForComponent(string componentId, string? placementId);

    void SaveForComponent(string componentId, string? placementId, ComponentSettingsSnapshot snapshot);

    void DeleteForComponent(string componentId, string? placementId);

    T LoadAirAppSettings<T>(string componentId, string? placementId) where T : new();

    void SaveAirAppSettings<T>(string componentId, string? placementId, T settings);

    void DeleteAirAppSettings(string componentId, string? placementId);
}
