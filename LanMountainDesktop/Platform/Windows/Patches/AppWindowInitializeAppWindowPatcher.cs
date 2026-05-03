using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Windowing;
using LanMountainDesktop.Platform.Windows;
using HarmonyLib;

namespace LanMountainDesktop.Platform.Windows.Patches;

[HarmonyPatch(typeof(FAAppWindow), "InitializeAppWindow")]
internal class AppWindowInitializeAppWindowPatcher
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_PseudoClasses")]
    private static extern IPseudoClasses GetPseudoClasses(StyledElement window);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_IsWindows")]
    private static extern void SetIsWindowsProperty(FAAppWindow window, bool v);

    static void Postfix(FAAppWindow __instance)
    {
        if (!ChromePatchState.UseSystemChrome) return;
        GetPseudoClasses(__instance).Remove(":windows");
        SetIsWindowsProperty(__instance, false);
    }
}
