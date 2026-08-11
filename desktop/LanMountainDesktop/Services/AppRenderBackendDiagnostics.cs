using System;
using System.Reflection;
using Avalonia;
using Avalonia.Platform;

namespace LanMountainDesktop.Services;

public readonly record struct AppRenderBackendInfo(
    string ActualBackend,
    string? ImplementationTypeName);

public static class AppRenderBackendDiagnostics
{
    public const string Unknown = "Unknown";

    public static AppRenderBackendInfo Detect()
    {
        var platformGraphics = GetPlatformGraphics();
        var implementationTypeName = platformGraphics?.GetType().FullName;
        var actualBackend = DetectBackendFromImplementationType(implementationTypeName, platformGraphics is null);

        return new AppRenderBackendInfo(actualBackend, implementationTypeName);
    }

    private static object? GetPlatformGraphics()
    {
        var currentResolver = typeof(AvaloniaLocator)
            .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);

        var getServiceMethod = currentResolver?
            .GetType()
            .GetMethod(
                "GetService",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(Type)],
                modifiers: null);

        return getServiceMethod?.Invoke(currentResolver, [typeof(IPlatformGraphics)]);
    }

    private static string DetectBackendFromImplementationType(string? implementationTypeName, bool isSoftwareFallback)
    {
        if (isSoftwareFallback)
        {
            return AppRenderingModeHelper.Software;
        }

        if (string.IsNullOrWhiteSpace(implementationTypeName))
        {
            return Unknown;
        }

        if (implementationTypeName.Contains("Vulkan", StringComparison.OrdinalIgnoreCase))
        {
            return AppRenderingModeHelper.Vulkan;
        }

        if (implementationTypeName.Contains("Wgl", StringComparison.OrdinalIgnoreCase))
        {
            return AppRenderingModeHelper.Wgl;
        }

        if (implementationTypeName.Contains("Angle", StringComparison.OrdinalIgnoreCase) ||
            implementationTypeName.Contains("Egl", StringComparison.OrdinalIgnoreCase))
        {
            return AppRenderingModeHelper.AngleEgl;
        }

        return Unknown;
    }
}
