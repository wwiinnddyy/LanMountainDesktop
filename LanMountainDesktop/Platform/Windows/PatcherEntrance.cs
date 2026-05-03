using HarmonyLib;

namespace LanMountainDesktop.Platform.Windows;

internal static class PatcherEntrance
{
    public static void InstallPatchers()
    {
        var harmony = new Harmony("dev.lanmountain.desktop.patchers");
        harmony.PatchAll(typeof(PatcherEntrance).Assembly);
    }
}
