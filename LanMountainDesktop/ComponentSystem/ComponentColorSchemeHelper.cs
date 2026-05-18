using System;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views;

namespace LanMountainDesktop.ComponentSystem;

public static class ComponentColorSchemeHelper
{
    public static bool ShouldUseMonetColor(string? componentColorScheme, string globalThemeColorMode)
    {
        if (string.Equals(componentColorScheme, ThemeAppearanceValues.ColorSchemeNative, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(componentColorScheme, ThemeAppearanceValues.ColorSchemeFollowSystem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.Equals(globalThemeColorMode, ThemeAppearanceValues.ColorModeDefaultNeutral, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetCurrentGlobalThemeColorMode()
    {
        try
        {
            var service = HostMaterialColorProvider.GetOrCreate();
            return service.GetMaterialColorSnapshot().ThemeColorMode;
        }
        catch
        {
            return ThemeAppearanceValues.ColorModeDefaultNeutral;
        }
    }

    public static string GetCurrentGlobalThemeColorMode(MaterialColorSnapshot materialColorSnapshot)
    {
        ArgumentNullException.ThrowIfNull(materialColorSnapshot);
        return materialColorSnapshot.ThemeColorMode;
    }
}
