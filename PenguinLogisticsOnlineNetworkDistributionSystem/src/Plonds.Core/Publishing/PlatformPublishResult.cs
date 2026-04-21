namespace Plonds.Core.Publishing;

public sealed record PlatformPublishResult(
    string Platform,
    string DistributionId,
    string CurrentAppDirectory,
    string? PreviousDirectory,
    string PreviousVersion,
    string FileMapPath,
    string SignaturePath,
    string DistributionPath,
    string LatestPath,
    IReadOnlyList<string> InstallerFiles);
