using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LanMountainDesktop.Services;

[SupportedOSPlatform("macos")]
internal static class MacIconService
{
    private const int IconSize = 256;

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr NSWorkspace_sharedWorkspace();

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr NSWorkspace_iconForFile(IntPtr workspace, IntPtr filePath);

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr NSImage_initWithContentsOfFile(IntPtr path);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGImageDestinationCreateWithURL(IntPtr url, IntPtr type, uint count, IntPtr options);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGImageDestinationAddImage(IntPtr dest, IntPtr image, IntPtr properties);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern bool CGImageDestinationFinalize(IntPtr dest);

    [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static extern IntPtr NSString_stringWithUTF8String(string str);

    [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static extern IntPtr NSURL_fileURLWithPath(IntPtr path);

    [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static extern void CFRelease(IntPtr handle);

    [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static extern IntPtr NSTemporaryDirectory();

    private static readonly string[] SystemFolderPaths =
    {
        "/System/Library/CoreServices/CoreTypes.bundle/Contents/Resources",
        "/System/Library/Extensions",
        "/System/Library/PrivateFrameworks"
    };

    private static readonly string[] FolderIconNames = { "GenericFolderIcon.icns", "SidebarDownloadsFolder.icns", "SidebarDocumentsFolder.icns" };
    private static readonly string[] DriveIconNames = { "GenericHardDiskIcon.icns", "ExternalDiskIcon.icns", "RemovableDiskIcon.icns" };

    public static byte[]? TryGetIconPngBytes(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return TryGetIconUsingNSWorkspace(filePath);
        }
        catch
        {
        }

        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return TryGetIconForExtension(extension);
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryGetSystemFolderIconPngBytes()
    {
        foreach (var folderPath in SystemFolderPaths)
        {
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            foreach (var iconName in FolderIconNames)
            {
                var iconPath = Path.Combine(folderPath, iconName);
                if (File.Exists(iconPath))
                {
                    var pngBytes = TryConvertIcnsToPng(iconPath);
                    if (pngBytes is not null)
                    {
                        return pngBytes;
                    }
                }
            }
        }

        return TryGetIconUsingNSWorkspace("/System/Library/CoreServices");
    }

    public static byte[]? TryGetDriveIconPngBytes()
    {
        foreach (var folderPath in SystemFolderPaths)
        {
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            foreach (var iconName in DriveIconNames)
            {
                var iconPath = Path.Combine(folderPath, iconName);
                if (File.Exists(iconPath))
                {
                    var pngBytes = TryConvertIcnsToPng(iconPath);
                    if (pngBytes is not null)
                    {
                        return pngBytes;
                    }
                }
            }
        }

        return TryGetIconUsingNSWorkspace("/");
    }

    private static byte[]? TryGetIconUsingNSWorkspace(string filePath)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"icon_{Guid.NewGuid():N}.png");

            var script = $@"
tell application ""System Events""
    set theIcon to icon of file ""{filePath}""
end tell
";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e 'tell application \"Finder\" to get icon of file \"{filePath}\"'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            return TryGetIconUsingSips(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryGetIconUsingSips(string filePath)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"icon_{Guid.NewGuid():N}.png");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sips",
                    Arguments = $"-s format png -z {IconSize} {IconSize} \"{filePath}\" --out \"{tempPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit(5000);

            if (File.Exists(tempPath))
            {
                var bytes = File.ReadAllBytes(tempPath);
                File.Delete(tempPath);
                return bytes;
            }
        }
        catch
        {
        }

        return null;
    }

    private static byte[]? TryGetIconForExtension(string extension)
    {
        var iconName = GetIconNameForExtension(extension);

        foreach (var folderPath in SystemFolderPaths)
        {
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            var iconPath = Path.Combine(folderPath, iconName);
            if (File.Exists(iconPath))
            {
                var pngBytes = TryConvertIcnsToPng(iconPath);
                if (pngBytes is not null)
                {
                    return pngBytes;
                }
            }
        }

        return null;
    }

    private static string GetIconNameForExtension(string extension)
    {
        return extension switch
        {
            ".txt" => "TextEdit.icns",
            ".md" => "TextEdit.icns",
            ".pdf" => "Preview.icns",
            ".doc" or ".docx" => "Microsoft Word.icns",
            ".xls" or ".xlsx" => "Microsoft Excel.icns",
            ".ppt" or ".pptx" => "Microsoft PowerPoint.icns",
            ".zip" or ".rar" or ".7z" => "Archive Utility.icns",
            ".mp3" or ".wav" or ".flac" or ".aac" => "Music.icns",
            ".mp4" or ".avi" or ".mkv" or ".mov" => "QuickTime Player.icns",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "Preview.icns",
            ".cs" => "Visual Studio.icns",
            ".js" or ".ts" => "Visual Studio Code.icns",
            ".py" => "IDLE.icns",
            ".json" => "TextEdit.icns",
            ".xml" => "TextEdit.icns",
            ".html" or ".htm" => "Safari.icns",
            ".css" => "TextEdit.icns",
            ".sh" => "Terminal.icns",
            ".app" => "AppIcon.icns",
            ".dmg" => "DiskImage.icns",
            _ => "GenericDocumentIcon.icns"
        };
    }

    private static byte[]? TryConvertIcnsToPng(string icnsPath)
    {
        if (!File.Exists(icnsPath))
        {
            return null;
        }

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"icon_{Guid.NewGuid():N}.png");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sips",
                    Arguments = $"-s format png \"{icnsPath}\" --out \"{tempPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit(5000);

            if (File.Exists(tempPath))
            {
                var bytes = File.ReadAllBytes(tempPath);
                File.Delete(tempPath);
                return bytes;
            }
        }
        catch
        {
        }

        return null;
    }
}
