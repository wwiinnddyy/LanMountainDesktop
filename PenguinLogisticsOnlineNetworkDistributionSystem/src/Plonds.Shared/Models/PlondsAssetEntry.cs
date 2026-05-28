namespace Plonds.Shared.Models;

public sealed record PlondsAssetEntry(
    string AssetId,
    string FileName,
    string Sha256,
    long Size,
    IReadOnlyList<PlondsMirrorEntry> Mirrors);
