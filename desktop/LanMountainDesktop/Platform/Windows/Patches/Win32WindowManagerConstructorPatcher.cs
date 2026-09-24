using FluentAvalonia.UI.Windowing;
using HarmonyLib;
using LanMountainDesktop.Platform.Windows;

namespace LanMountainDesktop.Platform.Windows.Patches;

// 这个类里没有一个是"被代码调用"的：HarmonyLib 按 [HarmonyPatch] / [HarmonyTargetMethod] 与
// Prefix/Postfix 命名约定反射拿它们（装配级 PatchAll 在 PatcherEntrance.InstallPatchers，
// 由 Program.cs:186 在启动时调）。所以 IDE0051 在这里是误报，别照它删——删了窗口边框补丁就静默失效。
#pragma warning disable IDE0051

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

// 压制的范围就到这个类为止：往这个文件里再加成员时 IDE0051 照常生效。
#pragma warning restore IDE0051
