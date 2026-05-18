using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Services.Settings;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

internal sealed class WindowMaterialService : IWindowMaterialService
{
    private const int Windows11Build = 22000;
    private const int Windows11_24H2Build = 26100;

    public bool CanChangeMode => GetAvailableModes().Count > 1;

    public IReadOnlyList<string> GetAvailableModes()
    {
        return GetSupportProfile() switch
        {
            WindowMaterialSupportProfile.FullSwitching =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialMica,
                ThemeAppearanceValues.MaterialAcrylic
            ],
            WindowMaterialSupportProfile.FixedMica =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialMica
            ],
            WindowMaterialSupportProfile.FixedAcrylic =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialAcrylic
            ],
            _ =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone
            ]
        };
    }

    public void Apply(Window window, string materialMode)
    {
        ArgumentNullException.ThrowIfNull(window);

        var normalizedMode = ThemeAppearanceValues.NormalizeSystemMaterialMode(materialMode);
        var supportProfile = GetSupportProfile();
        var effectiveMode = normalizedMode == ThemeAppearanceValues.MaterialAuto
            ? ResolveAutoMaterialMode(supportProfile)
            : normalizedMode;

        if (effectiveMode == ThemeAppearanceValues.MaterialNone)
        {
            window.Background = Brushes.White;
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
            return;
        }

        window.Background = Brushes.Transparent;

        if (supportProfile == WindowMaterialSupportProfile.NoneOnly)
        {
            window.TransparencyLevelHint =
            [
                WindowTransparencyLevel.None
            ];
            return;
        }

        window.TransparencyLevelHint = normalizedMode == ThemeAppearanceValues.MaterialAuto
            ? ResolveAutoTransparencyLevels(supportProfile)
            : effectiveMode switch
        {
            ThemeAppearanceValues.MaterialMica =>
            [
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ],
            ThemeAppearanceValues.MaterialAcrylic =>
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ],
            _ =>
            [
                WindowTransparencyLevel.None
            ]
        };
    }

    private static string ResolveAutoMaterialMode(WindowMaterialSupportProfile supportProfile)
    {
        return supportProfile switch
        {
            WindowMaterialSupportProfile.FullSwitching or WindowMaterialSupportProfile.FixedMica =>
                ThemeAppearanceValues.MaterialMica,
            WindowMaterialSupportProfile.FixedAcrylic =>
                ThemeAppearanceValues.MaterialAcrylic,
            _ => ThemeAppearanceValues.MaterialNone
        };
    }

    private static IReadOnlyList<WindowTransparencyLevel> ResolveAutoTransparencyLevels(WindowMaterialSupportProfile supportProfile)
    {
        return supportProfile switch
        {
            WindowMaterialSupportProfile.FullSwitching or WindowMaterialSupportProfile.FixedMica =>
            [
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ],
            WindowMaterialSupportProfile.FixedAcrylic =>
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ],
            _ =>
            [
                WindowTransparencyLevel.None
            ]
        };
    }

    private static bool IsTransparencyEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            var value = key?.GetValue("EnableTransparency");
            return value switch
            {
                int intValue => intValue != 0,
                byte byteValue => byteValue != 0,
                _ => true
            };
        }
        catch
        {
            return true;
        }
    }

    private static WindowMaterialSupportProfile GetSupportProfile()
    {
        if (!OperatingSystem.IsWindows() || !IsTransparencyEnabled())
        {
            return WindowMaterialSupportProfile.NoneOnly;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, Windows11_24H2Build))
        {
            return WindowMaterialSupportProfile.FullSwitching;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, Windows11Build))
        {
            return WindowMaterialSupportProfile.FixedMica;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0))
        {
            return WindowMaterialSupportProfile.FixedAcrylic;
        }

        return WindowMaterialSupportProfile.NoneOnly;
    }

    private enum WindowMaterialSupportProfile
    {
        NoneOnly = 0,
        FixedMica = 1,
        FixedAcrylic = 2,
        FullSwitching = 3
    }
}
