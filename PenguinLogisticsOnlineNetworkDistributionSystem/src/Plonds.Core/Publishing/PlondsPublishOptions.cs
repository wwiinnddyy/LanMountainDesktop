namespace Plonds.Core.Publishing;

public sealed record PlondsPublishOptions(
    string ReleaseTag,
    string Repository,
    string ManifestPath,
    string ChangedZipPath,
    string FilesZipPath,
    string WorkDir,
    string S3KeyPrefix,
    PlondsS3ClientOptions S3)
{
    public int DirectoryUploadConcurrency { get; init; } = 4;
}
