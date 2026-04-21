namespace Plonds.Shared.Models;

public sealed record PlondsReleaseManifest(
    string FormatVersion,
    string ReleaseTag,
    string Version,
    string Channel,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PlondsReleasePlatformEntry> Platforms);
