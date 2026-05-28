namespace Plonds.Shared.Models;

public sealed record PlondsManifest(
    string FormatVersion,
    string ReleaseTag,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PlondsAssetEntry> Assets);
