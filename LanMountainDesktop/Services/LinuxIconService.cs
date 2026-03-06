using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LanMountainDesktop.Services;

internal static class LinuxIconService
{
    private static readonly string[] SupportedRasterExtensions =
    [
        ".png",
        ".ico"
    ];

    private static readonly Regex SizeDirectoryRegex =
        new(@"(?<size>\d{1,4})x\d{1,4}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, string?> IconPathCache = new(StringComparer.OrdinalIgnoreCase);

    public static byte[]? TryGetIconPngBytes(string? iconKey, string? desktopFileDirectory = null)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(iconKey))
        {
            return null;
        }

        foreach (var candidatePath in ResolveIconCandidates(iconKey.Trim(), desktopFileDirectory))
        {
            if (TryReadIconBytes(candidatePath, out var bytes))
            {
                return bytes;
            }
        }

        return null;
    }

    private static IEnumerable<string> ResolveIconCandidates(string iconKey, string? desktopFileDirectory)
    {
        if (Path.HasExtension(iconKey))
        {
            var directPath = ExpandHome(iconKey);
            if (Path.IsPathRooted(directPath))
            {
                yield return directPath;
            }
            else if (!string.IsNullOrWhiteSpace(desktopFileDirectory))
            {
                yield return Path.GetFullPath(Path.Combine(desktopFileDirectory, directPath));
            }

            yield break;
        }

        var resolvedThemePath = ResolveThemedIconPath(iconKey);
        if (!string.IsNullOrWhiteSpace(resolvedThemePath))
        {
            yield return resolvedThemePath;
        }
    }

    private static string? ResolveThemedIconPath(string iconName)
    {
        return IconPathCache.GetOrAdd(iconName, static key => FindBestMatchingIconPath(key));
    }

    private static string? FindBestMatchingIconPath(string iconName)
    {
        var candidates = new List<(string Path, int Score)>();
        foreach (var iconRoot in EnumerateIconRoots())
        {
            foreach (var extension in SupportedRasterExtensions)
            {
                foreach (var candidatePath in EnumerateFilesSafe(iconRoot, iconName + extension))
                {
                    candidates.Add((candidatePath, ScoreIconPath(candidatePath)));
                }
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Length)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateIconRoots()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome) && !string.IsNullOrWhiteSpace(homeDirectory))
        {
            dataHome = Path.Combine(homeDirectory, ".local", "share");
        }

        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share")
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(dataHome))
        {
            candidates.Add(Path.Combine(dataHome, "icons"));
            candidates.Add(Path.Combine(dataHome, "pixmaps"));
        }

        foreach (var dataDir in dataDirs)
        {
            candidates.Add(Path.Combine(dataDir, "icons"));
            candidates.Add(Path.Combine(dataDir, "pixmaps"));
        }

        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            candidates.Add(Path.Combine(homeDirectory, ".icons"));
            candidates.Add(Path.Combine(homeDirectory, ".local", "share", "flatpak", "exports", "share", "icons"));
        }

        candidates.Add("/var/lib/flatpak/exports/share/icons");
        candidates.Add("/var/lib/snapd/desktop/icons");

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string rootPath, string fileName)
    {
        try
        {
            return Directory.EnumerateFiles(rootPath, fileName, SearchOption.AllDirectories);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool TryReadIconBytes(string filePath, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var extension = Path.GetExtension(filePath);
            if (!SupportedRasterExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
                !File.Exists(filePath))
            {
                return false;
            }

            bytes = File.ReadAllBytes(filePath);
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static int ScoreIconPath(string filePath)
    {
        var score = 0;
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            score += 4_000;
        }
        else if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            score += 2_000;
        }

        if (filePath.Contains($"{Path.DirectorySeparatorChar}hicolor{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            score += 8_000;
        }

        if (filePath.Contains($"{Path.DirectorySeparatorChar}apps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            score += 1_000;
        }

        var match = SizeDirectoryRegex.Match(filePath);
        if (match.Success &&
            int.TryParse(match.Groups["size"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            score += Math.Min(size, 512);
        }

        return score;
    }

    private static string ExpandHome(string path)
    {
        if (!path.StartsWith("~", StringComparison.Ordinal))
        {
            return path;
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return path;
        }

        return path.Length == 1
            ? homeDirectory
            : Path.Combine(homeDirectory, path[2..]);
    }
}
