using Avalonia;

namespace LanMountainDesktop.Services;

public static class AppRenderingModeHelper
{
    public const string Default = "Default";
    public const string Software = "Software";
    public const string AngleEgl = "AngleEgl";
    public const string Wgl = "Wgl";
    public const string Vulkan = "Vulkan";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "SOFTWARE" => Software,
            "ANGLEEGL" => AngleEgl,
            "ANGLE_EGL" => AngleEgl,
            "WGL" => Wgl,
            "VULKAN" => Vulkan,
            _ => Default
        };
    }

    public static Win32RenderingMode[]? GetWin32RenderingModes(string? value)
    {
        return Normalize(value) switch
        {
            Software => [Win32RenderingMode.Software],
            AngleEgl => [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            Wgl => [Win32RenderingMode.Wgl, Win32RenderingMode.Software],
            Vulkan => [Win32RenderingMode.Vulkan, Win32RenderingMode.Software],
            _ => null
        };
    }
}
