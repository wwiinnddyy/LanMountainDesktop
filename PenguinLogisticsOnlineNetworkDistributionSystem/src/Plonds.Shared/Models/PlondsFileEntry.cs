namespace Plonds.Shared.Models;

public sealed record PlondsFileEntry(
    string Path,
    string Op,
    string ContentHash,
    long Size,
    string Mode,
    string? ObjectKey = null,
    string? Compression = null,
    string? PatchBaseHash = null,
    string? PatchObjectKey = null);

