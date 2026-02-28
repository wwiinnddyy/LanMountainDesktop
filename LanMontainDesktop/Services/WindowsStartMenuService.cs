using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LanMontainDesktop.Models;

namespace LanMontainDesktop.Services;

public sealed class WindowsStartMenuService
{
    private static readonly CompareInfo SortCompareInfo = CultureInfo.GetCultureInfo("zh-CN").CompareInfo;
    private static readonly CompareOptions SortOptions =
        CompareOptions.IgnoreCase |
        CompareOptions.IgnoreKanaType |
        CompareOptions.IgnoreWidth |
        CompareOptions.StringSort;

    private static readonly HashSet<string> SupportedEntryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk",
        ".url",
        ".appref-ms"
    };

    public StartMenuFolderNode Load()
    {
        var root = new StartMenuFolderNode("All Apps", string.Empty);
        if (!OperatingSystem.IsWindows())
        {
            return root;
        }

        foreach (var programsRoot in EnumerateProgramsRoots())
        {
            try
            {
                if (!Directory.Exists(programsRoot))
                {
                    continue;
                }

                var scannedRoot = ScanFolder(programsRoot, programsRoot, "All Apps");
                MergeFolder(root, scannedRoot);
            }
            catch
            {
                // Ignore unreadable start menu roots to keep launcher rendering resilient.
            }
        }

        NormalizeFolderHierarchy(root);
        SortFolder(root);
        return root;
    }

    private static IEnumerable<string> EnumerateProgramsRoots()
    {
        var userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);

        var candidates = new[]
        {
            Path.Combine(userStartMenu, "Programs"),
            Path.Combine(commonStartMenu, "Programs")
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static StartMenuFolderNode ScanFolder(string folderPath, string rootPath, string? nameOverride = null)
    {
        var relativePath = Path.GetRelativePath(rootPath, folderPath);
        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            relativePath = string.Empty;
        }

        var folder = new StartMenuFolderNode(
            nameOverride ?? Path.GetFileName(folderPath),
            relativePath);

        foreach (var subFolderPath in Directory.EnumerateDirectories(folderPath))
        {
            var folderName = Path.GetFileName(subFolderPath);
            if (folderName.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            folder.Folders.Add(ScanFolder(subFolderPath, rootPath));
        }

        foreach (var filePath in Directory.EnumerateFiles(folderPath))
        {
            var extension = Path.GetExtension(filePath);
            if (!SupportedEntryExtensions.Contains(extension))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var normalizedName = fileName.Replace('_', ' ').Trim();
            folder.Apps.Add(new StartMenuAppEntry
            {
                DisplayName = normalizedName,
                FilePath = filePath,
                RelativePath = Path.GetRelativePath(rootPath, filePath),
                IconPngBytes = OperatingSystem.IsWindows()
                    ? WindowsIconService.TryGetIconPngBytes(filePath)
                    : null
            });
        }

        return folder;
    }

    private static void MergeFolder(StartMenuFolderNode target, StartMenuFolderNode source)
    {
        var appPathSet = new HashSet<string>(
            target.Apps.Select(app => app.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var app in source.Apps)
        {
            if (appPathSet.Add(app.RelativePath))
            {
                target.Apps.Add(app);
            }
        }

        foreach (var sourceFolder in source.Folders)
        {
            var existing = target.Folders.FirstOrDefault(folder =>
                string.Equals(folder.Name, sourceFolder.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                target.Folders.Add(sourceFolder);
                continue;
            }

            MergeFolder(existing, sourceFolder);
        }
    }

    private static void SortFolder(StartMenuFolderNode folder)
    {
        folder.Folders.Sort((left, right) => CompareDisplayName(left.Name, right.Name));
        folder.Apps.Sort((left, right) => CompareDisplayName(left.DisplayName, right.DisplayName));
        foreach (var child in folder.Folders)
        {
            SortFolder(child);
        }
    }

    private static int CompareDisplayName(string? left, string? right)
    {
        var normalizedLeft = NormalizeForSort(left);
        var normalizedRight = NormalizeForSort(right);
        return SortCompareInfo.Compare(normalizedLeft, normalizedRight, SortOptions);
    }

    private static string NormalizeForSort(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "~";
        }

        return text.Trim();
    }

    private static void NormalizeFolderHierarchy(StartMenuFolderNode root)
    {
        if (root.Folders.Count == 0)
        {
            return;
        }

        var normalizedChildren = new List<StartMenuFolderNode>();
        foreach (var child in root.Folders)
        {
            var normalizedChild = CollapseSingleChildFolders(child);
            MergeIntoFolderList(normalizedChildren, normalizedChild);
        }

        root.Folders.Clear();
        root.Folders.AddRange(normalizedChildren);
    }

    private static StartMenuFolderNode CollapseSingleChildFolders(StartMenuFolderNode folder)
    {
        if (folder.Folders.Count > 0)
        {
            var normalizedChildren = new List<StartMenuFolderNode>();
            foreach (var child in folder.Folders)
            {
                var normalizedChild = CollapseSingleChildFolders(child);
                MergeIntoFolderList(normalizedChildren, normalizedChild);
            }

            folder.Folders.Clear();
            folder.Folders.AddRange(normalizedChildren);
        }

        while (folder.Apps.Count == 0 && folder.Folders.Count == 1)
        {
            folder = folder.Folders[0];
        }

        return folder;
    }

    private static void MergeIntoFolderList(List<StartMenuFolderNode> folders, StartMenuFolderNode source)
    {
        var existing = folders.FirstOrDefault(folder =>
            string.Equals(folder.Name, source.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            folders.Add(source);
            return;
        }

        MergeFolder(existing, source);
    }
}
