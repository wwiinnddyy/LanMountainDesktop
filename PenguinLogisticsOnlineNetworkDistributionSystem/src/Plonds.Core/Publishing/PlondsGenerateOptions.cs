namespace Plonds.Core.Publishing;

public sealed record PlondsGenerateOptions(
    string CurrentVersion,
    string CurrentDirectory,
    string Platform,
    string OutputRoot,
    string PreviousVersion = "0.0.0",
    string? PreviousDirectory = null,
    string Channel = "stable",
    string? DistributionId = null,
    string? RepoBaseUrl = null,
    string? FileMapUrl = null,
    string? FileMapSignatureUrl = null,
    string? InstallerDirectory = null,
    string? InstallerBaseUrl = null);
