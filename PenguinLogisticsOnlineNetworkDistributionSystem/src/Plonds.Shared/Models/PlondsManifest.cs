namespace Plonds.Shared.Models;

public sealed record PlondsManifest(
    string FormatVersion,
    string CurrentVersion,
    string PreviousVersion,
    bool IsFullUpdate,
    bool RequiresCleanInstall,
    string Channel,
    string Platform,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, PlondsFileEntry> FilesMap,
    IReadOnlyDictionary<string, PlondsChangedFileEntry> ChangedFilesMap,
    IReadOnlyDictionary<string, string> Checksums,
    PlondsDownloadInfo? Downloads = null,
    IReadOnlyList<PlondsSourceDescriptor>? Sources = null);
