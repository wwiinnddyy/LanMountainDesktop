namespace Plonds.Core.Publishing;

public sealed record PlondsPublishOptions(
    string Version,
    string AppArtifactsRoot,
    string InstallerArtifactsRoot,
    string OutputRoot,
    string PrivateKeyPath,
    string Channel = "stable",
    string? BaselineRoot = null,
    string? RepoBaseUrl = null,
    string? InstallerBaseUrl = null);
