namespace Plonds.Shared.Models;

public sealed record PlondsFileEntry(
    string Action,
    string Hash,
    long Size,
    string HashAlgorithm = "sha256");
