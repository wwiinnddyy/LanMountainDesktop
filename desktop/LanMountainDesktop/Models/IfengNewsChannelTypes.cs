using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public static class IfengNewsChannelTypes
{
    public const string Comprehensive = "Comprehensive";
    public const string Mainland = "Mainland";
    public const string Taiwan = "Taiwan";

    public static IReadOnlyList<string> SupportedValues { get; } =
    [
        Comprehensive,
        Mainland,
        Taiwan
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

        return Comprehensive;
    }
}
