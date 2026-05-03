using FluentAvalonia.UI.Windowing;
using HarmonyLib;
using LanMountainDesktop.Platform.Windows;

namespace LanMountainDesktop.Platform.Windows.Patches;

[HarmonyPatch]
internal class Win32WindowManagerConstructorPatcher
{
    [HarmonyTargetMethod]
    static System.Reflection.MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("FluentAvalonia.UI.Windowing.Win32WindowManager");
        return AccessTools.Constructor(type!, [typeof(FAAppWindow)]);
    }

    static bool Prefix(FAAppWindow window)
    {
        return !ChromePatchState.UseSystemChrome;
    }
}
