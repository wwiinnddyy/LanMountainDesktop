using System;
using System.IO;
using System.Linq;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.AirApps;
using LanMountainDesktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.AirAppHost;

/// <summary>
/// Loads a third-party AirApp window into the AirAppHost process.
/// Reads the AirApp package manifest, loads the entrance assembly with the desktop
/// <see cref="AirAppLoader"/>, resolves the requested window by id, and produces the
/// window "blueprint" (content + descriptor) to be hosted inside the FAAppWindow shell.
/// </summary>
internal sealed class AirAppWindowLoader
{
    /// <summary>
    /// Result of loading a third-party AirApp window.
    /// </summary>
    internal sealed record LoadedAirAppWindow(
        IAirAppWindow Window,
        LoadedAirApp LoadedAirApp);

    public LoadedAirAppWindow Load(AirAppLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.AppPackagePath))
        {
            throw new InvalidOperationException("AirAppWindowLoader requires an --app-package path.");
        }

        var packageDirectory = Path.GetFullPath(options.AppPackagePath);
        if (!Directory.Exists(packageDirectory))
        {
            throw new InvalidOperationException($"AirApp package directory was not found: '{packageDirectory}'.");
        }

        var manifestPath = Path.Combine(packageDirectory, AirAppSdkInfo.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"AirApp package directory does not contain '{AirAppSdkInfo.ManifestFileName}': '{packageDirectory}'.");
        }

        var manifest = AirAppManifest.Load(manifestPath);
        var assemblyPath = manifest.ResolveEntranceAssemblyPath(manifestPath);
        if (!File.Exists(assemblyPath))
        {
            throw new InvalidOperationException($"AirApp entrance assembly was not found: '{assemblyPath}'.");
        }

        var loader = new AirAppLoader();
        var loadResult = loader.LoadFromAssembly(assemblyPath, manifest);
        if (!loadResult.IsSuccess || loadResult.LoadedAirApp is null)
        {
            throw new InvalidOperationException(
                $"Failed to load AirApp '{manifest.Id}': {loadResult.Error?.Message ?? "unknown error"}",
                loadResult.Error);
        }

        var loadedAirApp = loadResult.LoadedAirApp;
        try
        {
            var windowId = ResolveWindowId(options, manifest, loadedAirApp);
            var windowRegistration = loadedAirApp.Services
                .GetServices<AirAppWindowRegistration>()
                .FirstOrDefault(registration => string.Equals(registration.WindowId, windowId, StringComparison.OrdinalIgnoreCase));

            if (windowRegistration is null)
            {
                throw new InvalidOperationException(
                    $"AirApp '{manifest.Id}' does not declare a window with id '{windowId}'. " +
                    "Register windows with AddAirAppWindow<TWindow> in Initialize().");
            }

            var window = CreateWindow(loadedAirApp, windowRegistration);
            AppLogger.Info(
                "AirAppWindow",
                $"Loaded third-party AirApp window. AirAppId='{manifest.Id}'; WindowId='{windowId}'; WindowType='{windowRegistration.WindowType.FullName}'.");
            return new LoadedAirAppWindow(window, loadedAirApp);
        }
        catch
        {
            loadedAirApp.Dispose();
            throw;
        }
    }

    private static string ResolveWindowId(
        AirAppLaunchOptions options,
        AirAppManifest manifest,
        LoadedAirApp loadedAirApp)
    {
        if (!string.IsNullOrWhiteSpace(options.TargetEntryId))
        {
            return options.TargetEntryId.Trim();
        }

        var declaredWindows = loadedAirApp.Services.GetServices<AirAppWindowRegistration>().ToArray();
        if (declaredWindows.Length == 1)
        {
            return declaredWindows[0].WindowId;
        }

        if (manifest.Windows is { Count: > 0 })
        {
            return manifest.Windows[0].Id;
        }

        throw new InvalidOperationException(
            $"AirApp '{manifest.Id}' exposes no windows and no --target-entry-id was provided.");
    }

    private static IAirAppWindow CreateWindow(LoadedAirApp loadedAirApp, AirAppWindowRegistration registration)
    {
        var instance = loadedAirApp.Services.GetService(registration.WindowType);
        instance ??= ActivatorUtilities.CreateInstance(loadedAirApp.Services, registration.WindowType);

        if (instance is not IAirAppWindow window)
        {
            throw new InvalidOperationException(
                $"Window type '{registration.WindowType.FullName}' does not implement IAirAppWindow.");
        }

        return window;
    }
}
