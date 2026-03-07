using LanMountainDesktop.Services;

namespace LanMountainDesktop.ComponentSystem;

public interface IComponentSettingsStoreAware
{
    void SetComponentSettingsStore(IComponentInstanceSettingsStore settingsStore);
}
