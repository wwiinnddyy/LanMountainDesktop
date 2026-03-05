using System;
using System.Collections.Generic;
using System.Linq;

namespace LanMountainDesktop.Services;

public static class WorldClockTimeZoneCatalog
{
    public const int ClockCount = 4;

    private static readonly string[][] DefaultTimeZoneCandidates =
    [
        ["China Standard Time", "Asia/Shanghai"],
        ["GMT Standard Time", "Europe/London", "UTC"],
        ["AUS Eastern Standard Time", "Australia/Sydney"],
        ["Eastern Standard Time", "America/New_York"]
    ];

    private static readonly Dictionary<string, string[]> CrossPlatformAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = ["Asia/Shanghai"],
            ["Asia/Shanghai"] = ["China Standard Time"],
            ["GMT Standard Time"] = ["Europe/London", "UTC"],
            ["Europe/London"] = ["GMT Standard Time", "UTC"],
            ["AUS Eastern Standard Time"] = ["Australia/Sydney"],
            ["Australia/Sydney"] = ["AUS Eastern Standard Time"],
            ["Eastern Standard Time"] = ["America/New_York"],
            ["America/New_York"] = ["Eastern Standard Time"],
            ["UTC"] = ["Etc/UTC"],
            ["Etc/UTC"] = ["UTC"],
            ["Tokyo Standard Time"] = ["Asia/Tokyo"],
            ["Asia/Tokyo"] = ["Tokyo Standard Time"]
        };

    public static IReadOnlyList<string> NormalizeTimeZoneIds(IEnumerable<string>? configuredIds)
    {
        var available = TimeZoneInfo.GetSystemTimeZones();
        return NormalizeTimeZoneIds(configuredIds, available);
    }

    public static IReadOnlyList<string> NormalizeTimeZoneIds(
        IEnumerable<string>? configuredIds,
        IReadOnlyList<TimeZoneInfo> availableTimeZones)
    {
        var availableById = BuildAvailableTimeZoneLookup(availableTimeZones);
        var requested = configuredIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList() ?? [];

        var normalized = new List<string>(ClockCount);
        for (var index = 0; index < ClockCount; index++)
        {
            var requestedId = index < requested.Count ? requested[index] : null;
            var resolved = ResolveAvailableId(requestedId, availableById) ??
                           ResolveDefaultId(index, availableById) ??
                           TimeZoneInfo.Local.Id;
            normalized.Add(resolved);
        }

        return normalized;
    }

    public static TimeZoneInfo ResolveTimeZoneOrLocal(string? timeZoneId)
    {
        if (TryResolveTimeZone(timeZoneId, out var resolved))
        {
            return resolved;
        }

        return TimeZoneInfo.Local;
    }

    private static Dictionary<string, TimeZoneInfo> BuildAvailableTimeZoneLookup(
        IReadOnlyList<TimeZoneInfo> availableTimeZones)
    {
        return availableTimeZones
            .Where(zone => !string.IsNullOrWhiteSpace(zone.Id))
            .GroupBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveDefaultId(
        int slotIndex,
        IReadOnlyDictionary<string, TimeZoneInfo> availableById)
    {
        var clampedIndex = Math.Clamp(slotIndex, 0, ClockCount - 1);
        foreach (var candidateId in DefaultTimeZoneCandidates[clampedIndex])
        {
            var resolved = ResolveAvailableId(candidateId, availableById);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ResolveAvailableId(
        string? candidateId,
        IReadOnlyDictionary<string, TimeZoneInfo> availableById)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            return null;
        }

        var normalizedCandidate = candidateId.Trim();
        if (availableById.TryGetValue(normalizedCandidate, out var exact))
        {
            return exact.Id;
        }

        if (TryResolveTimeZone(normalizedCandidate, out var resolvedZone) &&
            availableById.TryGetValue(resolvedZone.Id, out var resolved))
        {
            return resolved.Id;
        }

        if (!CrossPlatformAliases.TryGetValue(normalizedCandidate, out var aliases))
        {
            return null;
        }

        foreach (var alias in aliases)
        {
            if (availableById.TryGetValue(alias, out var aliasZone))
            {
                return aliasZone.Id;
            }

            if (TryResolveTimeZone(alias, out var aliasResolvedZone) &&
                availableById.TryGetValue(aliasResolvedZone.Id, out var mappedAlias))
            {
                return mappedAlias.Id;
            }
        }

        return null;
    }

    private static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Local;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        var normalizedId = timeZoneId.Trim();
        if (TryFindTimeZone(normalizedId, out timeZone))
        {
            return true;
        }

        if (!CrossPlatformAliases.TryGetValue(normalizedId, out var aliases))
        {
            return false;
        }

        foreach (var alias in aliases)
        {
            if (TryFindTimeZone(alias, out timeZone))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Local;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
