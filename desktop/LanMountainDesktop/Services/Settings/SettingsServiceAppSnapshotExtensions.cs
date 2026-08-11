using System;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public static class SettingsServiceAppSnapshotExtensions
{
    public static AppSettingsSnapshot Load(this ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        return settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
    }

    public static void Save(this ISettingsService settingsService, AppSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        settingsService.SaveSnapshot(SettingsScope.App, snapshot ?? new AppSettingsSnapshot());
    }
}
