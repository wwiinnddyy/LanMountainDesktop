namespace Plonds.Shared.Models;

public sealed record DdssAssetEntry(
    string AssetId,
    string FileName,
    string Sha256,
    long Size,
    IReadOnlyList<DdssMirrorEntry> Mirrors);
