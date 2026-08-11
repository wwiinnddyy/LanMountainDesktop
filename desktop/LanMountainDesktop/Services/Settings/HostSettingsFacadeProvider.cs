using System;

namespace LanMountainDesktop.Services.Settings;

internal static class HostSettingsFacadeProvider
{
    private static readonly object Gate = new();
    private static SettingsFacadeService? _instance;

    public static ISettingsFacadeService GetOrCreate()
    {
        lock (Gate)
        {
            _instance ??= new SettingsFacadeService();
            return _instance;
        }
    }

    public static void BindPluginRuntime(PluginRuntimeService pluginRuntimeService)
    {
        ArgumentNullException.ThrowIfNull(pluginRuntimeService);
        lock (Gate)
        {
            _instance ??= new SettingsFacadeService(pluginRuntimeService);
            _instance.BindPluginRuntime(pluginRuntimeService);
        }
    }
}
