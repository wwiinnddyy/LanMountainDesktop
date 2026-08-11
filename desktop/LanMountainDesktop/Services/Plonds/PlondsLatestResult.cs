namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsLatestResult(
    bool Success,
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version? LatestVersion,
    IReadOnlyList<PlondsManifestCandidate> Candidates,
    string? ErrorMessage)
{
    public static PlondsLatestResult Available(
        Version currentVersion,
        Version latestVersion,
        IReadOnlyList<PlondsManifestCandidate> candidates)
    {
        return new PlondsLatestResult(true, true, currentVersion, latestVersion, candidates, null);
    }

    public static PlondsLatestResult UpToDate(Version currentVersion, Version latestVersion)
    {
        return new PlondsLatestResult(true, false, currentVersion, latestVersion, [], null);
    }

    public static PlondsLatestResult Failed(Version currentVersion, string message)
    {
        return new PlondsLatestResult(false, false, currentVersion, null, [], message);
    }
}
