using System;

namespace LanMountainDesktop.Models;

public static class DailyArtworkMirrorSources
{
    public const string Domestic = "Domestic";
    public const string Overseas = "Overseas";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Domestic, StringComparison.OrdinalIgnoreCase)
            ? Domestic
            : Overseas;
    }
}
