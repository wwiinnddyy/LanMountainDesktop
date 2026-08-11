using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public static class Stcn24ForumSourceTypes
{
    public const string LatestCreated = "LatestCreated";
    public const string LatestActivity = "LatestActivity";
    public const string MostReplies = "MostReplies";
    public const string EarliestCreated = "EarliestCreated";
    public const string EarliestActivity = "EarliestActivity";
    public const string LeastReplies = "LeastReplies";
    public const string FrontpageLatest = "FrontpageLatest";
    public const string FrontpageEarliest = "FrontpageEarliest";

    public static IReadOnlyList<string> SupportedValues { get; } =
    [
        LatestCreated,
        LatestActivity,
        MostReplies,
        EarliestCreated,
        EarliestActivity,
        LeastReplies,
        FrontpageLatest,
        FrontpageEarliest
    ];

    public static string Normalize(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        foreach (var supported in SupportedValues)
        {
            if (string.Equals(candidate, supported, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return LatestCreated;
    }
}
