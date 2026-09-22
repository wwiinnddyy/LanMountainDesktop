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
}
