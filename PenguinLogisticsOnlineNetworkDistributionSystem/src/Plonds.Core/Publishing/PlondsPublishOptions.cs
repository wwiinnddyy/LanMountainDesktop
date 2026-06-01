namespace Plonds.Core.Publishing;

public sealed record PlondsPublishOptions(
    string ReleaseTag,
    string Repository,
    string ManifestPath,
    string ChangedZipPath,
    string WorkDir,
    string S3KeyPrefix,
    PlondsS3ClientOptions S3);
