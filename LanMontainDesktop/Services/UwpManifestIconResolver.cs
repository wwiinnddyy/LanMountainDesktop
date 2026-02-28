using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LanMontainDesktop.Services;

[SupportedOSPlatform("windows")]
internal static class UwpManifestIconResolver
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Regex ScaleRegex =
        new(@"scale-(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TargetSizeRegex =
        new(@"targetsize-(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] ManifestLogoAttributePriority =
    [
        "Square44x44Logo",
        "Square150x150Logo",
        "Square310x310Logo",
        "Square71x71Logo",
        "Wide310x150Logo",
        "Logo",
        "SmallLogo"
    ];

    private static readonly Dictionary<string, byte[]?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static bool TryGetIconPngBytesFromAumid(string aumid, out byte[]? pngBytes)
    {
        pngBytes = null;
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return false;
        }

        lock (CacheLock)
        {
            if (IconCache.TryGetValue(aumid, out var cached))
            {
                pngBytes = cached;
                return cached is not null;
            }
        }

        if (!TrySplitAumid(aumid, out var packageFamilyName, out var appId))
        {
            return false;
        }

        foreach (var installLocation in GetPackageInstallLocations(packageFamilyName))
        {
            if (TryExtractIconFromManifestInstallLocation(installLocation, appId, out pngBytes))
            {
                lock (CacheLock)
                {
                    IconCache[aumid] = pngBytes;
                }

                return true;
            }
        }

        lock (CacheLock)
        {
            IconCache[aumid] = null;
        }

        return false;
    }

    private static bool TrySplitAumid(string aumid, out string packageFamilyName, out string appId)
    {
        packageFamilyName = string.Empty;
        appId = string.Empty;
        var separatorIndex = aumid.IndexOf('!');
        if (separatorIndex <= 0 || separatorIndex >= aumid.Length - 1)
        {
            return false;
        }

        packageFamilyName = aumid[..separatorIndex].Trim();
        appId = aumid[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(packageFamilyName) && !string.IsNullOrWhiteSpace(appId);
    }

    private static IReadOnlyList<string> GetPackageInstallLocations(string packageFamilyName)
    {
        var fromApi = GetPackageInstallLocationsByFamilyApi(packageFamilyName);
        return fromApi.Count > 0
            ? fromApi
            : GetPackageInstallLocationsByDirectoryScan(packageFamilyName);
    }

    private static IReadOnlyList<string> GetPackageInstallLocationsByFamilyApi(string packageFamilyName)
    {
        var results = new List<(string PackageFullName, string InstallLocation)>();

        uint packageCount = 0;
        uint bufferLength = 0;
        var firstCall = GetPackagesByPackageFamily(
            packageFamilyName,
            ref packageCount,
            null,
            ref bufferLength,
            null);
        if (firstCall != ErrorInsufficientBuffer && firstCall != ErrorSuccess)
        {
            return [];
        }

        if (packageCount == 0)
        {
            return [];
        }

        var packageFullNamePointers = new IntPtr[packageCount];
        var packageNamesBuffer = new char[Math.Max(1, (int)bufferLength)];
        var secondCall = GetPackagesByPackageFamily(
            packageFamilyName,
            ref packageCount,
            packageFullNamePointers,
            ref bufferLength,
            packageNamesBuffer);
        if (secondCall != ErrorSuccess)
        {
            return [];
        }

        for (var i = 0; i < packageCount; i++)
        {
            var packageFullName = Marshal.PtrToStringUni(packageFullNamePointers[i]);
            if (string.IsNullOrWhiteSpace(packageFullName))
            {
                continue;
            }

            uint pathLength = 0;
            var getPathFirst = GetPackagePathByFullName(packageFullName, ref pathLength, null);
            if (getPathFirst != ErrorInsufficientBuffer || pathLength == 0)
            {
                continue;
            }

            var pathBuilder = new StringBuilder((int)pathLength);
            var getPathSecond = GetPackagePathByFullName(packageFullName, ref pathLength, pathBuilder);
            if (getPathSecond != ErrorSuccess)
            {
                continue;
            }

            var installPath = pathBuilder.ToString().TrimEnd('\0').Trim();
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            {
                continue;
            }

            results.Add((packageFullName, installPath));
        }

        return results
            .OrderByDescending(item => ParsePackageVersion(item.PackageFullName))
            .Select(item => item.InstallLocation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetPackageInstallLocationsByDirectoryScan(string packageFamilyName)
    {
        if (!TrySplitPackageFamilyName(packageFamilyName, out var packageName, out var publisherId))
        {
            return [];
        }

        var windowsAppsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");

        if (!Directory.Exists(windowsAppsDirectory))
        {
            return [];
        }

        try
        {
            var directories = Directory
                .EnumerateDirectories(windowsAppsDirectory)
                .Where(directoryPath =>
                {
                    var directoryName = Path.GetFileName(directoryPath);
                    if (string.IsNullOrWhiteSpace(directoryName))
                    {
                        return false;
                    }

                    return directoryName.StartsWith(packageName + "_", StringComparison.OrdinalIgnoreCase) &&
                           directoryName.Contains("__" + publisherId, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(directoryPath => ParsePackageVersion(Path.GetFileName(directoryPath)))
                .ToList();

            return directories;
        }
        catch
        {
            return [];
        }
    }

    private static bool TrySplitPackageFamilyName(string packageFamilyName, out string packageName, out string publisherId)
    {
        packageName = string.Empty;
        publisherId = string.Empty;

        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return false;
        }

        var separatorIndex = packageFamilyName.LastIndexOf('_');
        if (separatorIndex <= 0 || separatorIndex >= packageFamilyName.Length - 1)
        {
            return false;
        }

        packageName = packageFamilyName[..separatorIndex].Trim();
        publisherId = packageFamilyName[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(publisherId);
    }

    private static Version ParsePackageVersion(string? packageIdentity)
    {
        if (string.IsNullOrWhiteSpace(packageIdentity))
        {
            return new Version(0, 0);
        }

        var identity = packageIdentity.Trim();
        var firstUnderscore = identity.IndexOf('_');
        if (firstUnderscore < 0 || firstUnderscore >= identity.Length - 1)
        {
            return new Version(0, 0);
        }

        var secondUnderscore = identity.IndexOf('_', firstUnderscore + 1);
        if (secondUnderscore < 0)
        {
            return new Version(0, 0);
        }

        var versionText = identity[(firstUnderscore + 1)..secondUnderscore];
        return Version.TryParse(versionText, out var version)
            ? version
            : new Version(0, 0);
    }

    private static bool TryExtractIconFromManifestInstallLocation(string installLocation, string appId, out byte[]? pngBytes)
    {
        pngBytes = null;
        var manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(manifestPath);
        }
        catch
        {
            return false;
        }

        var applicationNodes = document
            .Descendants()
            .Where(node => string.Equals(node.Name.LocalName, "Application", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (applicationNodes.Count == 0)
        {
            return false;
        }

        var applicationNode = applicationNodes.FirstOrDefault(node =>
            string.Equals(
                node.Attributes().FirstOrDefault(attr => string.Equals(attr.Name.LocalName, "Id", StringComparison.OrdinalIgnoreCase))?.Value,
                appId,
                StringComparison.OrdinalIgnoreCase)) ?? applicationNodes.First();

        var logoCandidates = new List<string>();
        CollectManifestLogoCandidates(applicationNode, logoCandidates);

        foreach (var rawLogoPath in logoCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var candidateFilePath in EnumerateManifestLogoFiles(installLocation, rawLogoPath))
            {
                if (TryConvertImageFileToPngBytes(candidateFilePath, out pngBytes))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void CollectManifestLogoCandidates(XElement applicationNode, List<string> output)
    {
        foreach (var node in applicationNode.DescendantsAndSelf())
        {
            var localName = node.Name.LocalName;
            if (!string.Equals(localName, "VisualElements", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(localName, "DefaultTile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var attributeName in ManifestLogoAttributePriority)
            {
                var attribute = node
                    .Attributes()
                    .FirstOrDefault(attr => string.Equals(attr.Name.LocalName, attributeName, StringComparison.OrdinalIgnoreCase));
                if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    output.Add(attribute.Value.Trim());
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateManifestLogoFiles(string installLocation, string rawLogoPath)
    {
        var normalizedAssetPath = NormalizeManifestAssetPath(rawLogoPath);
        if (string.IsNullOrWhiteSpace(normalizedAssetPath))
        {
            return [];
        }

        var baseCandidatePath = Path.GetFullPath(Path.Combine(installLocation, normalizedAssetPath));
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(baseCandidatePath))
        {
            files.Add(baseCandidatePath);
        }

        var directoryPath = Path.GetDirectoryName(baseCandidatePath);
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return files;
        }

        var baseName = Path.GetFileNameWithoutExtension(baseCandidatePath);
        var extension = Path.GetExtension(baseCandidatePath);
        var searchPattern = string.IsNullOrWhiteSpace(extension)
            ? baseName + ".*"
            : baseName + "*" + extension;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly))
            {
                files.Add(filePath);
            }
        }
        catch
        {
            // ignore inaccessible folders
        }

        return files
            .OrderByDescending(ScoreManifestIconCandidate)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeManifestAssetPath(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var cleaned = rawValue.Trim().Trim('"');
        if (cleaned.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (cleaned.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["ms-appx:///".Length..];
        }

        cleaned = cleaned
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        return cleaned;
    }

    private static int ScoreManifestIconCandidate(string filePath)
    {
        var score = 0;
        var fileName = Path.GetFileName(filePath);

        var targetSizeMatch = TargetSizeRegex.Match(fileName);
        if (targetSizeMatch.Success && int.TryParse(targetSizeMatch.Groups["n"].Value, out var targetSize))
        {
            score += targetSize * 100;
        }

        var scaleMatch = ScaleRegex.Match(fileName);
        if (scaleMatch.Success && int.TryParse(scaleMatch.Groups["n"].Value, out var scale))
        {
            score += scale * 10;
        }

        if (fileName.Contains("altform-unplated", StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }

        if (Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        return score;
    }

    private static bool TryConvertImageFileToPngBytes(string filePath, out byte[]? pngBytes)
    {
        pngBytes = null;
        try
        {
            using var image = Image.FromFile(filePath);
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            pngBytes = stream.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetPackagesByPackageFamily(
        string packageFamilyName,
        ref uint count,
        IntPtr[]? packageFullNames,
        ref uint bufferLength,
        char[]? buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetPackagePathByFullName(
        string packageFullName,
        ref uint pathLength,
        StringBuilder? path);
}

