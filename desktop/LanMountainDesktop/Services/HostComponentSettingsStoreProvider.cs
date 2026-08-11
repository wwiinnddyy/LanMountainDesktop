using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

internal static class HostComponentSettingsStoreProvider
{
    private static readonly IComponentInstanceSettingsStore Instance =
        new ComponentSettingsService(HostSettingsFacadeProvider.GetOrCreate().Settings);

    public static IComponentInstanceSettingsStore GetOrCreate()
    {
        return Instance;
    }
}
