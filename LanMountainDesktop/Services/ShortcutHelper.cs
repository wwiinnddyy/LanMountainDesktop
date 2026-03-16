using System.Runtime.InteropServices;

namespace LanMountainDesktop.Services;

internal static class ShortcutHelper
{
    [ComImport]
    [Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")]
    internal class WshShell { }

    [ComImport]
    [Guid("F935DC21-1CF0-11D0-ADB9-00C04FD58A0B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    internal interface IWshShortcut
    {
        string TargetPath { get; set; }
    }

    public static string? GetShortcutTarget(string lnkPath)
    {
        try
        {
            dynamic shell = new WshShell();
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            return shortcut.TargetPath;
        }
        catch
        {
            return null;
        }
    }
}
