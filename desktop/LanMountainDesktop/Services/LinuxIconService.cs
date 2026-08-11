using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace LanMountainDesktop.Services;

[SupportedOSPlatform("linux")]
internal static class LinuxIconService
{
    private static readonly string[] IconThemePaths = {
        "/usr/share/icons",
        "/usr/share/pixmaps",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/icons"),
        "/var/lib/snapd/desktop/icons"
    };

    private static readonly string[] IconSizes = { "512x512", "256x256", "128x128", "96x96", "64x64", "48x48", "32x32", "24x24", "16x16" };

    private static readonly string[] FolderIconNames = { "folder", "inode-directory", "folder-default" };
    private static readonly string[] DriveIconNames = { "drive-harddisk", "drive-removable-media", "media-removable" };

    public static byte[]? TryGetIconPngBytes(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var iconName = GetIconNameForExtension(extension);

            return TryGetThemeIcon(iconName);
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryGetIconPngBytes(string iconName, string? searchDirectory)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(iconName) && File.Exists(iconName))
            {
                if (iconName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    return File.ReadAllBytes(iconName);
                }

                if (iconName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (iconName.EndsWith(".xpm", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            var pngBytes = TryGetThemeIcon(iconName);
            if (pngBytes is not null)
            {
                return pngBytes;
            }

            if (!string.IsNullOrWhiteSpace(searchDirectory))
            {
                var localIconPath = Path.Combine(searchDirectory, "icons", iconName + ".png");
                if (File.Exists(localIconPath))
                {
                    return File.ReadAllBytes(localIconPath);
                }

                localIconPath = Path.Combine(searchDirectory, iconName + ".png");
                if (File.Exists(localIconPath))
                {
                    return File.ReadAllBytes(localIconPath);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryGetSystemFolderIconPngBytes()
    {
        foreach (var iconName in FolderIconNames)
        {
            var iconBytes = TryGetThemeIcon(iconName);
            if (iconBytes is not null)
            {
                return iconBytes;
            }
        }

        return null;
    }

    public static byte[]? TryGetDriveIconPngBytes()
    {
        foreach (var iconName in DriveIconNames)
        {
            var iconBytes = TryGetThemeIcon(iconName);
            if (iconBytes is not null)
            {
                return iconBytes;
            }
        }

        return null;
    }

    private static string GetIconNameForExtension(string extension)
    {
        return extension switch
        {
            ".txt" => "text-x-generic",
            ".md" => "text-x-markdown",
            ".pdf" => "application-pdf",
            ".doc" or ".docx" => "application-msword",
            ".xls" or ".xlsx" => "application-vnd.ms-excel",
            ".ppt" or ".pptx" => "application-vnd.ms-powerpoint",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "application-x-archive",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "audio-x-generic",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "video-x-generic",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" => "image-x-generic",
            ".cs" => "text-x-csharp",
            ".js" or ".ts" => "text-x-javascript",
            ".py" => "text-x-python",
            ".java" => "text-x-java",
            ".cpp" or ".c" or ".h" => "text-x-c++",
            ".json" => "application-json",
            ".xml" => "text-xml",
            ".html" or ".htm" => "text-html",
            ".css" => "text-css",
            ".sh" or ".bash" => "text-x-script",
            ".exe" or ".msi" => "application-x-executable",
            ".deb" or ".rpm" => "application-x-package",
            ".iso" or ".img" => "application-x-cd-image",
            _ => "text-x-generic"
        };
    }

    private static byte[]? TryGetThemeIcon(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        foreach (var themePath in IconThemePaths)
        {
            if (!Directory.Exists(themePath))
            {
                continue;
            }

            var iconBytes = TryFindIconInTheme(themePath, iconName);
            if (iconBytes is not null)
            {
                return iconBytes;
            }
        }

        return TryGetIconFromGtkTheme(iconName);
    }

    private static byte[]? TryFindIconInTheme(string themePath, string iconName)
    {
        try
        {
            foreach (var sizeDir in IconSizes)
            {
                var iconPath = Path.Combine(themePath, "Adwaita", sizeDir, "mimetypes", $"{iconName}.png");
                if (File.Exists(iconPath))
                {
                    return File.ReadAllBytes(iconPath);
                }

                iconPath = Path.Combine(themePath, "Adwaita", sizeDir, "places", $"{iconName}.png");
                if (File.Exists(iconPath))
                {
                    return File.ReadAllBytes(iconPath);
                }

                iconPath = Path.Combine(themePath, "Adwaita", sizeDir, "devices", $"{iconName}.png");
                if (File.Exists(iconPath))
                {
                    return File.ReadAllBytes(iconPath);
                }
            }

            foreach (var sizeDir in IconSizes)
            {
                var iconPath = Path.Combine(themePath, "hicolor", sizeDir, "mimetypes", $"{iconName}.png");
                if (File.Exists(iconPath))
                {
                    return File.ReadAllBytes(iconPath);
                }

                iconPath = Path.Combine(themePath, "hicolor", sizeDir, "places", $"{iconName}.png");
                if (File.Exists(iconPath))
                {
                    return File.ReadAllBytes(iconPath);
                }

                iconPath = Path.Combine(themePath, "hicolor", sizeDir, "devices", $"{iconName}.png");
                if (File.Exists(iconPath))
                {
                    return File.ReadAllBytes(iconPath);
                }
            }

            var directPath = Path.Combine(themePath, $"{iconName}.png");
            if (File.Exists(directPath))
            {
                return File.ReadAllBytes(directPath);
            }
        }
        catch
        {
        }

        return null;
    }

    private static byte[]? TryGetIconFromGtkTheme(string iconName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gtk3-icon-browser",
                    Arguments = $"--icon={iconName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            return null;
        }
        catch
        {
            return null;
        }
    }
}
