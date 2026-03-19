using System;
using System.Collections.Generic;
using System.Linq;

namespace LanMountainDesktop.Models;

public static class OfficeRecentDocumentSourceTypes
{
    public const string Registry = "registry";
    public const string RecentFolders = "recent_folders";
    public const string JumpLists = "jump_lists";

    public static IReadOnlyList<string> SupportedValues { get; } =
    [
        Registry,
        RecentFolders,
        JumpLists
    ];

    public static IReadOnlyList<string> DefaultValues => SupportedValues;

    public static IReadOnlyList<string> NormalizeValues(IEnumerable<string>? values, bool useDefaultWhenEmpty)
    {
        if (values is null)
        {
            return useDefaultWhenEmpty ? DefaultValues : Array.Empty<string>();
        }

        var normalized = values
            .Select(NormalizeValue)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0 && useDefaultWhenEmpty)
        {
            return DefaultValues;
        }

        return normalized;
    }

    private static string? NormalizeValue(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Registry => Registry,
            RecentFolders => RecentFolders,
            JumpLists => JumpLists,
            _ => null
        };
    }
}
