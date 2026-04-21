namespace Plonds.Shared.Models;

public sealed record PlondsMirrorAsset(
    string Platform,
    string Arch,
    string Url,
    string? FileName = null,
    string? Sha256 = null,
    long Size = 0);
