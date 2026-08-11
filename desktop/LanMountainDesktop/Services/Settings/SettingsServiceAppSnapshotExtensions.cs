using System;
using LanMountainDesktop.Models;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.Services;

public static class SettingsServiceAppSnapshotExtensions
{
    public static AppSettingsSnapshot Load(this ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        return settingsService.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
    }

    public static void Save(this ISettingsService settingsService, AppSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        settingsService.SaveSnapshot(AirAppSettingsScope.App, snapshot ?? new AppSettingsSnapshot());
    }
}
