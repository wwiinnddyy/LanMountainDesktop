using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public interface IComponentInstanceSettingsStore
{
    ComponentSettingsSnapshot Load();

    void Save(ComponentSettingsSnapshot snapshot);

    ComponentSettingsSnapshot LoadForComponent(string componentId, string? placementId);

    void SaveForComponent(string componentId, string? placementId, ComponentSettingsSnapshot snapshot);

    void DeleteForComponent(string componentId, string? placementId);

    T LoadPluginSettings<T>(string componentId, string? placementId) where T : new();

    void SavePluginSettings<T>(string componentId, string? placementId, T settings);

    void DeletePluginSettings(string componentId, string? placementId);
}
