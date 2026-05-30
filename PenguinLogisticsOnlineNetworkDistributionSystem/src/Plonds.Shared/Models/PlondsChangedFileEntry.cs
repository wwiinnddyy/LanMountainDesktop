namespace Plonds.Shared.Models;

public sealed record PlondsChangedFileEntry(
    string ArchivePath,
    string Hash,
    long Size,
    string HashAlgorithm = "sha256");
