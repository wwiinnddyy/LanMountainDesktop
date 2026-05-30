namespace Plonds.Core.Publishing;

public sealed record PlondsCommitDeltaBuildResult(
    string Platform,
    string ChangedZipPath,
    string ManifestPath,
    bool IsFullUpdate,
    bool RequiresCleanInstall,
    bool FellBackToFileCompare,
    string CurrentVersion,
    string? BaselineVersion,
    IReadOnlyList<string> ChangedSourceFiles,
    IReadOnlyList<string> MappedArtifactFiles);
