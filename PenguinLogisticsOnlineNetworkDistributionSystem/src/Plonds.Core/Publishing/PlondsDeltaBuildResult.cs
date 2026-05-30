namespace Plonds.Core.Publishing;

public sealed record PlondsDeltaBuildResult(
    string Platform,
    string ChangedZipPath,
    string ManifestPath,
    bool IsFullUpdate,
    bool RequiresCleanInstall,
    string CurrentVersion,
    string? BaselineVersion);
