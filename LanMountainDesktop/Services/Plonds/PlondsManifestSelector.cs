namespace LanMountainDesktop.Services.Plonds;

internal static class PlondsManifestSelector
{
    public static PlondsManifestCandidate? SelectHighestVersion(IEnumerable<PlondsManifestCandidate> candidates)
    {
        return SelectHighestVersionCandidates(candidates).FirstOrDefault();
    }

    public static IReadOnlyList<PlondsManifestCandidate> SelectHighestVersionCandidates(IEnumerable<PlondsManifestCandidate> candidates)
    {
        var usableCandidates = candidates
            .Where(candidate => TryParseVersion(candidate.Manifest.CurrentVersion, out _))
            .OrderByDescending(candidate => ParseVersion(candidate.Manifest.CurrentVersion))
            .ThenByDescending(candidate => candidate.Source.Priority)
            .ToArray();

        var highest = usableCandidates.FirstOrDefault();
        if (highest is null)
        {
            return [];
        }

        var highestVersion = ParseVersion(highest.Manifest.CurrentVersion);
        return usableCandidates
            .Where(candidate => ParseVersion(candidate.Manifest.CurrentVersion).CompareTo(highestVersion) == 0)
            .ToArray();
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Version.TryParse(value.Trim().TrimStart('v', 'V'), out var parsed))
        {
            return false;
        }

        version = parsed.Revision >= 0
            ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), parsed.Revision)
            : new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    private static Version ParseVersion(string value)
    {
        return TryParseVersion(value, out var version) ? version : new Version(0, 0, 0);
    }
}
