using System.Collections.Generic;

namespace LanMountainDesktop.PluginSdk;

public interface IComponentSettingsAccessor
{
    string ComponentId { get; }

    string? PlacementId { get; }

    T LoadSnapshot<T>() where T : new();

    void SaveSnapshot<T>(T snapshot, IReadOnlyCollection<string>? changedKeys = null);

    T LoadSection<T>(string sectionId) where T : new();

    void SaveSection<T>(string sectionId, T section, IReadOnlyCollection<string>? changedKeys = null);

    void DeleteSection(string sectionId);

    T? GetValue<T>(string key);

    void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null);
}
