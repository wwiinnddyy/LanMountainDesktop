using System;
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
            var service = HostAppearanceThemeProvider.GetOrCreate();
            var appearance = service.GetCurrent();
            return appearance?.ThemeColorMode ?? ThemeAppearanceValues.ColorModeDefaultNeutral;
        }
        catch
        {
            return ThemeAppearanceValues.ColorModeDefaultNeutral;
        }
    }
}
