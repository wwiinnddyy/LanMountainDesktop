namespace Plonds.Shared.Models;

public sealed record DdssManifest(
    string FormatVersion,
    string ReleaseTag,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DdssAssetEntry> Assets);
