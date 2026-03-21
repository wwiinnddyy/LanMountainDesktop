using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

public interface ISystemWallpaperProvider
{
    bool IsSupported { get; }
    string? GetWallpaperPath();
    event EventHandler? WallpaperChanged;
}

internal sealed class SystemWallpaperProvider : ISystemWallpaperProvider, IDisposable
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public event EventHandler? WallpaperChanged;

    public string? GetWallpaperPath()
    {
        if (!IsSupported)
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var wallpaperPath = key?.GetValue("Wallpaper") as string;

            if (string.IsNullOrWhiteSpace(wallpaperPath))
            {
                return null;
            }

            if (!File.Exists(wallpaperPath))
            {
                return null;
            }

            return wallpaperPath;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
    }
}

public static class HostSystemWallpaperProvider
{
    private static ISystemWallpaperProvider? _instance;

    public static ISystemWallpaperProvider GetOrCreate()
    {
        return _instance ??= new SystemWallpaperProvider();
    }
}
