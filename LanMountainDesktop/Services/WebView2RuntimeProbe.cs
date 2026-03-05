using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

public sealed record WebView2RuntimeAvailability(
    bool IsAvailable,
    string? Version,
    string Message);

public static class WebView2RuntimeProbe
{
    private const string WebView2RuntimeClientId = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string WebView2RuntimeKeyPath = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + WebView2RuntimeClientId;
    public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static WebView2RuntimeAvailability GetAvailability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WebView2RuntimeAvailability(
                IsAvailable: true,
                Version: null,
                Message: string.Empty);
        }

        try
        {
            var version = TryGetVersionFromWebView2Api();
            if (string.IsNullOrWhiteSpace(version))
            {
                version = TryGetVersionFromRegistry();
            }

            if (!string.IsNullOrWhiteSpace(version))
            {
                return new WebView2RuntimeAvailability(
                    IsAvailable: true,
                    Version: version.Trim(),
                    Message: string.Empty);
            }

            return new WebView2RuntimeAvailability(
                IsAvailable: false,
                Version: null,
                Message: $"WebView2 Runtime is missing. Install it from {RuntimeDownloadUrl} and restart the app.");
        }
        catch (Exception ex)
        {
            return new WebView2RuntimeAvailability(
                IsAvailable: false,
                Version: null,
                Message: $"WebView2 runtime check failed: {ex.Message}");
        }
    }

    public static string ResolveUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        var userDataFolder = Path.Combine(localAppData, "LanMountainDesktop", "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return userDataFolder;
    }

    private static string? TryGetVersionFromWebView2Api()
    {
        var type = Type.GetType(
            "Microsoft.Web.WebView2.Core.CoreWebView2Environment, Microsoft.Web.WebView2.Core",
            throwOnError: false);
        if (type is null)
        {
            return null;
        }

        var method = type.GetMethod(
            "GetAvailableBrowserVersionString",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (method is null)
        {
            return null;
        }

        return method.Invoke(null, null) as string;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetVersionFromRegistry()
    {
        return TryReadVersionFromRegistry(RegistryHive.LocalMachine, RegistryView.Registry64)
               ?? TryReadVersionFromRegistry(RegistryHive.LocalMachine, RegistryView.Registry32)
               ?? TryReadVersionFromRegistry(RegistryHive.CurrentUser, RegistryView.Registry64)
               ?? TryReadVersionFromRegistry(RegistryHive.CurrentUser, RegistryView.Registry32);
    }

    [SupportedOSPlatform("windows")]
    private static string? TryReadVersionFromRegistry(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runtimeKey = baseKey.OpenSubKey(WebView2RuntimeKeyPath, writable: false);
            if (runtimeKey is null)
            {
                return null;
            }

            var value = runtimeKey.GetValue("pv") as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }
}
