using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class LinuxDesktopEntryService
{
    private static readonly Regex FieldCodeRegex =
        new(@"%[fFuUdDnNickvm]", RegexOptions.Compiled);

    public StartMenuFolderNode Load()
    {
        var root = new StartMenuFolderNode("All Apps", string.Empty);
        if (!OperatingSystem.IsLinux())
        {
            return root;
        }

        var seenDesktopIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var applicationsRoot in EnumerateApplicationsRoots())
        {
            foreach (var desktopFilePath in EnumerateDesktopFilesSafe(applicationsRoot))
            {
                if (!TryParseDesktopEntry(desktopFilePath, applicationsRoot, out var appEntry))
                {
                    continue;
                }

                if (seenDesktopIds.Add(appEntry.RelativePath))
                {
                    root.Apps.Add(appEntry);
                }
            }
        }

        root.Apps.Sort((left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase));
        return root;
    }

    private static IEnumerable<string> EnumerateApplicationsRoots()
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
            candidates.Add(Path.Combine(dataHome, "applications"));
        }

        foreach (var dataDir in dataDirs)
        {
            candidates.Add(Path.Combine(dataDir, "applications"));
        }

        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            candidates.Add(Path.Combine(homeDirectory, ".local", "share", "flatpak", "exports", "share", "applications"));
        }

        candidates.Add("/var/lib/flatpak/exports/share/applications");
        candidates.Add("/var/lib/snapd/desktop/applications");

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateDesktopFilesSafe(string applicationsRoot)
    {
        try
        {
            return Directory.EnumerateFiles(applicationsRoot, "*.desktop", SearchOption.AllDirectories);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool TryParseDesktopEntry(string desktopFilePath, string applicationsRoot, out StartMenuAppEntry appEntry)
    {
        appEntry = null!;

        Dictionary<string, string> fields;
        try
        {
            fields = ReadDesktopEntryFields(desktopFilePath);
        }
        catch
        {
            return false;
        }

        if (!fields.TryGetValue("Type", out var entryType) ||
            !string.Equals(entryType, "Application", StringComparison.OrdinalIgnoreCase) ||
            GetBooleanField(fields, "NoDisplay") ||
            GetBooleanField(fields, "Hidden"))
        {
            return false;
        }

        var displayName = GetPreferredName(fields);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        if (!fields.TryGetValue("Exec", out var execValue) ||
            !TryParseExec(execValue, out var launchExecutable, out var launchArguments))
        {
            return false;
        }

        if (fields.TryGetValue("TryExec", out var tryExecValue) &&
            !string.IsNullOrWhiteSpace(tryExecValue) &&
            !CommandExists(tryExecValue))
        {
            return false;
        }

        var desktopFileId = BuildDesktopFileId(desktopFilePath, applicationsRoot);
        var iconValue = fields.TryGetValue("Icon", out var iconFieldValue)
            ? iconFieldValue
            : string.Empty;
        var workingDirectory = Path.IsPathRooted(launchExecutable)
            ? Path.GetDirectoryName(launchExecutable)
            : null;

        appEntry = new StartMenuAppEntry
        {
            DisplayName = displayName.Trim(),
            FilePath = desktopFilePath,
            RelativePath = desktopFileId,
            IconPngBytes = LinuxIconService.TryGetIconPngBytes(iconValue, Path.GetDirectoryName(desktopFilePath)),
            LaunchExecutable = launchExecutable,
            LaunchArguments = launchArguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory
        };
        return true;
    }

    private static Dictionary<string, string> ReadDesktopEntryFields(string desktopFilePath)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inDesktopEntrySection = false;
        foreach (var rawLine in File.ReadLines(desktopFilePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inDesktopEntrySection = string.Equals(line, "[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inDesktopEntrySection)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            fields[key] = value;
        }

        return fields;
    }

    private static bool GetBooleanField(IReadOnlyDictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) &&
               bool.TryParse(value, out var result) &&
               result;
    }

    private static string GetPreferredName(IReadOnlyDictionary<string, string> fields)
    {
        if (TryGetLocalizedField(fields, "Name", out var localizedName))
        {
            return localizedName;
        }

        return fields.TryGetValue("Name", out var fallbackName)
            ? fallbackName
            : string.Empty;
    }

    private static bool TryGetLocalizedField(IReadOnlyDictionary<string, string> fields, string baseKey, out string value)
    {
        value = string.Empty;
        var uiCulture = CultureInfo.CurrentUICulture;
        var candidates = new[]
        {
            $"{baseKey}[{uiCulture.Name}]",
            $"{baseKey}[{uiCulture.TwoLetterISOLanguageName}]"
        };

        foreach (var key in candidates)
        {
            if (fields.TryGetValue(key, out var localizedValue) &&
                !string.IsNullOrWhiteSpace(localizedValue))
            {
                value = localizedValue;
                return true;
            }
        }

        return false;
    }

    private static string BuildDesktopFileId(string desktopFilePath, string applicationsRoot)
    {
        var relativePath = Path.GetRelativePath(applicationsRoot, desktopFilePath)
            .Replace(Path.DirectorySeparatorChar, '-')
            .Replace(Path.AltDirectorySeparatorChar, '-');

        return relativePath.Trim();
    }

    private static bool TryParseExec(string execValue, out string launchExecutable, out List<string> launchArguments)
    {
        launchExecutable = string.Empty;
        launchArguments = [];

        var tokens = TokenizeExec(execValue);
        if (tokens.Count == 0)
        {
            return false;
        }

        var cleanedTokens = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var normalizedToken = token.Replace("%%", "%", StringComparison.Ordinal);
            if (normalizedToken.Length == 2 && normalizedToken[0] == '%')
            {
                continue;
            }

            normalizedToken = FieldCodeRegex.Replace(normalizedToken, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                continue;
            }

            cleanedTokens.Add(normalizedToken);
        }

        if (cleanedTokens.Count == 0)
        {
            return false;
        }

        launchExecutable = cleanedTokens[0];
        launchArguments = cleanedTokens.Skip(1).ToList();
        return true;
    }

    private static List<string> TokenizeExec(string execValue)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        char quoteChar = '\0';

        foreach (var c in execValue)
        {
            if ((c == '"' || c == '\'') &&
                (!inQuotes || quoteChar == c))
            {
                if (inQuotes)
                {
                    inQuotes = false;
                    quoteChar = '\0';
                }
                else
                {
                    inQuotes = true;
                    quoteChar = c;
                }

                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static bool CommandExists(string command)
    {
        var trimmedCommand = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmedCommand))
        {
            return false;
        }

        if (Path.IsPathRooted(trimmedCommand))
        {
            return File.Exists(trimmedCommand);
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pathEntry in pathEntries)
        {
            try
            {
                var candidate = Path.Combine(pathEntry, trimmedCommand);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return false;
    }
}
