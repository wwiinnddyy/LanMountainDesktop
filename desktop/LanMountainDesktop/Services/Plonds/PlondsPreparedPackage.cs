namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsPreparedPackage(
    Version Version,
    PlondsPackageMode Mode,
    string ManifestPath,
    string? ChangedZipPath,
    string? ChangedDirectory,
    string? FilesZipPath,
    string? FilesDirectory);
