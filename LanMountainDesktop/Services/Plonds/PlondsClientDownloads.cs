namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsClientDownloads(
    string? ReleaseTag,
    PlondsGitHubDownloads? GitHub,
    PlondsS3Downloads? S3);

internal sealed record PlondsGitHubDownloads(
    string? ReleaseUrl,
    string? ManifestUrl,
    string? ChangedZipUrl,
    string? FilesZipUrl);

internal sealed record PlondsS3Downloads(
    string? Bucket,
    string? Prefix,
    string? ManifestKey,
    string? ManifestUrl,
    string? ChangedZipKey,
    string? ChangedZipUrl,
    string? ChangedFolderKey,
    string? ChangedFolderUrl,
    string? FilesZipKey,
    string? FilesZipUrl,
    string? FilesFolderKey,
    string? FilesFolderUrl);
