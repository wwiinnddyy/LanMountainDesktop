using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LanMountainDesktop.Services;

internal static class TelemetryEnvironmentInfo
{
    public static string GetAppVersion()
    {
        var assembly = typeof(TelemetryEnvironmentInfo).Assembly;
        var version = assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static string GetEnvironment()
    {
#if DEBUG
        return "development";
#else
        return "production";
#endif
    }

    public static string GetOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        return "Unknown";
    }

    public static string GetOsVersion()
    {
        try
        {
            return Environment.OSVersion.VersionString ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    public static string GetOsBuild()
    {
        try
        {
            return Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "Unknown";
        }
    }

    public static string GetDeviceModel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows PC";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux PC";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "Mac";
        }

        return "Unknown";
    }

    public static string GetDeviceArchitecture()
    {
        return RuntimeInformation.OSArchitecture.ToString();
    }

    public static string GetSystemLanguage()
    {
        try
        {
            return CultureInfo.CurrentUICulture.Name ?? "en-US";
        }
        catch
        {
            return "en-US";
        }
    }

    public static int GetProcessorCount()
    {
        return Environment.ProcessorCount;
    }

    public static long GetTotalMemoryMB()
    {
        try
        {
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }

    public static string GetRuntimeVersion()
    {
        return Environment.Version.ToString();
    }

    public static string GetClrVersion()
    {
        return Environment.Version.ToString();
    }

    public static string GetLocalDayPart(DateTimeOffset timestamp)
    {
        var hour = timestamp.ToLocalTime().Hour;
        return hour switch
        {
            < 6 => "late_night",
            < 12 => "morning",
            < 18 => "afternoon",
            _ => "evening"
        };
    }
}
