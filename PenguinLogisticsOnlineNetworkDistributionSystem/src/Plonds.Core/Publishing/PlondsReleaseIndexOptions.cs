namespace Plonds.Core.Publishing;

public sealed record PlondsReleaseIndexOptions(
    string ReleaseTag,
    string Version,
    string Channel,
    string PlatformSummariesDirectory,
    string OutputRoot,
    string PrivateKeyPath);
