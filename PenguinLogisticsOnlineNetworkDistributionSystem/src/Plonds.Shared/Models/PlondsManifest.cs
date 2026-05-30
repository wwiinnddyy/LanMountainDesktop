using System.Text.Json.Serialization;

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
    string CompareMethod,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? HashAlgorithm,
    IReadOnlyDictionary<string, PlondsFileEntry> FilesMap,
    IReadOnlyDictionary<string, PlondsChangedFileEntry> ChangedFilesMap,
    IReadOnlyDictionary<string, string> Checksums);
