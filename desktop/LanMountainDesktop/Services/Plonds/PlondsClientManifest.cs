namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsClientManifest(
    string FormatVersion,
    string CurrentVersion,
    string PreviousVersion,
    bool IsFullUpdate,
    bool RequiresCleanInstall,
    string Channel,
    string Platform,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, PlondsClientFileEntry> FilesMap,
    IReadOnlyDictionary<string, PlondsClientChangedFileEntry> ChangedFilesMap,
    IReadOnlyDictionary<string, string> Checksums,
    PlondsClientDownloads? Downloads,
    IReadOnlyList<PlondsSourceDescriptor>? Sources);

internal sealed record PlondsClientFileEntry(
    string Action,
    string Hash,
    long Size,
    string HashAlgorithm = "sha256");

internal sealed record PlondsClientChangedFileEntry(
    string ArchivePath,
    string Hash,
    long Size,
    string HashAlgorithm = "sha256");
