using System.Text.Json.Serialization;

namespace Plonds.Shared.Models;

public sealed record PlondsDownloadInfo(
    string ReleaseTag,
    [property: JsonPropertyName("github")]
    PlondsGitHubDownloadInfo GitHub,
    PlondsS3DownloadInfo S3);

public sealed record PlondsGitHubDownloadInfo(
    string ReleaseUrl,
    string ManifestUrl,
    string ChangedZipUrl,
    string FilesZipUrl);

public sealed record PlondsS3DownloadInfo(
    string Bucket,
    string Prefix,
    string ManifestKey,
    string ManifestUrl,
    string ChangedZipKey,
    string ChangedZipUrl,
    string ChangedFolderKey,
    string ChangedFolderUrl,
    string FilesZipKey,
    string FilesZipUrl,
    string FilesFolderKey,
    string FilesFolderUrl);
