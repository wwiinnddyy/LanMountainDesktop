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

    // Postfix 不是"被代码调用"的：HarmonyLib 按 [HarmonyPatch] 与 Postfix 命名约定反射拿它
    // （装配级 PatchAll 在 PatcherEntrance.InstallPatchers，由 Program.cs:186 启动时调）。
    // IDE0051 在这里是误报，别照它删——删了"用系统边框时摘掉 :windows 伪类"这一步就静默失效。
    // 范围只圈这一个方法：上面那两个 [UnsafeAccessor] extern 是真被调的（实现由运行时生成）。
#pragma warning disable IDE0051
    static void Postfix(FAAppWindow __instance)
    {
        if (!ChromePatchState.UseSystemChrome) return;
        GetPseudoClasses(__instance).Remove(":windows");
        SetIsWindowsProperty(__instance, false);
    }
#pragma warning restore IDE0051
}
