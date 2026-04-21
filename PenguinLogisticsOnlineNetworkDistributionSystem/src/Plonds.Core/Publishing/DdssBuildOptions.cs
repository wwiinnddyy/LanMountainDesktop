namespace Plonds.Core.Publishing;

public sealed record DdssBuildOptions(
    string ReleaseTag,
    string AssetsDirectory,
    string OutputRoot,
    string PrivateKeyPath,
    string Repository,
    string? S3BaseUrl = null);
