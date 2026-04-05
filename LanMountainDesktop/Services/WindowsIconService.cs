using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace LanMountainDesktop.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsIconService
{
    private const int HighResolutionIconSize = 256;
    private const int MaxShellPath = 1024;
    private const int StgmRead = 0x00000000;
    private const uint SiigbfBiggerSizeOk = 0x00000001;
    private const uint SiigbfIconOnly = 0x00000004;
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiLargeIcon = 0x00000000;
    private const uint ShgfiUseFileAttributes = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint CoinitApartmentThreaded = 0x2;
    private const int SOk = 0;
    private const int SFalse = 1;
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    private static readonly Regex AumidRegex =
        new(@"shell:AppsFolder\\(?<aumid>[^\s""]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static byte[]? TryGetIconPngBytes(string filePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var normalizedEntryPath = Path.GetFullPath(filePath);
        if (!File.Exists(normalizedEntryPath))
        {
            return null;
        }

        try
        {
            var extension = Path.GetExtension(normalizedEntryPath);
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadLnkIconLocation(normalizedEntryPath, out var iconLocation, out var iconIndex) &&
                    TryResolveIconPath(iconLocation, normalizedEntryPath, out var resolvedIconPath) &&
                    TryExtractIconFromResourceFile(resolvedIconPath, iconIndex, out var pngBytesFromLnkIconLocation))
                {
                    return pngBytesFromLnkIconLocation;
                }

                if (TryReadLnkArguments(normalizedEntryPath, out var arguments) &&
                    TryParseAumidFromArguments(arguments, out var aumid))
                {
                    var appsFolderPath = $"shell:AppsFolder\\{aumid}";
                    if (TryExtractIconWithShellItemImageFactory(appsFolderPath, out var pngBytesFromAppsFolder))
                    {
                        return pngBytesFromAppsFolder;
                    }

                    if (UwpManifestIconResolver.TryGetIconPngBytesFromAumid(aumid, out var pngBytesFromManifest))
                    {
                        return pngBytesFromManifest;
                    }
                }

                if (TryReadLnkTargetPath(normalizedEntryPath, out var targetPath) &&
                    TryExtractIconFromResourceFile(targetPath, 0, out var pngBytesFromLnkTarget))
                {
                    return pngBytesFromLnkTarget;
                }
            }
            else if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadUrlIconLocation(normalizedEntryPath, out var iconFile, out var iconIndex) &&
                    TryResolveIconPath(iconFile, normalizedEntryPath, out var resolvedIconPath) &&
                    TryExtractIconFromResourceFile(resolvedIconPath, iconIndex, out var pngBytesFromUrlIconLocation))
                {
                    return pngBytesFromUrlIconLocation;
                }
            }

            if (TryExtractIconWithShellItemImageFactory(normalizedEntryPath, out var pngBytesFromShellItem))
            {
                return pngBytesFromShellItem;
            }

            if (TryExtractIconWithShGetFileInfo(normalizedEntryPath, out var pngBytesFromShGetFileInfo))
            {
                return pngBytesFromShGetFileInfo;
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
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        // Prefer the HICON-based path first to preserve alpha better for folder glyphs.
        if (TryExtractFolderIconWithShGetFileInfo(out var shGetFolderIcon) &&
            shGetFolderIcon is not null)
        {
            return shGetFolderIcon;
        }

        var isWin11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        var preferredProbePaths = isWin11
            ? new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.SystemDirectory
            }
            : new[]
            {
                Environment.SystemDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

        foreach (var probePath in preferredProbePaths)
        {
            if (string.IsNullOrWhiteSpace(probePath) || !Directory.Exists(probePath))
            {
                continue;
            }

            if (TryExtractIconWithShellItemImageFactory(probePath, out var shellFolderIcon))
            {
                return shellFolderIcon;
            }
        }

        return null;
    }

    private static bool TryParseAumidFromArguments(string arguments, out string aumid)
    {
        aumid = string.Empty;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        var match = AumidRegex.Match(arguments);
        if (!match.Success)
        {
            return false;
        }

        aumid = match.Groups["aumid"].Value.Trim().Trim('"');
        return !string.IsNullOrWhiteSpace(aumid);
    }

    private static bool TryReadLnkIconLocation(string lnkFilePath, out string iconLocation, out int iconIndex)
    {
        iconLocation = string.Empty;
        iconIndex = 0;
        if (!TryInitializeCom(out var shouldUninitialize))
        {
            return false;
        }

        try
        {
            if (!TryCreateShellLink(out var shellLink))
            {
                return false;
            }

            try
            {
                if (!TryLoadShellLink(shellLink, lnkFilePath))
                {
                    return false;
                }

                var iconPathBuilder = new StringBuilder(MaxShellPath);
                if (shellLink.GetIconLocation(iconPathBuilder, iconPathBuilder.Capacity, out iconIndex) < 0)
                {
                    return false;
                }

                iconLocation = iconPathBuilder.ToString().Trim();
                return !string.IsNullOrWhiteSpace(iconLocation);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            UninitializeCom(shouldUninitialize);
        }
    }

    private static bool TryReadLnkTargetPath(string lnkFilePath, out string targetPath)
    {
        targetPath = string.Empty;
        if (!TryInitializeCom(out var shouldUninitialize))
        {
            return false;
        }

        try
        {
            if (!TryCreateShellLink(out var shellLink))
            {
                return false;
            }

            try
            {
                if (!TryLoadShellLink(shellLink, lnkFilePath))
                {
                    return false;
                }

                var targetPathBuilder = new StringBuilder(MaxShellPath);
                if (shellLink.GetPath(targetPathBuilder, targetPathBuilder.Capacity, IntPtr.Zero, 0) < 0)
                {
                    return false;
                }

                targetPath = targetPathBuilder.ToString().Trim();
                return !string.IsNullOrWhiteSpace(targetPath);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            UninitializeCom(shouldUninitialize);
        }
    }

    private static bool TryReadLnkArguments(string lnkFilePath, out string arguments)
    {
        arguments = string.Empty;
        if (!TryInitializeCom(out var shouldUninitialize))
        {
            return false;
        }

        try
        {
            if (!TryCreateShellLink(out var shellLink))
            {
                return false;
            }

            try
            {
                if (!TryLoadShellLink(shellLink, lnkFilePath))
                {
                    return false;
                }

                var argumentsBuilder = new StringBuilder(MaxShellPath);
                if (shellLink.GetArguments(argumentsBuilder, argumentsBuilder.Capacity) < 0)
                {
                    return false;
                }

                arguments = argumentsBuilder.ToString().Trim();
                return !string.IsNullOrWhiteSpace(arguments);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            UninitializeCom(shouldUninitialize);
        }
    }

    private static bool TryCreateShellLink(out IShellLinkW shellLink)
    {
        shellLink = null!;
        var shellLinkType = Type.GetTypeFromCLSID(CLSID_ShellLink);
        if (shellLinkType is null)
        {
            return false;
        }

        shellLink = (IShellLinkW?)Activator.CreateInstance(shellLinkType)!;
        return shellLink is not null;
    }

    private static bool TryLoadShellLink(IShellLinkW shellLink, string lnkFilePath)
    {
        if (shellLink is not IPersistFile persistFile)
        {
            return false;
        }

        try
        {
            persistFile.Load(lnkFilePath, StgmRead);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadUrlIconLocation(string urlFilePath, out string iconFile, out int iconIndex)
    {
        iconFile = string.Empty;
        iconIndex = 0;
        if (!File.Exists(urlFilePath))
        {
            return false;
        }

        try
        {
            foreach (var rawLine in File.ReadLines(urlFilePath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                {
                    iconFile = line["IconFile=".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line["IconIndex=".Length..].Trim(), out var parsedIndex))
                {
                    iconIndex = parsedIndex;
                }
            }
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(iconFile))
        {
            return false;
        }

        if (TrySplitIconFileAndIndex(iconFile, out var splitIconFile, out var splitIndex))
        {
            iconFile = splitIconFile;
            if (iconIndex == 0)
            {
                iconIndex = splitIndex;
            }
        }

        return true;
    }

    private static bool TryResolveIconPath(string rawIconLocation, string shortcutPath, out string resolvedIconPath)
    {
        resolvedIconPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawIconLocation))
        {
            return false;
        }

        var cleaned = rawIconLocation.Trim().Trim('"');
        if (cleaned.StartsWith("@", StringComparison.Ordinal))
        {
            cleaned = cleaned[1..];
        }

        if (TrySplitIconFileAndIndex(cleaned, out var splitPath, out _))
        {
            cleaned = splitPath;
        }

        cleaned = Environment.ExpandEnvironmentVariables(cleaned);
        if (cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Path.IsPathRooted(cleaned))
        {
            var shortcutDirectory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrWhiteSpace(shortcutDirectory))
            {
                var relativeResolved = Path.GetFullPath(Path.Combine(shortcutDirectory, cleaned));
                if (File.Exists(relativeResolved))
                {
                    resolvedIconPath = relativeResolved;
                    return true;
                }
            }

            var systemResolved = Path.Combine(Environment.SystemDirectory, cleaned);
            if (File.Exists(systemResolved))
            {
                resolvedIconPath = systemResolved;
                return true;
            }
        }

        if (File.Exists(cleaned))
        {
            resolvedIconPath = cleaned;
            return true;
        }

        return false;
    }

    private static bool TrySplitIconFileAndIndex(string rawValue, out string iconFile, out int iconIndex)
    {
        iconFile = rawValue.Trim();
        iconIndex = 0;
        if (string.IsNullOrWhiteSpace(iconFile))
        {
            return false;
        }

        var commaIndex = iconFile.LastIndexOf(',');
        if (commaIndex <= 0 || commaIndex >= iconFile.Length - 1)
        {
            return false;
        }

        var possibleIndex = iconFile[(commaIndex + 1)..].Trim();
        if (!int.TryParse(possibleIndex, out iconIndex))
        {
            return false;
        }

        iconFile = iconFile[..commaIndex].Trim().Trim('"');
        return !string.IsNullOrWhiteSpace(iconFile);
    }

    private static bool TryExtractIconFromResourceFile(string resourceFilePath, int iconIndex, out byte[]? pngBytes)
    {
        pngBytes = null;
        if (string.IsNullOrWhiteSpace(resourceFilePath) || !File.Exists(resourceFilePath))
        {
            return false;
        }

        if (TryExtractIconWithShDefExtractIcon(resourceFilePath, iconIndex, out pngBytes))
        {
            return true;
        }

        if (TryExtractIconWithPrivateExtractIcons(resourceFilePath, iconIndex, out pngBytes))
        {
            return true;
        }

        return TryExtractIconWithExtractIconEx(resourceFilePath, iconIndex, out pngBytes);
    }

    private static bool TryExtractIconWithShDefExtractIcon(string filePath, int iconIndex, out byte[]? pngBytes)
    {
        pngBytes = null;
        var requestedSize = MakeLong(HighResolutionIconSize, HighResolutionIconSize);
        var hr = SHDefExtractIcon(filePath, iconIndex, 0, out var largeIcon, out _, (uint)requestedSize);
        if (hr < 0 || largeIcon == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            pngBytes = ConvertHiconToPngBytes(largeIcon);
            return pngBytes is not null;
        }
        finally
        {
            _ = DestroyIcon(largeIcon);
        }
    }

    private static bool TryExtractIconWithPrivateExtractIcons(string filePath, int iconIndex, out byte[]? pngBytes)
    {
        pngBytes = null;
        var iconHandles = new IntPtr[1];
        var iconIds = new uint[1];
        var extracted = PrivateExtractIcons(
            filePath,
            iconIndex,
            HighResolutionIconSize,
            HighResolutionIconSize,
            iconHandles,
            iconIds,
            1,
            0);
        if (extracted == 0 || extracted == 0xFFFFFFFF || iconHandles[0] == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            pngBytes = ConvertHiconToPngBytes(iconHandles[0]);
            return pngBytes is not null;
        }
        finally
        {
            _ = DestroyIcon(iconHandles[0]);
        }
    }

    private static bool TryExtractIconWithExtractIconEx(string filePath, int iconIndex, out byte[]? pngBytes)
    {
        pngBytes = null;
        var largeIcons = new IntPtr[1];
        var extracted = ExtractIconEx(filePath, iconIndex, largeIcons, null, 1);
        if (extracted <= 0 || largeIcons[0] == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            pngBytes = ConvertHiconToPngBytes(largeIcons[0]);
            return pngBytes is not null;
        }
        finally
        {
            _ = DestroyIcon(largeIcons[0]);
        }
    }

    private static bool TryExtractIconWithShellItemImageFactory(string filePath, out byte[]? pngBytes)
    {
        pngBytes = null;
        if (SHCreateItemFromParsingName(filePath, IntPtr.Zero, IID_IShellItemImageFactory, out var imageFactoryObject) < 0 ||
            imageFactoryObject is null)
        {
            return false;
        }

        try
        {
            var imageFactory = (IShellItemImageFactory)imageFactoryObject;
            var size = new SizeStruct { cx = HighResolutionIconSize, cy = HighResolutionIconSize };
            var flags = SiigbfIconOnly | SiigbfBiggerSizeOk;
            if (imageFactory.GetImage(size, flags, out var hBitmap) < 0 || hBitmap == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                pngBytes = ConvertHbitmapToPngBytes(hBitmap);
                return pngBytes is not null;
            }
            finally
            {
                _ = DeleteObject(hBitmap);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FinalReleaseComObject(imageFactoryObject);
        }
    }

    private static bool TryExtractIconWithShGetFileInfo(string filePath, out byte[]? pngBytes)
    {
        pngBytes = null;
        if (SHGetFileInfo(
                filePath,
                FileAttributeNormal,
                out var fileInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                ShgfiIcon | ShgfiUseFileAttributes) == IntPtr.Zero ||
            fileInfo.hIcon == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            pngBytes = ConvertHiconToPngBytes(fileInfo.hIcon);
            return pngBytes is not null;
        }
        finally
        {
            _ = DestroyIcon(fileInfo.hIcon);
        }
    }

    private static bool TryExtractFolderIconWithShGetFileInfo(out byte[]? pngBytes)
    {
        pngBytes = null;
        if (SHGetFileInfo(
                "folder",
                FileAttributeDirectory,
                out var fileInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes) == IntPtr.Zero ||
            fileInfo.hIcon == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            pngBytes = ConvertHiconToPngBytes(fileInfo.hIcon);
            return pngBytes is not null;
        }
        finally
        {
            _ = DestroyIcon(fileInfo.hIcon);
        }
    }

    private static byte[]? ConvertHiconToPngBytes(IntPtr iconHandle)
    {
        if (iconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(iconHandle);
            var width = Math.Max(16, icon.Width);
            var height = Math.Max(16, icon.Height);
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawIcon(icon, new Rectangle(0, 0, width, height));
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ConvertHbitmapToPngBytes(IntPtr bitmapHandle)
    {
        if (bitmapHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var source = Image.FromHbitmap(bitmapHandle);
            var width = source.Width;
            var height = source.Height;

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, 0, 0, width, height);
            }

            FixBitmapAlpha(bitmap);

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void FixBitmapAlpha(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        
        try
        {
            var bytes = Math.Abs(data.Stride) * height;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);

            for (var i = 0; i < bytes; i += 4)
            {
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                var a = buffer[i + 3];

                if (a == 0 && (r != 0 || g != 0 || b != 0))
                {
                    a = (byte)Math.Max(r, Math.Max(g, b));
                    buffer[i + 3] = a;
                }
                else if (a > 0 && a < 255)
                {
                    buffer[i] = (byte)(b * 255 / a);
                    buffer[i + 1] = (byte)(g * 255 / a);
                    buffer[i + 2] = (byte)(r * 255 / a);
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static bool TryInitializeCom(out bool shouldUninitialize)
    {
        shouldUninitialize = false;
        var result = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);
        if (result is SOk or SFalse)
        {
            shouldUninitialize = true;
            return true;
        }

        return result == RpcEChangedMode;
    }

    private static void UninitializeCom(bool shouldUninitialize)
    {
        if (shouldUninitialize)
        {
            CoUninitialize();
        }
    }

    private static int MakeLong(int lowWord, int highWord)
    {
        return (highWord << 16) | (lowWord & 0xFFFF);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeStruct
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        [PreserveSig] int GetIDList(out IntPtr ppidl);
        [PreserveSig] int SetIDList(IntPtr pidl);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        [PreserveSig] int GetHotkey(out short pwHotkey);
        [PreserveSig] int SetHotkey(short wHotkey);
        [PreserveSig] int GetShowCmd(out int piShowCmd);
        [PreserveSig] int SetShowCmd(int iShowCmd);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int iIcon);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint fFlags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SizeStruct size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        uint[] piconid,
        uint nIcons,
        uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
